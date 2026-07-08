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
    [Export] public int Resolution = 51;              // 51 samples → 50 cells at 1m each = full 50m tile, NO scaling needed

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

        // Sample the shared TerrainHeight function in world space so terrain
        // and road are perfectly aligned. No per-chunk random seeds needed.
        for (var i = 0; i < Resolution; i++)
        for (var j = 0; j < Resolution; j++)
        {
            var localX = -halfX + i * _cellSize;
            var localZ = -halfZ + j * _cellSize;
            _heights[i, j] = TerrainHeight.Sample(
                GlobalPosition.X + localX,
                GlobalPosition.Z + localZ);
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

        // Build collision using HeightMapShape3D — smooth, no internal-edge
        // jitter. Resolution=51 means cellSize=1.0m exactly, so the HeightMapShape3D
        // covers the full 50m tile natively. NO scaling on CollisionShape3D
        // (scaling HeightMapShape3D is unreliable in Godot's physics server).
        // MapData is row-major: index = z * MapWidth + x.
        var heightMap = new float[Resolution * Resolution];
        for (var j = 0; j < Resolution; j++)   // j = Z (rows)
        for (var i = 0; i < Resolution; i++)   // i = X (columns)
            heightMap[j * Resolution + i] = _heights[i, j];

        var hms = new HeightMapShape3D
        {
            MapWidth = Resolution,
            MapDepth = Resolution,
            MapData = heightMap,
        };
        var sb = new StaticBody3D { Name = "TerrainCollision" };
        sb.AddChild(new CollisionShape3D { Shape = hms });
        AddChild(sb);

        GD.Print($"[RoadChunk] terrain built at {GlobalPosition}. " +
                 $"res={Resolution} cell={_cellSize:F2}m " +
                 $"heightRange=[{MinHeight():F2}, {MaxHeight():F2}]");
    }

    private float MinHeight()
    {
        var min = float.MaxValue;
        for (var i = 0; i < Resolution; i++)
        for (var j = 0; j < Resolution; j++)
            if (_heights![i, j] < min) min = _heights[i, j];
        return min;
    }

    private float MaxHeight()
    {
        var max = float.MinValue;
        for (var i = 0; i < Resolution; i++)
        for (var j = 0; j < Resolution; j++)
            if (_heights![i, j] > max) max = _heights[i, j];
        return max;
    }
}
