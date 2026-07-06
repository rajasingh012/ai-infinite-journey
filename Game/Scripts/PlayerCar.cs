using Godot;

namespace AiInfiniteJourney;

/// <summary>
/// Builds a simple car visual + collision hull as children of the RigidBody3D root.
/// Kept separate from CarController so the controller stays focused on physics
/// and this stays focused on shape/material.
/// </summary>
[GlobalClass]
public partial class PlayerCar : CarController
{
    [Export] public Vector3 BodySize = new(1.8f, 0.6f, 4.0f); // Half-extents ×2, X=width Y=height Z=length
    [Export] public Color BodyColor = new(0.85f, 0.15f, 0.15f);

    public override void _Ready()
    {
        base._Ready();

        // Collision hull.
        var collision = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = BodySize },
        };
        AddChild(collision);

        // Body mesh.
        var body = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = BodySize },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = BodyColor,
                Metallic = 0.4f,
                Roughness = 0.4f,
            },
        };
        AddChild(body);

        // Cabin (smaller box on top, shifted up and slightly back).
        var cabinSize = BodySize * new Vector3(0.8f, 0.5f, 0.5f);
        var cabin = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = cabinSize },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = BodyColor.Darkened(0.2f),
                Metallic = 0.6f,
                Roughness = 0.3f,
            },
            Position = new Vector3(0.0f, BodySize.Y * 0.6f, -BodySize.Z * 0.05f),
        };
        AddChild(cabin);
    }
}
