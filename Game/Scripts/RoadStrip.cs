using Godot;

namespace AiInfiniteJourney;

/// <summary>
/// A single tileable road segment (50m long, 8m wide). Spawned by RoadManager
/// to build an endless road. Includes collision, road surface mesh, and
/// dashed center line. Tiles at Z boundaries so adjacent strips line up
/// without visible seams.
/// </summary>
[GlobalClass]
public partial class RoadStrip : Node3D
{
    [Export] public float StripLength = 50.0f;
    [Export] public float RoadWidth = 8.0f;
    [Export] public Color RoadColor = new(0.20f, 0.20f, 0.22f); // default asphalt

    private StandardMaterial3D? _roadMat;

    public override void _Ready()
    {
        BuildRoad();
        BuildDashes();
    }

    /// <summary>
    /// Called by RoadManager to tint this strip to match the current
    /// journey theme (e.g. desert = sandy, mountain = dark gray).
    /// </summary>
    public void SetColor(Color color)
    {
        if (_roadMat is not null)
            _roadMat.AlbedoColor = color;
    }

    private void BuildRoad()
    {
        var road = new StaticBody3D { Name = "RoadStrip" };

        var roadShape = new CollisionShape3D
        {
            Shape = new BoxShape3D
            {
                Size = new Vector3(RoadWidth, 0.05f, StripLength),
            },
            Position = new Vector3(0.0f, 0.05f, StripLength * 0.5f),
        };
        road.AddChild(roadShape);

        var roadMesh = new MeshInstance3D
        {
            Mesh = new BoxMesh
            {
                Size = new Vector3(RoadWidth, 0.05f, StripLength),
            },
        };
        _roadMat = new StandardMaterial3D
        {
            AlbedoColor = RoadColor,
            Roughness = 0.7f,
        };
        roadMesh.MaterialOverride = _roadMat;
        roadMesh.Position = new Vector3(0.0f, 0.05f, StripLength * 0.5f);
        road.AddChild(roadMesh);

        AddChild(road);
    }

    private void BuildDashes()
    {
        const float dashLength = 1.5f;
        const float dashGap = 1.5f;
        const float dashWidth = 0.2f;
        const float dashHeight = 0.011f;
        const float cycle = dashLength + dashGap; // 3.0m

        var dashMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.95f, 0.95f, 0.85f),
            Roughness = 0.5f,
        };

        // Number of full cycles in this strip. Start at Z=0 (the start of
        // the strip) so adjacent strips maintain dash continuity.
        var dashCount = (int)(StripLength / cycle);

        for (var i = 0; i < dashCount; i++)
        {
            var z = i * cycle + dashLength * 0.5f; // center of the dash
            var dash = new MeshInstance3D
            {
                Mesh = new BoxMesh
                {
                    Size = new Vector3(dashWidth, dashHeight, dashLength),
                },
                Position = new Vector3(0.0f, 0.105f, z),
                MaterialOverride = dashMat,
            };
            AddChild(dash);
        }
    }
}
