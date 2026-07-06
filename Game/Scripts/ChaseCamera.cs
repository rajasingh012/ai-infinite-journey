using Godot;

namespace AiInfiniteJourney;

/// <summary>
/// Smooth chase camera. Lives on a Node3D and is parented to the car.
/// Offsets are in local space, so the rig rotates with the car.
/// </summary>
[GlobalClass]
public partial class ChaseCamera : Node3D
{
    [Export] public NodePath TargetPath { get; set; } = "../..";
    [Export] public Vector3 LocalOffset = new(0.0f, 2.5f, 6.0f);  // Behind (positive Z) and above
    [Export] public Vector3 LookAtOffset = new(0.0f, 1.0f, -2.0f); // Look ahead of the car
    [Export] public float FollowSpeed = 8.0f;   // Position lerp speed (higher = snappier)
    [Export] public float LookSpeed = 12.0f;    // Rotation lerp speed
    [Export] public bool UseSpringArm = true;   // Phase 1 uses a simple offset; toggle for future

    private Node3D? _target;

    public override void _Ready()
    {
        if (TargetPath != null && !TargetPath.IsEmpty)
        {
            _target = GetNodeOrNull<Node3D>(TargetPath);
            if (_target == null)
                GD.PushWarning($"ChaseCamera: target '{TargetPath}' not found, falling back to parent.");
        }
        _target ??= GetParentOrNull<Node3D>();

        // Snap on first frame to avoid a fly-in from origin.
        if (_target != null)
        {
            GlobalPosition = _target.GlobalPosition + _target.GlobalTransform.Basis * LocalOffset;
            LookAt(_target.GlobalPosition + LookAtOffset, Vector3.Up);
        }
    }

    public override void _Process(double delta)
    {
        if (_target == null) return;
        var dt = (float)delta;

        // Desired position: behind/above the car in world space.
        var desiredPos = _target.GlobalPosition + _target.GlobalTransform.Basis * LocalOffset;
        GlobalPosition = GlobalPosition.Lerp(desiredPos, 1.0f - Mathf.Exp(-FollowSpeed * dt));

        // Smoothly turn to look at the car.
        var lookTarget = _target.GlobalPosition + LookAtOffset;
        var newTransform = GlobalTransform.LookingAt(lookTarget, Vector3.Up);
        GlobalTransform = GlobalTransform.InterpolateWith(newTransform, 1.0f - Mathf.Exp(-LookSpeed * dt));
    }
}
