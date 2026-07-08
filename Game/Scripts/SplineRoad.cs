using Godot;
using System.Collections.Generic;

namespace AiInfiniteJourney;

/// <summary>
/// A curved road segment built from a Catmull-Rom spline that follows
/// the terrain. Control points are generated deterministically from world
/// Z position (via TerrainHeight), so adjacent segments share phantom
/// control points and achieve C1 tangent continuity at boundaries.
///
/// Each segment builds:
///   - A ribbon mesh (triangle strip) with StandardMaterial3D
///   - A ConcavePolygonShape3D collision (trimesh) on a StaticBody3D
///   - Center-line dashes placed by accumulated arc length
/// </summary>
[GlobalClass]
public partial class SplineRoad : Node3D
{
    [Export] public float StripLength = 50.0f;
    [Export] public float RoadWidth = 8.0f;
    [Export] public float ControlPointSpacing = 12.5f;
    [Export] public float MeshResolution = 1.5f; // meters between mesh samples
    [Export] public Color RoadColor = new(0.20f, 0.20f, 0.22f);

    private StandardMaterial3D? _roadMat;

    public override void _Ready()
    {
        BuildRoad();
        BuildDashes();
    }

    /// <summary>Re-tint the road surface (called by RoadManager on theme change).</summary>
    public void SetColor(Color color)
    {
        if (_roadMat is not null)
            _roadMat.AlbedoColor = color;
    }

    // ── Centerline sampling ───────────────────────────────────────────

    /// <summary>
    /// Generate Catmull-Rom control points (including phantom points one
    /// spacing beyond each end) and sample the spline at fine intervals.
    /// Returns (position, tangent) pairs for samples within [0, StripLength].
    /// </summary>
    private List<(Vector3 pos, Vector3 tangent)> SampleCenterline()
    {
        var baseZ = Position.Z;

        // Generate control points from -spacing to +StripLength+spacing.
        var cps = new List<Vector3>();
        for (var lz = -ControlPointSpacing;
             lz <= StripLength + ControlPointSpacing + 0.01f;
             lz += ControlPointSpacing)
        {
            var worldZ = baseZ + lz;
            cps.Add(new Vector3(
                TerrainHeight.RoadCenterX(worldZ),
                TerrainHeight.RoadHeightAt(worldZ),
                lz));
        }

        // Sample Catmull-Rom through consecutive quadruplets.
        var samples = new List<(Vector3 pos, Vector3 tangent)>();
        for (var i = 0; i < cps.Count - 3; i++)
        {
            var p0 = cps[i];
            var p1 = cps[i + 1];
            var p2 = cps[i + 2];
            var p3 = cps[i + 3];

            var segLen = p2.DistanceTo(p1);
            var n = Mathf.Max(2, (int)(segLen / MeshResolution) + 1);

            for (var j = 0; j < n; j++)
            {
                var t = (float)j / n;
                var pos = CatmullRom(p0, p1, p2, p3, t);
                var tan = CatmullRomTangent(p0, p1, p2, p3, t);
                if (pos.Z >= -0.01f && pos.Z <= StripLength + 0.01f)
                    samples.Add((pos, tan));
            }
        }
        return samples;
    }

    // ── Road mesh + collision ─────────────────────────────────────────

    private void BuildRoad()
    {
        var samples = SampleCenterline();
        if (samples.Count < 2) return;

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        for (var i = 0; i < samples.Count - 1; i++)
        {
            var (p, tan) = samples[i];
            var (p2, _) = samples[i + 1];

            // Horizontal perpendicular to the road tangent.
            var perp = new Vector3(-tan.Z, 0, tan.X).Normalized();
            var hw = RoadWidth * 0.5f;

            var left = p + perp * hw;
            var right = p - perp * hw;
            var left2 = p2 + perp * hw;
            var right2 = p2 - perp * hw;

            // Winding: (left, left2, right) and (right, left2, right2)
            // produces +Y-facing normals in both driving directions.
            st.SetUV(new Vector2(0, (float)i / samples.Count));
            st.AddVertex(left);
            st.SetUV(new Vector2(0, (float)(i + 1) / samples.Count));
            st.AddVertex(left2);
            st.SetUV(new Vector2(1, (float)i / samples.Count));
            st.AddVertex(right);

            st.SetUV(new Vector2(1, (float)i / samples.Count));
            st.AddVertex(right);
            st.SetUV(new Vector2(0, (float)(i + 1) / samples.Count));
            st.AddVertex(left2);
            st.SetUV(new Vector2(1, (float)(i + 1) / samples.Count));
            st.AddVertex(right2);
        }

        st.GenerateNormals();
        var mesh = st.Commit();

        _roadMat = new StandardMaterial3D
        {
            AlbedoColor = RoadColor,
            Roughness = 0.7f,
        };
        AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = _roadMat });

        // Collision: trimesh shape from the road mesh.
        var shape = mesh.CreateTrimeshShape();
        if (shape is not null)
        {
            var body = new StaticBody3D { Name = "RoadCollision" };
            body.AddChild(new CollisionShape3D { Shape = shape });
            AddChild(body);
        }
    }

    // ── Center-line dashes ────────────────────────────────────────────

    private void BuildDashes()
    {
        var samples = SampleCenterline();
        if (samples.Count < 2) return;

        var dashMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.95f, 0.95f, 0.85f),
            Roughness = 0.5f,
        };

        const float dashLen = 1.5f;
        const float dashGap = 1.5f;
        const float cycle = dashLen + dashGap;

        var distAccum = 0f;
        for (var i = 0; i < samples.Count - 1; i++)
        {
            var (pos, tan) = samples[i];
            var (next, _) = samples[i + 1];
            distAccum += pos.DistanceTo(next);

            if (distAccum < cycle) continue;
            distAccum = 0f;

            var yaw = Mathf.Atan2(tan.X, tan.Z);
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.2f, 0.01f, dashLen) },
                Position = new Vector3(pos.X, pos.Y + 0.06f, pos.Z),
                Rotation = new Vector3(0, yaw, 0),
                MaterialOverride = dashMat,
            });
        }
    }

    // ── Catmull-Rom math ──────────────────────────────────────────────

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return 0.5f * (
            2 * p1 +
            (-p0 + p2) * t +
            (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 +
            (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
    }

    private static Vector3 CatmullRomTangent(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        var t2 = t * t;
        return 0.5f * (
            (-p0 + p2) +
            2 * (2 * p0 - 5 * p1 + 4 * p2 - p3) * t +
            3 * (-p0 + 3 * p1 - 3 * p2 + p3) * t2);
    }
}
