using Godot;

namespace AiInfiniteJourney;

/// <summary>
/// Arcade-style car controller. Reads the "forward", "back", "left", "right",
/// and "brake" input actions and applies force/steering to a RigidBody3D.
///
/// This is intentionally simple for Phase 1: driveable car + camera + road.
/// Phase 2 will swap in a real wheel/raycast suspension. Phase 3+ stays the
/// same — the procedural generator just drops the car into the world.
///
/// Notable fixes from the 2026-07-07 debug session (where the car did not
/// move under WASD despite input being correctly received):
///   1. Sleeping + CanSleep are disabled. RigidBody3D falls asleep on its own
///      when idle; for a player-controlled car we never want that.
///   2. ContactMonitor is enabled and MaxContactsReported is set. Without
///      these, GetContactCount() always returns 0 and contact events are
///      never delivered, making physics-related bugs very hard to debug.
///   3. The collision shape is added in PlayerCar via CallDeferred so the
///      physics server registers it (otherwise the body has no colliders
///      and ApplyCentralForce goes into the void).
///   4. The ChaseCamera offset was changed to (0, 2.5, 6) to follow the
///      new Y position of the body.
/// </summary>
[GlobalClass]
public partial class CarController : RigidBody3D
{
    [Export] public float EngineForce = 80000.0f;
    [Export] public float BrakeForce = 30.0f;
    [Export] public float MaxSteerAngleDeg = 35.0f;
    [Export] public float SteerSpeedDegPerSec = 180.0f;
    [Export] public float SteerReturnDegPerSec = 240.0f;
    [Export] public float MaxForwardSpeed = 14.0f;
    [Export] public float MaxReverseSpeed = 5.0f;
    [Export] public float CruiseThrottle = 0.3f;
    [Export] public float LateralGripFactor = 3.0f;
    [Export] public float Downforce = 20.0f;
    [Export] public float AntiBounceForce = 50.0f;
    [Export] public NodePath? ChunkedRoadManagerPath { get; set; }

    // Off-road penalty tuning
    private const float OffRoadThrottleScale = 0.25f;  // 25% throttle when wheels off the road
    private const float OffRoadLateralDrag   = 5.0f;   // Strong sideways drag off-road
    // Soft "tractor beam" pulling the car back toward the road centerline
    // when it's off-road. Catches cases where the car has escaped past
    // the side walls (e.g. from a high-speed impact or spawn glitch)
    // and would otherwise be stranded on the grass.
    private const float OffRoadPullStrength = 50000.0f;  // Newtons at 1m off-road; scales with distance
    // Auto-steer assist: when off-road, add a steering nudge pointing
    // toward the road centerline so the car naturally returns to the
    // road even without driver input. Disabled if user is steering hard
    // (|steerInput| > 0.5) — don't fight the player.
    private const float OffRoadAutoSteerDeg = 15.0f;     // degrees of steering nudge
    // Last-resort recovery: if the car has been off-road and barely moving
    // (likely wedged against an obstacle) for this many seconds, teleport
    // it back onto the road centerline at its current Z. Prevents the
    // car from being permanently stranded.
    private const float OffRoadStuckTeleportSeconds = 3.0f;
    private float _offRoadStuckTimer;
    // Anti-rollover: if the car's local Y points more than this many
    // degrees away from world up, apply a restoring torque. Catches
    // the car flipping over from a high-speed crash.
    private const float AntiRolloverAngleDeg = 30.0f;  // Lowered from 45: catch flips earlier
    private const float AntiRolloverTorque = 15000.0f;   // Nearly doubled from 8000: 8000 wasn't enough to overcome collision-induced angular momentum at 14 m/s
    // Auto-right: if the car is upside down (tilt > 90°) for this long, snap upright.
    private const float AutoRightAngleDeg = 90.0f;
    private const float AutoRightSeconds = 2.0f;
    // General stuck-recovery: if the car hasn't moved more than this threshold
    // for N seconds (regardless of on/off-road), apply a forward boost to
    // break free from obstacles wedged on the road edge. The off-road teleport
    // only fires when off-road, so on-road stuck states need this fallback.
    private const float StuckSpeedThreshold = 1.0f;
    private const float StuckBoostSeconds = 5.0f;
    private const float StuckBoostImpulse = 25000.0f;
    private const int StuckBoostRetries = 2;  // After N boosts, teleport to centerline

