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
    [Export] public float EngineForce = 120000.0f;    // Newtons along forward axis. Effective value used during the 2026-07-07 "car doesn't move" debug. 100x the original 1200 N. Lower values (3000, 12000) were tested and did not move the 1200 kg body — the RigidBody3D solver swallows small per-tick forces for resting bodies. 120000 N gives ~100 m/s² peak acceleration, capped at MaxForwardSpeed=25 m/s, which is the right arcade feel for a hackathon demo. Day-2 work will replace this with proper wheel colliders so we can dial down to realistic values.
    [Export] public float BrakeForce = 30.0f;          // Strong friction scalar while brake is held
    [Export] public float MaxSteerAngleDeg = 35.0f;    // Max front-wheel steering angle
    [Export] public float SteerSpeedDegPerSec = 180.0f; // How fast the steering wheel turns
    [Export] public float SteerReturnDegPerSec = 240.0f; // How fast it recenters when no input
    [Export] public float MaxForwardSpeed = 18.0f;     // m/s — soft cap (~65 km/h). Lowered from 25 to keep a hackathon demo at driving-game speeds, not racing speeds.
    [Export] public float LateralGripFactor = 3.0f;   // Lowered from 12: high grip impulse oscillated with terrain jitter
    [Export] public float Downforce = 20.0f;           // Speed-scaled downforce multiplier
    [Export] public float AntiBounceForce = 50.0f;     // Damps vertical velocity on contact to prevent earthquake jitter

    private float _steerAngleDeg;

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
        var throttle = Input.GetActionStrength("forward");
        var reverse = Input.GetActionStrength("back");
        var braking = Input.IsActionPressed("brake") || (throttle < 0.01f && reverse < 0.01f && LinearVelocity.Length() < 0.5f);

        // Forward force along the car's local -Z (Godot convention: -Z is forward)
        var forwardDir = -GlobalTransform.Basis.Z;
        var fwdSpeed = LinearVelocity.Dot(forwardDir);

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

        // --- Steering as yaw torque, scaled by speed (no torque at standstill) ---
        var speedFactor = Mathf.Clamp(fwdSpeed / 5.0f, 0.0f, 1.0f);
        var yawTorque = Mathf.DegToRad(_steerAngleDeg) * speedFactor * 1500.0f;
        // Yaw around local up axis (Y in Godot).
        // Negated: positive Y torque rotates counter-clockwise from above
        // (toward -X / left in Godot). Without the negation, pressing right
        // turns the car left — confirmed inverted steering bug.
        ApplyTorque(GlobalTransform.Basis.Y * -yawTorque);

        // --- Lateral grip: kill sideways velocity component ---
        var vel = LinearVelocity;
        var lateralVel = vel - forwardDir * fwdSpeed;
        var gripImpulse = -lateralVel * Mass * LateralGripFactor * dt;
        ApplyCentralImpulse(gripImpulse);

        // --- Downforce to prevent the car from feeling floaty ---
        // Base downforce keeps the car planted at low speeds (mass * 0.5 ≈ 0.05G
        // of artificial gravity on top of Godot's real gravity). Speed-scaled
        // downforce grows with velocity so the car doesn't launch on hills.
        var baseDownforce = Mass * 0.5f;
        var speedDownforce = Downforce * Mathf.Abs(fwdSpeed);
        ApplyCentralForce(-GlobalTransform.Basis.Y * (baseDownforce + speedDownforce));

        // --- Debug: log every 60 physics frames (~1/sec) ---
        _debugLogCounter++;
        if (_debugLogCounter >= 60)
        {
            _debugLogCounter = 0;
            GD.Print($"[Car] pos={GlobalPosition:F1} vel={LinearVelocity:F1} " +
                     $"fwd={fwdSpeed:F1} steer={_steerAngleDeg:F0}° contacts={contactCount}");
        }
    }

    private int _debugLogCounter;
}
