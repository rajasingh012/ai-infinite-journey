using Godot;

namespace AiInfiniteJourney;

/// <summary>
/// Player car visual + collision. Loads the Kenney sedan GLB mesh and applies
/// its colormap texture. Wraps a CarController (the physics body).
/// </summary>
[GlobalClass]
public partial class PlayerCar : CarController
{
    [Export] public string CarMeshPath { get; set; } = "res://Assets/Models/Cars/sedan.glb";
    [Export] public string CarTexturePath { get; set; } = "res://Assets/Textures/car-colormap.png";
    [Export] public Vector3 CollisionSize { get; set; } = new(1.8f, 0.8f, 4.0f);

    public override void _Ready()
    {
        base._Ready();

        // Collision hull (sized to roughly match the Kenney sedan).
        // Use CallDeferred so the CollisionShape3D is added during the
        // right scene-tree phase — adding it directly in _Ready can
        // result in the physics server not registering the new shape,
        // which manifests as the rigid body having zero contacts even
        // when it's sitting on top of a collision body. (Confirmed bug
        // in this project on 2026-07-07.)
        var collision = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = CollisionSize },
        };
        CallDeferred(Node.MethodName.AddChild, collision);

        // Also set a PhysicsMaterial on the body to reduce terrain friction
        // at contact points — default friction (1.0) combined with downforce
        // can prevent the car from accelerating from standstill.
        PhysicsMaterialOverride = new PhysicsMaterial
        {
            Friction = 0.3f,
            Bounce = 0.0f,
        };

        // Load the sedan GLB as a child scene.
        var carScene = GD.Load<PackedScene>(CarMeshPath);
        if (carScene != null)
        {
            var instance = carScene.Instantiate<Node3D>();
            instance.Name = "SedanMesh";
            // Kenney cars are roughly 1 unit wide × 4 long. Apply texture via StandardMaterial3D.
            ApplyTextureToAllMaterials(instance, CarTexturePath);
            AddChild(instance);
        }
        else
        {
            GD.PushWarning($"PlayerCar: failed to load car mesh at {CarMeshPath}, falling back to box");
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = CollisionSize },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.15f, 0.15f) },
            });
        }
    }

    private static void ApplyTextureToAllMaterials(Node node, string texturePath)
    {
        var tex = GD.Load<Texture2D>(texturePath);
        if (tex == null) return;

        foreach (var child in node.GetChildren())
        {
            if (child is MeshInstance3D mi)
            {
                // Replace the material with a StandardMaterial3D using the colormap.
                // This works for Kenney assets that ship without embedded textures.
                var mat = new StandardMaterial3D
                {
                    AlbedoTexture = tex,
                    Metallic = 0.4f,
                    Roughness = 0.5f,
                };
                mi.MaterialOverride = mat;
            }
            if (child.GetChildCount() > 0)
                ApplyTextureToAllMaterials(child, texturePath);
        }
    }
}