    private float _steerAngleDeg;
    private bool _wasOffRoad;                            // for state-change logging
    private int _physicsTick;                            // total physics ticks, for periodic reports
    private int _offRoadDebounce;                        // consecutive frames in current off-road state
    private float _flipTimer;                            // seconds spent upside down
    private float _stuckTimer;                           // seconds at near-zero speed
    private int _stuckBoostCount;                        // consecutive boosts without making progress
    private Vector3 _lastStuckPos;                       // position when last boost/teleport fired
    private float _sinceStuckAction;                     // seconds since last stuck boost/teleport

    public override void _Ready()
    {
        // Center of mass low + forward helps stability. Must enable Custom mode
        // before writing to CenterOfMass, otherwise Godot throws.
        CenterOfMassMode = CenterOfMassModeEnum.Custom;
        var com = CenterOfMass;
        com.Y = -0.4f;
        CenterOfMass = com;

        // (1) Don't sleep: a player-controlled body must always respond.
        Sleeping = false;
        CanSleep = false;
        // Make sure the body isn't accidentally frozen or axis-locked.
        Freeze = false;

        // (2) Enable contact reporting so GetContactCount() returns a useful
        // value and BodyEntered/BodyExited fire when the car touches the world.
        ContactMonitor = true;
        MaxContactsReported = 8;

        // (3) Continuous collision detection so small forces aren't lost on
        // single-frame contacts. (Original "car doesn't move" debug, 2026-07-07.)
        ContinuousCd = true;

        // Bumper physics: low bounce + low friction prevents high-speed
        // collisions from launching the car into the air. Without this,
        // hitting a building at 18 m/s flips the car (tilt=132° observed).
        PhysicsMaterialOverride = new PhysicsMaterial
        {
            Friction = 0.3f,
            Bounce = 0.0f,
        };

        GD.Print($"[CarController] ready. layer={CollisionLayer} mask={CollisionMask} mass={Mass}");
    }

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;

        // --- Anti-bounce: kill vertical velocity when grounded ---
        // Prevents the "earthquake" effect where RigidBody3D bounces on
        // terrain contacts. Only applies when the car has ground contact.
        var contactCount = GetContactCount();
        if (contactCount > 0 && LinearVelocity.Y > -0.1f)
        {
            var vY = LinearVelocity.Y;
            if (Mathf.Abs(vY) > 0.5f)
            {
                LinearVelocity = new Vector3(LinearVelocity.X, vY * 0.3f, LinearVelocity.Z);
            }
        }

        // --- Steering ---
        var steerInput = Input.GetActionStrength("right") - Input.GetActionStrength("left");
        var maxSteer = MaxSteerAngleDeg;
        var targetSteer = steerInput * maxSteer;

        if (Mathf.Abs(steerInput) > 0.01f)
            _steerAngleDeg = Mathf.MoveToward(_steerAngleDeg, targetSteer, SteerSpeedDegPerSec * dt);
        else
            _steerAngleDeg = Mathf.MoveToward(_steerAngleDeg, 0.0f, SteerReturnDegPerSec * dt);

        // --- Throttle / brake / reverse ---
        var rawThrottle = Input.GetActionStrength("forward");
        var rawReverse = Input.GetActionStrength("back");
        // Cruise throttle: when the player isn't steering, apply a gentle
        // forward push so the car drives on flat road surfaces.
        var throttle = Mathf.Max(rawThrottle, CruiseThrottle);
        var reverse = Mathf.Max(rawReverse, 0f);
        var braking = Input.IsActionPressed("brake");

        // Forward force along the car's local -Z (Godot convention: -Z is forward)
        var forwardDir = -GlobalTransform.Basis.Z;
        var fwdSpeed = LinearVelocity.Dot(forwardDir);

