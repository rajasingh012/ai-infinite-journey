using Godot;

namespace AiInfiniteJourney;

/// <summary>
/// Scatters box-shaped obstacles (buildings/trees/rocks) beside a road chunk
/// based on the side theme from the LLM plan.
///
/// Sizes are randomly picked per spawn position to give visual variety:
/// - buildings: 4-8m wide, 5-14m tall, 4-8m deep (60% medium, 30% tall, 10% wide)
/// - trees: 2m cylindrical, 4m tall (simplified as boxes)
/// - rocks: 1-3m cubes
///
/// Each box has a StaticBody3D with a BoxShape3D collision so the car crashes.
/// </summary>
public partial class BuildingScatter : Node3D
{
    /// <summary>
    /// Spawn obstacles beside a road segment.
    /// Call once per chunk after it's positioned in the world.
    /// </summary>
    /// <param name="start">Start of the road segment (world space).</param>
    /// <param name="end">End of the road segment.</param>
    /// <param name="width">Road width — obstacles start beyond width/2 + margin.</param>
    /// <param name="sideTheme">"buildings", "sparse_buildings", "trees", "rocks", "none".</param>
    /// <param name="sideMultiplier">+1 for right side, -1 for left side.</param>
    public static void Scatter(
    Node parent, Vector3 start, Vector3 end, float width,
    string sideTheme, int sideMultiplier,
    uint seed)
    {
        if (sideTheme == "none") return;
        if (sideTheme == "sparse_buildings") sideTheme = "buildings"; // same obstacle, less dense

        var segmentDir = (end - start).Normalized();
        var perpendicular = new Vector3(-segmentDir.Z, 0, segmentDir.X); // 90° right

        var length = (end - start).Length();
        var density = sideTheme switch
        {
            "buildings" => 8f,
            "trees" => 6f,
            "rocks" => 20f,
            _ => 10f,
        };

        if (density <= 0) return;

        // Margin from road edge. Must account for obstacle half-size so
        // buildings don't overlap the road surface. Max building half-size
        // is 4m (wide 8x8), so buildings need margin ≥ 4m.
        var margin = sideTheme switch
        {
            "buildings" => 5f,
            "trees" => 2f,
            "rocks" => 1f,
            _ => 5f,
        };

        var offsetFromCenter = width / 2 + margin;
        var direction = perpendicular * sideMultiplier;

        // Deterministic random for this (seed, side)
        var rng = new Random(seed ^ (uint)sideMultiplier);
        var count = (int)(length / density);
        if (count < 1) return;

        for (int i = 0; i < count; i++)
        {
            var t = (i + 0.5f) / count; // center each object in its slot
            var pos = start.Lerp(end, t);

            // Jitter laterally ±30% of density
            var lateralJitter = (float)(rng.NextDouble() * 2 - 1) * density * 0.3f;
            pos += direction * (offsetFromCenter + Mathf.Max(0, lateralJitter));

            // Vary forward/back ±20% of slot width
            var forwardJitter = (float)(rng.NextDouble() * 2 - 1) * (density * 0.2f);
            pos += segmentDir * forwardJitter;

            // Surface height from terrain
            pos.Y = TerrainHeight.Sample(pos.X, pos.Z);

            // Skip if deeply buried (terrain too high) — rare with smooth terrain
            if (pos.Y < -10 || pos.Y > 20) continue;

            SpawnObstacle(parent, pos, sideTheme, rng);
        }
    }

    private static void SpawnObstacle(Node parent, Vector3 pos, string theme, Random rng)
    {
        var body = new StaticBody3D();

        Vector3 size;
        Color color;

        switch (theme)
        {
            case "buildings":
            {
                var roll = rng.NextDouble();
                if (roll < 0.6) // medium
                    size = new Vector3(5f, 8f, 5f);
                else if (roll < 0.9) // tall
                    size = new Vector3(4f, 12f, 4f);
                else // wide
                    size = new Vector3(8f, 6f, 8f);

                // Pastel tones for buildings — earthy palette
                var palette = rng.Next(4) switch
                {
                    0 => new Color(0.72f, 0.66f, 0.58f), // beige
                    1 => new Color(0.62f, 0.50f, 0.40f), // brown
                    2 => new Color(0.75f, 0.70f, 0.65f), // grey
                    _ => new Color(0.80f, 0.65f, 0.55f), // tan
                };
                color = palette;
                break;
            }
            case "trees":
            {
                var h = 4f + (float)(rng.NextDouble() * 2f);
                size = new Vector3(1.2f, h, 1.2f);
                color = new Color(
                    0.15f + (float)rng.NextDouble() * 0.15f,
                    0.40f + (float)rng.NextDouble() * 0.20f,
                    0.10f + (float)rng.NextDouble() * 0.10f);
                break;
            }
            case "rocks":
            {
                var s = 1f + (float)(rng.NextDouble() * 2f);
                size = new Vector3(s, s * 0.6f, s);
                color = new Color(
                    0.35f + (float)rng.NextDouble() * 0.15f,
                    0.35f + (float)rng.NextDouble() * 0.15f,
                    0.35f + (float)rng.NextDouble() * 0.15f);
                break;
            }
            default:
                return;
        }

        var shape = new BoxShape3D { Size = size };
        var collision = new CollisionShape3D { Shape = shape };
        body.AddChild(collision);

        // Visual placeholder — colored box with a subtle wireframe look
        var mesh = new BoxMesh
        {
            Size = size,
            Material = new StandardMaterial3D
            {
                AlbedoColor = color,
                Metallic = theme == "buildings" ? 0.1f : 0.0f,
                Roughness = theme == "buildings" ? 0.7f : 0.9f,
            },
        };
        var meshInstance = new MeshInstance3D { Mesh = mesh };
        body.AddChild(meshInstance);

        body.Position = pos;
        body.Name = $"obs_{theme}_{parent.GetChildCount()}";
        parent.AddChild(body);
    }

    private sealed class Random
    {
        private System.Random _rng;
        public Random(uint seed) => _rng = new System.Random((int)seed);
        public double NextDouble() => _rng.NextDouble();
        public int Next(int max) => _rng.Next(max);
    }
}