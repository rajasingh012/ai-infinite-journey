using Godot;

namespace AiInfiniteJourney;

/// <summary>
/// Arcade-style car controller. Reads the "forward", "back", "left", "right",
/// and "brake" input actions and applies force/steering to a RigidBody3D.
///
/// This is intentionally simple for Phase 1: driveable car + camera + road.
/// Phase 2 will swap in a real wheel/raycast suspension. Phase 3+ stays the
/// same — the procedural generator just drops the car into the world.
/// </summary>
[GlobalClass]
public partial class CarController : RigidBody3D
{
    [Export] public float EngineForce = 1200.0f;      // Newtons applied at the wheels along forward axis
    [Export] public float BrakeForce = 30.0f;          // Strong friction scalar while brake is held
    [Export] public float MaxSteerAngleDeg = 35.0f;    // Max front-wheel steering angle
    [Export] public float SteerSpeedDegPerSec = 180.0f; // How fast the steering wheel turns
    [Export] public float SteerReturnDegPerSec = 240.0f; // How fast it recenters when no input
    [Export] public float MaxForwardSpeed = 25.0f;     // m/s — soft cap, not a hard wall
    [Export] public float LateralGripFactor = 12.0f;   // Higher = less sliding sideways
    [Export] public float Downforce = 20.0f;           // Keeps the car planted at speed

    private float _steerAngleDeg;

    public override void _Ready()
    {
        // Center of mass low + forward helps stability. Must enable Custom mode
        // before writing to CenterOfMass, otherwise Godot throws.
        CenterOfMassMode = CenterOfMassModeEnum.Custom;
        var com = CenterOfMass;
        com.Y = -0.4f;
        CenterOfMass = com;
    }

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;

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

        if (throttle > 0.01f && fwdSpeed < MaxForwardSpeed)
            ApplyCentralForce(forwardDir * EngineForce * throttle);

        if (reverse > 0.01f && fwdSpeed > -MaxForwardSpeed * 0.5f)
            ApplyCentralForce(-forwardDir * EngineForce * 0.6f * reverse);

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
        ApplyTorque(GlobalTransform.Basis.Y * yawTorque);

        // --- Lateral grip: kill sideways velocity component ---
        var vel = LinearVelocity;
        var lateralVel = vel - forwardDir * fwdSpeed;
        var gripImpulse = -lateralVel * Mass * LateralGripFactor * dt;
        ApplyCentralImpulse(gripImpulse);

        // --- Downforce to prevent the car from feeling floaty ---
        ApplyCentralForce(-GlobalTransform.Basis.Y * Downforce * Mathf.Abs(fwdSpeed));
    }
}