        // Off-road penalty: throttle cut + lateral drag + tractor-beam pull
        // back toward the road centerline. If the car has been stranded
        // off-road and barely moving for several seconds (wedged against
        // an obstacle), teleport it back onto the road.
        var offRoad = false;
        ChunkedRoadManager? roadMgr = null;
        if (ChunkedRoadManagerPath is not null)
        {
            roadMgr = GetNodeOrNull<ChunkedRoadManager>(ChunkedRoadManagerPath);
            if (roadMgr is not null)
            {
                var (dist, halfWidth, isOnRoad) = roadMgr.GetRoadDistance(GlobalPosition);
                offRoad = !isOnRoad;
                if (offRoad)
                {
                    throttle *= OffRoadThrottleScale;
                    reverse *= OffRoadThrottleScale;

                    // Tractor beam toward nearest centerline point.
                    var pull = roadMgr.GetPullVector(GlobalPosition);
                    var pullMag = pull.Length();
                    if (pullMag > 0.01f)
                    {
                        var clampedDist = Mathf.Min(pullMag, halfWidth * 2f);
                        ApplyCentralForce(
                            -pull.Normalized() * OffRoadPullStrength * clampedDist);
                    }

                    // Stuck recovery: teleport back after N seconds of near-zero speed.
                    if (Mathf.Abs(fwdSpeed) < 1.0f)
                        _offRoadStuckTimer += dt;
                    else
                        _offRoadStuckTimer = 0f;
                    if (_offRoadStuckTimer >= OffRoadStuckTeleportSeconds)
                    {
                        var p = roadMgr.GetPullVector(GlobalPosition);
                        if (p.Length() > 0.01f)
                        {
                            var centerline = GlobalPosition - p;
                            GlobalPosition = new Vector3(centerline.X, centerline.Y + 1.5f, centerline.Z);
                            LinearVelocity = new Vector3(0, 0, -0.5f);
                            _offRoadStuckTimer = 0f;
                            GD.Print($"[Car] off-road stuck {OffRoadStuckTeleportSeconds}s — " +
                                     $"teleported to ({GlobalPosition.X:F1}, {GlobalPosition.Z:F1})");
                        }
                    }
                }
                else
                    _offRoadStuckTimer = 0f;
            }
        }

        // At low speed (< 1 m/s), the physics solver can lose force-based
        // acceleration because high downforce + terrain friction dominates
        // contact resolution. Use direct velocity override on the forward
        // axis to "kick start" the car. Above 1 m/s, use ApplyCentralForce
        // for standard physics.
        if (throttle > 0.01f && fwdSpeed < MaxForwardSpeed)
        {
            if (fwdSpeed < 1.0f)
            {
                // Velocity-based low-speed boost: directly accelerate forward
                // axis so the car can overcome terrain friction.
                var targetFwdSpeed = Mathf.Min(fwdSpeed + EngineForce * throttle * dt / Mass, MaxForwardSpeed);
                var bodyVel = LinearVelocity;
                bodyVel += forwardDir * (targetFwdSpeed - fwdSpeed);
                LinearVelocity = bodyVel;
            }
            else
            {
                ApplyCentralForce(forwardDir * EngineForce * throttle);
            }
        }

        if (reverse > 0.01f && fwdSpeed > -MaxForwardSpeed * 0.5f)
        {
            if (fwdSpeed > -1.0f)
            {
                var targetRevSpeed = Mathf.Max(fwdSpeed - EngineForce * 0.6f * reverse * dt / Mass, -MaxForwardSpeed * 0.5f);
                var bodyVel = LinearVelocity;
                bodyVel += (-forwardDir) * (fwdSpeed - targetRevSpeed);
                LinearVelocity = bodyVel;
            }
            else
            {
                ApplyCentralForce(-forwardDir * EngineForce * 0.6f * reverse);
            }
        }

        if (braking)
        {
            // Crude brake: dampen linear velocity in the forward axis.
            var v = LinearVelocity;
            var fwdComponent = forwardDir * fwdSpeed;
            var lateral = v - fwdComponent;
            LinearVelocity = fwdComponent * Mathf.Max(0.0f, 1.0f - BrakeForce * dt) + lateral;
        }

