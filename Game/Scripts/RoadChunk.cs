using Godot;

namespace AiInfiniteJourney;

/// <summary>
/// Builds a large ground plane for the world. No road — road strips are
/// managed by RoadManager. Single large box (2000x2000) so the car can
/// drive for ~110 seconds at max speed before reaching the edge.
/// </summary>
[GlobalClass]
public partial class RoadChunk : Node3D
{
    [Export] public Vector2 GroundSize = new(2000.0f, 2000.0f);
    [Export] public Color GroundColor = new(0.18f, 0.32f, 0.16f); // grass

    private MeshInstance3D? _groundMesh;

    public override void _Ready()
    {
        BuildGround();
    }

    /// <summary>
    /// Change the ground colour (called by RoadManager when the journey theme
    /// changes, so ground tint matches the LLM's output).
    /// </summary>
    public void SetGroundTint(Color color)
    {
        if (_groundMesh?.MaterialOverride is StandardMaterial3D mat)
            mat.AlbedoColor = color;
    }

    private void BuildGround()
    {
        var ground = new StaticBody3D { Name = "Ground" };

        var groundShape = new CollisionShape3D
        {
            Shape = new BoxShape3D
            {
                Size = new Vector3(GroundSize.X, 0.2f, GroundSize.Y),
            },
        };
        ground.AddChild(groundShape);

        _groundMesh = new MeshInstance3D
        {
            Mesh = new BoxMesh
            {
                Size = new Vector3(GroundSize.X, 0.2f, GroundSize.Y),
            },
        };
        var groundMat = new StandardMaterial3D
        {
            AlbedoColor = GroundColor,
            Roughness = 0.95f,
        };
        _groundMesh.MaterialOverride = groundMat;
        ground.AddChild(_groundMesh);

        AddChild(ground);
    }
}
