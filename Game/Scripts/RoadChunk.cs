using Godot;

namespace AiInfiniteJourney;

/// <summary>
/// Builds a large ground plane for the world. Phase 1 used a flat box. Phase 2
/// (the hackathon demo) uses a heightmap so the terrain has hills and valleys.
/// The road spline runs through this terrain — bridges and tunnels emerge
/// naturally from the height differences.
/// </summary>
[GlobalClass]
public partial class RoadChunk : Node3D
{
    [Export] public Vector2I ChunkSize = new(25, 25); // half-size in world units
    [Export] public int Resolution = 33;              // samples per axis (must be power-of-2 + 1)

    private float[,]? _heights;
    private float _cellSize;

    public override void _Ready()
    {
        _cellSize = (ChunkSize.X * 2.0f) / (Resolution - 1);
        BuildTerrain();
    }

    /// <summary>
    /// Sample the terrain height at a local (x, z) position. Used by the spline
    /// road system to snap the road to the terrain surface.
    /// </summary>
    public float SampleHeight(float localX, float localZ)
    {
        if (_heights is null) return 0f;
        var u = (localX + ChunkSize.X) / (ChunkSize.X * 2f);
        var v = (localZ + ChunkSize.Y) / (ChunkSize.Y * 2f);
        u = Mathf.Clamp(u, 0f, 1f);
        v = Mathf.Clamp(v, 0f, 1f);

        var fi = u * (Resolution - 1);
        var fj = v * (Resolution - 1);
        var i0 = (int)fi;
        var j0 = (int)fj;
        var i1 = Mathf.Min(i0 + 1, Resolution - 1);
        var j1 = Mathf.Min(j0 + 1, Resolution - 1);
        var ui = fi - i0;
        var vj = fj - j0;

        return Mathf.Lerp(
            Mathf.Lerp(_heights[i0, j0], _heights[i1, j0], ui),
            Mathf.Lerp(_heights[i0, j1], _heights[i1, j1], ui),
            vj);
    }

    private void BuildTerrain()
    {
        _heights = new float[Resolution, Resolution];
        var halfX = ChunkSize.X;
        var halfZ = ChunkSize.Y;

        // Generate heightmap using layered sine waves. The chunk's world
        // position seeds the phase so adjacent chunks blend smoothly.
        var seedX = GlobalPosition.X;
        var seedZ = GlobalPosition.Z;
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)(Mathf.Abs(seedX * 1000 + seedZ * 7) % 10000000 + 1);

        // Random terrain parameters per chunk (seeded by position, so same
        // chunk always produces the same terrain).
        var a0 = rng.RandfRange(0.5f, 2.0f);
        var a1 = rng.RandfRange(0.3f, 1.5f);
        var a2 = rng.RandfRange(0.1f, 0.6f);
        var f0x = rng.RandfRange(0.03f, 0.08f);
        var f0z = rng.RandfRange(0.03f, 0.08f);
        var f1x = rng.RandfRange(0.10f, 0.20f);
        var f1z = rng.RandfRange(0.10f, 0.20f);
        var phase0 = rng.RandfRange(0, Mathf.Tau);
        var phase1 = rng.RandfRange(0, Mathf.Tau);

        var maxH = 0.0f;
        for (var i = 0; i < Resolution; i++)
        for (var j = 0; j < Resolution; j++)
        {
            var x = -halfX + i * _cellSize;
            var z = -halfZ + j * _cellSize;
            var h = a0 * Mathf.Sin(f0x * x + f0z * z + phase0)
                  + a1 * Mathf.Sin(f1x * x + f1z * z + phase1)
                  + a2 * Mathf.Cos(f1z * x - f1x * z + phase1 * 0.5f);
            _heights[i, j] = h;
            if (h > maxH) maxH = h;
        }

        // Build visual mesh.
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        for (var i = 0; i < Resolution - 1; i++)
        for (var j = 0; j < Resolution - 1; j++)
        {
            var x0 = -halfX + i * _cellSize;
            var z0 = -halfZ + j * _cellSize;
            var x1 = x0 + _cellSize;
            var z1 = z0 + _cellSize;

            var h00 = _heights[i, j];
            var h10 = _heights[i + 1, j];
            var h01 = _heights[i, j + 1];
            var h11 = _heights[i + 1, j + 1];

            // Two triangles per quad.
            // Triangle 1: (0,0) → (1,0) → (0,1)
            var n1 = new Plane(new Vector3(x0, h00, z0),
                               new Vector3(x1, h10, z0),
                               new Vector3(x0, h01, z1)).Normal;
            st.SetNormal(n1);
            st.SetUV(new Vector2((float)i / (Resolution - 1), (float)j / (Resolution - 1)));
            st.AddVertex(new Vector3(x0, h00, z0));
            st.SetNormal(n1);
            st.SetUV(new Vector2((float)(i + 1) / (Resolution - 1), (float)j / (Resolution - 1)));
            st.AddVertex(new Vector3(x1, h10, z0));
            st.SetNormal(n1);
            st.SetUV(new Vector2((float)i / (Resolution - 1), (float)(j + 1) / (Resolution - 1)));
            st.AddVertex(new Vector3(x0, h01, z1));

            // Triangle 2: (1,0) → (1,1) → (0,1)
            var n2 = new Plane(new Vector3(x1, h10, z0),
                               new Vector3(x1, h11, z1),
                               new Vector3(x0, h01, z1)).Normal;
            st.SetNormal(n2);
            st.SetUV(new Vector2((float)(i + 1) / (Resolution - 1), (float)j / (Resolution - 1)));
            st.AddVertex(new Vector3(x1, h10, z0));
            st.SetNormal(n2);
            st.SetUV(new Vector2((float)(i + 1) / (Resolution - 1), (float)(j + 1) / (Resolution - 1)));
            st.AddVertex(new Vector3(x1, h11, z1));
            st.SetNormal(n2);
            st.SetUV(new Vector2((float)i / (Resolution - 1), (float)(j + 1) / (Resolution - 1)));
            st.AddVertex(new Vector3(x0, h01, z1));
        }

        st.GenerateNormals();

        var terrainMesh = st.Commit();
        var terrainMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.18f, 0.32f, 0.16f), // grass
            Roughness = 0.95f,
        };
        var mz = new MeshInstance3D
        {
            Mesh = terrainMesh,
            MaterialOverride = terrainMat,
        };
        AddChild(mz);

        // Build collision using HeightMapShape3D.
        var heightMap = new float[Resolution * Resolution];
        for (var i = 0; i < Resolution; i++)
        for (var j = 0; j < Resolution; j++)
            heightMap[i * Resolution + j] = _heights[i, j];

        var hms = new HeightMapShape3D
        {
            MapWidth = Resolution,
            MapDepth = Resolution,
            MapData = heightMap,
        };
        var cs = new CollisionShape3D
        {
            Shape = hms,
        };
        var sb = new StaticBody3D { Name = "Terrain" };
        sb.AddChild(cs);
        // Center the collision shape over the chunk area.
        cs.Position = new Vector3(0, 0, 0);
        // The HeightMapShape3D is centered and sized by MapData range.
        // Its world-size is controlled by MapWidth/MapDepth and the height
        // values — Godot scales it to (MapWidth-1)*cellSize per axis.

        AddChild(sb);
    }
}