        // --- Hard speed clamp ---
        // MaxForwardSpeed only caps engine force, but collisions and terrain
        // can push the car faster. Hard-clamp the horizontal speed.
        {
            var hVel = new Vector3(LinearVelocity.X, 0, LinearVelocity.Z);
            var hSpeed = hVel.Length();
            if (hSpeed > MaxForwardSpeed)
            {
                var scale = MaxForwardSpeed / hSpeed;
                LinearVelocity = new Vector3(
                    hVel.X * scale, LinearVelocity.Y, hVel.Z * scale);
            }
        }

        // --- Steering as yaw torque, scaled by speed (no torque at standstill) ---
        // Auto-steer assist when off-road: nudge steering toward the road
        // centerline so the car returns without driver input. Disabled if
        // the player is actively steering — don't fight the player.
        var autoSteerDeg = 0.0f;
        if (offRoad && Mathf.Abs(steerInput) < 0.5f && roadMgr is not null)
        {
            var pull = roadMgr.GetPullVector(GlobalPosition);
            // pull.X > 0 means car is to the right of the road → steer LEFT (negative)
            // pull.X < 0 means car is to the left of the road → steer RIGHT (positive)
            // For curved roads, the pull has both X and Z components; project onto
            // the car's local right axis so the steering direction is intuitive
            // regardless of heading.
            if (pull.Length() > 0.01f)
            {
                var localRight = GlobalTransform.Basis.X;
                // Sign of the dot product tells us which way the car is offset
                // relative to its own heading. Positive dot = pull is to the
                // car's right → steer left (negative steer).
                var lateralSign = Mathf.Sign(pull.Dot(localRight));
                autoSteerDeg = -lateralSign * OffRoadAutoSteerDeg;
            }
        }
        var effectiveSteer = _steerAngleDeg + autoSteerDeg;
        var speedFactor = Mathf.Clamp(fwdSpeed / 5.0f, 0.0f, 1.0f);
        var yawTorque = Mathf.DegToRad(effectiveSteer) * speedFactor * 1500.0f;
        // Yaw around local up axis (Y in Godot).
        // Negated: positive Y torque rotates counter-clockwise from above
        // (toward -X / left in Godot). Without the negation, pressing right
        // turns the car left — confirmed inverted steering bug.
        ApplyTorque(GlobalTransform.Basis.Y * -yawTorque);

        // --- Lateral grip: kill sideways velocity component ---
        var vel = LinearVelocity;
        var lateralVel = vel - forwardDir * fwdSpeed;
        var gripMultiplier = offRoad ? OffRoadLateralDrag : LateralGripFactor;
        var gripImpulse = -lateralVel * Mass * gripMultiplier * dt;
        ApplyCentralImpulse(gripImpulse);

        // --- Downforce to prevent the car from feeling floaty ---
        // Base downforce keeps the car planted at low speeds (mass * 0.5 ≈ 0.05G
        // of artificial gravity on top of Godot's real gravity). Speed-scaled
        // downforce grows with velocity so the car doesn't launch on hills.
        var baseDownforce = Mass * 0.5f;
        var speedDownforce = Downforce * Mathf.Abs(fwdSpeed);
        ApplyCentralForce(-GlobalTransform.Basis.Y * (baseDownforce + speedDownforce));

