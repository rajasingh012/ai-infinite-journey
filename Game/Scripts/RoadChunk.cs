using Godot;

namespace AiInfiniteJourney;

/// <summary>
/// Builds the world for Phase 1: a large ground plane and a single straight road segment
/// the player can drive along. Everything is procedurally placed in code (no external assets
/// needed) so the project builds and runs out of the box.
///
/// Phase 2 will replace this with a spline-based road generator + chunk streaming.
/// </summary>
[GlobalClass]
public partial class RoadChunk : Node3D
{
    [Export] public Vector2 GroundSize = new(400.0f, 400.0f); // X = width, Y = depth
    [Export] public float RoadLength = 200.0f;
    [Export] public float RoadWidth = 8.0f;
    [Export] public Color GroundColor = new(0.18f, 0.32f, 0.16f); // grass
    [Export] public Color RoadColor = new(0.20f, 0.20f, 0.22f);    // asphalt

    public override void _Ready()
    {
        BuildGround();
        BuildRoad();
    }

    private void BuildGround()
    {
        var ground = new StaticBody3D
        {
            Name = "Ground",
        };

        var groundShape = new CollisionShape3D
        {
            Shape = new BoxShape3D
            {
                Size = new Vector3(GroundSize.X, 0.2f, GroundSize.Y),
            },
        };
        ground.AddChild(groundShape);

        var groundMesh = new MeshInstance3D
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
        groundMesh.MaterialOverride = groundMat;
        ground.AddChild(groundMesh);

        AddChild(ground);
    }

    private void BuildRoad()
    {
        var road = new StaticBody3D { Name = "Road" };

        var roadShape = new CollisionShape3D
        {
            Shape = new BoxShape3D
            {
                Size = new Vector3(RoadWidth, 0.05f, RoadLength),
            },
            Position = new Vector3(0.0f, 0.05f, 0.0f),
        };
        road.AddChild(roadShape);

        var roadMesh = new MeshInstance3D
        {
            Mesh = new BoxMesh
            {
                Size = new Vector3(RoadWidth, 0.05f, RoadLength),
            },
        };
        var roadMat = new StandardMaterial3D
        {
            AlbedoColor = RoadColor,
            Roughness = 0.7f,
        };
        roadMesh.MaterialOverride = roadMat;
        roadMesh.Position = new Vector3(0.0f, 0.05f, 0.0f);
        road.AddChild(roadMesh);

        // Dashed center line — a series of small flat boxes down the middle.
        const float dashLength = 1.5f;
        const float dashGap = 1.5f;
        const float dashWidth = 0.2f;
        const float dashHeight = 0.011f;
        var dashCount = (int)(RoadLength / (dashLength + dashGap));
        var dashMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.95f, 0.95f, 0.85f),
            Roughness = 0.5f,
        };
        for (var i = 0; i < dashCount; i++)
        {
            var z = -RoadLength * 0.5f + (i + 0.5f) * (dashLength + dashGap);
            var dash = new MeshInstance3D
            {
                Mesh = new BoxMesh
                {
                    Size = new Vector3(dashWidth, dashHeight, dashLength),
                },
                Position = new Vector3(0.0f, 0.105f, z),
                MaterialOverride = dashMat,
            };
            road.AddChild(dash);
        }

        AddChild(road);
    }
}