        // --- Anti-rollover: restore upright if the car tilts too far ---
        var localUp = GlobalTransform.Basis.Y;
        var tiltDeg = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(localUp.Dot(Vector3.Up), -1f, 1f)));
        if (tiltDeg > AntiRolloverAngleDeg)
        {
            var rollAxis = localUp.Cross(Vector3.Up).Normalized();
            var rollStrength = Mathf.Clamp((tiltDeg - AntiRolloverAngleDeg) / 45f, 0f, 1f);
            ApplyTorque(rollAxis * AntiRolloverTorque * rollStrength);
        }

        // --- Auto-right: if flipped upside down for too long, snap upright ---
        if (tiltDeg > AutoRightAngleDeg)
            _flipTimer += dt;
        else
            _flipTimer = 0f;
        if (_flipTimer >= AutoRightSeconds)
        {
            var b = GlobalTransform.Basis;
            var up = Vector3.Up;
            var fwd = -b.Z;
            fwd.Y = 0; fwd = fwd.Normalized();
            var right = up.Cross(fwd).Normalized();
            var newFwd = right.Cross(up).Normalized();
            var newBasis = new Basis(right, up, -newFwd);
            GlobalTransform = new Transform3D(newBasis, GlobalTransform.Origin);
            AngularVelocity = Vector3.Zero;
            LinearVelocity = new Vector3(LinearVelocity.X, 0, LinearVelocity.Z);
            _flipTimer = 0f;
            GD.Print($"[Car] auto-right after {AutoRightSeconds}s flip " +
                     $"(tilt was {tiltDeg:F0}°) at ({GlobalPosition.X:F1}, {GlobalPosition.Z:F1})");
        }

        // --- General stuck-recovery (works on-road too) ---
        // Escalation: boost → boost → forward-teleport. The boost count
        // resets only when the car has moved >15m from the last stuck spot,
        // not on momentary speed spikes (the old code reset on a single frame
        // of speed from the boost itself, creating an infinite boost loop).
        _sinceStuckAction += dt;
        if (LinearVelocity.Length() < StuckSpeedThreshold)
            _stuckTimer += dt;
        else
            _stuckTimer = 0f;

        // Check: has the car made real progress since the last stuck action?
        var distFromStuckPos = (GlobalPosition - _lastStuckPos).Length();
        if (_stuckBoostCount > 0 && distFromStuckPos > 15f)
        {
            _stuckBoostCount = 0;
            GD.Print($"[Car] stuck resolved (moved {distFromStuckPos:F0}m from last stuck)");
        }

        if (_stuckTimer >= StuckBoostSeconds && tiltDeg < AutoRightAngleDeg)
        {
            _stuckBoostCount++;
            _lastStuckPos = GlobalPosition;
            _sinceStuckAction = 0f;

            if (_stuckBoostCount > StuckBoostRetries && roadMgr is not null)
            {
                // Teleport forward along the road centerline, past the obstacle.
                // Find the nearest centerline point, then jump 25m ahead in -Z.
                var p = roadMgr.GetPullVector(GlobalPosition);
                if (p.Length() > 0.01f)
                {
                    var cl = GlobalPosition - p;
                    GlobalPosition = new Vector3(cl.X, cl.Y + 1.5f, cl.Z - 25f);
                    LinearVelocity = new Vector3(0, 0, -2f);
                    GD.Print($"[Car] stuck-teleport forward 25m → ({GlobalPosition.X:F1}, {GlobalPosition.Z:F1})");
                }
            }
            else
            {
                ApplyCentralImpulse(forwardDir * StuckBoostImpulse);
                GD.Print($"[Car] stuck-recovery boost #{_stuckBoostCount} at ({GlobalPosition.X:F1}, {GlobalPosition.Z:F1})");
            }
            _stuckTimer = 0f;
        }

        // --- State-change logging with hysteresis ---
        // Without hysteresis, the car sitting on the road edge toggles
        // off-road state every frame, flooding the log. Require 30 frames
        // (~0.5s) of stable state before logging.
        if (offRoad == _wasOffRoad)
            _offRoadDebounce++;
        else
            _offRoadDebounce = 0;
        if (_offRoadDebounce == 30 && offRoad != _wasOffRoad)
        {
            GD.Print($"[Car] off-road state changed: {_wasOffRoad} -> {offRoad} " +
                     $"(pos={GlobalPosition.X:F1}, {GlobalPosition.Z:F1})");
            _wasOffRoad = offRoad;
            _offRoadDebounce = 60; // prevent re-log
        }

        // --- Debug: log every 60 physics frames (~1/sec) ---
        _debugLogCounter++;
        _physicsTick++;
        if (_debugLogCounter >= 60)
        {
            _debugLogCounter = 0;
            var rollInfo = tiltDeg > 10f ? $" tilt={tiltDeg:F0}°" : "";
            GD.Print($"[Car] pos=({GlobalPosition.X:F1},{GlobalPosition.Z:F1}) " +
                     $"vel={LinearVelocity.Length():F1} fwd={fwdSpeed:F1} " +
                     $"steer={_steerAngleDeg:F0}°{(autoSteerDeg != 0 ? $"(auto {autoSteerDeg:F0})" : "")} " +
                     $"contacts={contactCount}" + (offRoad ? " OFF_ROAD" : "") + rollInfo);
        }
    }

    private int _debugLogCounter;
}
