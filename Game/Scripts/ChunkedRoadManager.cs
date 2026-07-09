using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace AiInfiniteJourney;

/// <summary>
/// Manages the road ahead of the player as a sliding window of chunks
/// fetched from the API (or fallback). Runs on a timer (8s interval) while
/// the car is driving.
///
/// Each chunk is a straight road segment (visual mesh only) with optional
/// side obstacles and a potential dead-end blocker. Chunks are freed once
/// the car is >100m past them.
///
/// Wire this into Main.tscn as a child of the root. Requires a JourneyApi
/// child node for fetching chunks, and a reference to the player car for
/// distance tracking.
/// </summary>
public partial class ChunkedRoadManager : Node3D
{
    [Export] public NodePath? JourneyApiPath { get; set; }
    [Export] public NodePath? CarPath { get; set; }
    [Export] public string Theme { get; set; } = "Countryside";
    [Export] public float ChunkInterval { get; set; } = 8f;

    /// <summary>Slow timer can fire multiple requests in a single tick.</summary>
    private JourneyApi? _api;
    private CarController? _car;
    private Timer? _timer;

    /// <summary>
    /// Active road chunks. We hold 3 ahead; the car moves through them.
    /// Each item: (chunk_id, Node3D with road visual + obstacles + maybe dead-end).
    /// Lazily reparented to this node.
    /// </summary>
    private readonly List<ChunkNode> _chunks = new();
    private float _nextChunkEndX;
    private float _nextChunkEndZ;
    private float _seedCarX;
    private float _seedCarZ;

    /// <summary>Prevent multiple concurrent API calls.</summary>
    private bool _fetching;

    // JSON deserialization options for the chunk plan
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public override void _Ready()
    {
        _api = GetNodeOrNull<JourneyApi>(JourneyApiPath ?? "");
        _car = GetNodeOrNull<CarController>(CarPath ?? "");

        if (_car is null)
        {
            GD.PushWarning("[ChunkedRoadManager] Car not found — chunks won't spawn.");
            return;
        }

        // Listen for theme updates from the journey plan.
        JourneyState.Instance.Connect(nameof(JourneyState.PlanChanged),
            Callable.From(() => { Theme = JourneyState.Instance.Plan?.Theme ?? "Countryside"; }));

        // Start with a seed chunk right in front of the car so the road
        // is visible immediately, even before the first API call completes.
        var spawnX = _car.Position.X;
        var spawnZ = _car.Position.Z;
        var seedEnd = new Vector3(spawnX, 0, spawnZ - 60f);
        AddChunkNode(
            new ChunkSegment
            {
                ChunkId = -1,
                Start = new[] { spawnX, 0f, spawnZ },
                End = new[] { spawnX, 0f, spawnZ - 60f },
                Width = 8f,
                DeadEnd = false,
                SideLeft = "none",
                SideRight = "none",
                TerrainBias = "",
            });

        _nextChunkEndX = spawnX;
        _nextChunkEndZ = spawnZ - 60f;

        // Stash the car spawn Z so subsequent fallback batches anchor to
        // the car's CURRENT position each tick (the car moves forward in
        // -Z; this seed anchors the first generation only).
        _seedCarZ = spawnZ;
        _seedCarX = spawnX;

        _timer = new Timer { WaitTime = ChunkInterval, OneShot = false, Autostart = false };
        AddChild(_timer);
        _timer.Timeout += OnTimerTick;
        _timer.Start();

        // Fire immediately so we get the first batch right away.
        CallDeferred(nameof(FetchNextBatch));
    }

    /// <summary>How close the car gets to the last chunk's end before
    /// we fetch a new batch. Should be less than the per-batch road
    /// length (3 chunks × ~65m = ~200m) so the fetch completes before
    /// the road runs out.</summary>
    private const float RefetchThresholdMeters = 350f;

    public override void _Process(double delta)
    {
        if (_car is null) return;

        // Free chunks outside the keep window. We measure by Z-distance from
        // the car: forward is -Z in Godot, so chunks with EndPosition.Z much
        // smaller than carZ are ahead, and EndPosition.Z much larger than
        // carZ are behind. The car drives in -Z direction.
        var carZ = _car.Position.Z;
        for (int i = _chunks.Count - 1; i >= 0; i--)
        {
            var endZ = _chunks[i].EndPosition.Z;
            var startZ = _chunks[i].StartPosition.Z;

            // Keep window: chunk must overlap [-100, +500] metres around car
            // in the Z axis (ahead = more negative, behind = more positive).
            // 500m of visible road ahead lets the player see what's coming.
            var behind = endZ > carZ + 100f;        // chunk end is more positive than 100m behind car
            var tooFarAhead = startZ < carZ - 500f;  // chunk start is more than 500m ahead

            if (behind || tooFarAhead)
            {
                _chunks[i].Node?.QueueFree();
                _chunks.RemoveAt(i);
            }
        }

        // Position-driven fetch: if the car is closer than
        // RefetchThresholdMeters to the end of the FURTHEST-ahead chunk,
        // fetch a new batch so the road continues indefinitely.
        // (Logic lives in the static DistanceToFurthestChunkEnd helper so
        // it's unit-testable without instantiating Godot nodes.)
        if (!_fetching)
        {
            var distAhead = DistanceToFurthestChunkEnd(
                _chunks.Select(c => c.EndPosition.Z), carZ);
            if (distAhead < RefetchThresholdMeters)
                FetchNextBatch();
        }
    }

    private void OnTimerTick()
    {
        if (_fetching) return;
        // Fallback: timer-based re-check, in case _Process-based fetch is
        // throttled by frame rate or the car is stationary. The
        // _Process distance check is the primary driver; this just guards
        // against pathological cases.
        if (_chunks.Count == 0) FetchNextBatch();
    }

    private void FetchNextBatch()
    {
        if (_fetching) return;
        _fetching = true;

        // Use the deterministic fallback generator for real-time chunk
        // streaming — it chains properly from the last chunk end. The
        // API chunk endpoint is too slow (LLM takes 60-120s) for
        // real-time gameplay.
        GD.Print("[ChunkedRoadManager] fallback batch...");
        var fallback = BuildFallback();
        foreach (var chunk in fallback)
            AddChunkNode(chunk);
        _fetching = false;
    }

    /// <summary>
    /// In-memory fallback when API is unavailable. Generates 2 chunks
    /// starting just in front of the car so the road is always visible.
    /// Same logic as the backend's SyntheticChunkFallback but running locally
    /// so the game works even without the backend server running.
    /// </summary>
    private List<ChunkSegment> BuildFallback()
    {
        // Anchor from the furthest chunk end so batches chain seamlessly.
        // The car position is NOT used for anchoring — it jumps sideways
        // relative to where the last batch ended, creating gaps where the
        // road "suddenly ends" and reappears 200m ahead, offset laterally.
        var anchorX = _nextChunkEndX;
        var anchorZ = _nextChunkEndZ;

        var rng = new Random(
            (uint)(anchorX.GetHashCode() ^ anchorZ.GetHashCode() ^ Theme.GetHashCode()) ^ 0x9e3779b9u);
        var chunks = new List<ChunkSegment>();
        var posX = anchorX;
        var posZ = anchorZ;
        // Mild initial heading so the road starts straight ahead, then drifts.
        var headingDeg = rng.Range(-5f, 5f);

        for (int i = 0; i < 3; i++)
        {
            var length = rng.Range(50f, 80f);
            // Smaller turn range keeps road near centerline for longer.
            var turn = rng.Range(-6f, 6f);
            headingDeg += turn;
            headingDeg = Math.Clamp(headingDeg, -25f, 25f);

            var rad = headingDeg * Mathf.Pi / 180f;
            var dx = Mathf.Sin(rad) * length;
            var dz = -Mathf.Cos(rad) * length;

            var sX = posX; var sZ = posZ;
            var eX = posX + dx; var eZ = posZ + dz;

            var deadEnd = (i == 2 && rng.Next() < 0.30f);

            chunks.Add(new ChunkSegment
            {
                ChunkId = (_chunks.Count + i),
                Start = new[] { sX, 0f, sZ },
                End = new[] { eX, 0f, eZ },
                Width = 8f,
                DeadEnd = deadEnd,
                SideLeft = i == 0 ? "buildings" : "trees",
                SideRight = i == 0 ? "rocks" : "sparse_buildings",
                TerrainBias = "",
            });

            posX = eX; posZ = eZ;
        }

        _nextChunkEndX = posX;
        _nextChunkEndZ = posZ;
        return chunks;
    }

    private void SpawnSideWall(
        Node3D parent, Vector3 start, Vector3 end, Vector3 direction, Vector3 perp,
        float offset, float thickness, float height, int sideMultiplier, float midY)
    {
        var wallCenter = (start + end) / 2 + perp * (offset * sideMultiplier);
        var wallLength = (end - start).Length();

        var wall = new StaticBody3D
        {
            Position = new Vector3(wallCenter.X, midY, wallCenter.Z),
            Rotation = new Vector3(0, Mathf.Atan2(direction.X, -direction.Z), 0),
            Name = sideMultiplier < 0 ? "wall_left" : "wall_right",
        };

        var shape = new BoxShape3D
        {
            // Size convention matches the road mesh: local Z = length
            // (along the road), local X = thickness (across the road).
            Size = new Vector3(thickness, height, wallLength),
        };
        wall.AddChild(new CollisionShape3D { Shape = shape });

        // No mesh — wall is invisible. Player sees the road surface but
        // feels the car bump against the invisible boundary.
        parent.AddChild(wall);
    }

    private void AddChunkNode(ChunkSegment chunk)
    {
        var start = new Vector3(chunk.Start[0], 0, chunk.Start[2]);
        var end = new Vector3(chunk.End[0], 0, chunk.End[2]);

        GD.Print($"[ChunkedRoadManager] spawn chunk {chunk.ChunkId}: ({start.X:F1},{start.Z:F1})→({end.X:F1},{end.Z:F1}) L={chunk.SideLeft} R={chunk.SideRight}");

        var node = new Node3D();
        node.Name = $"chunk_{chunk.ChunkId}";

        // --- Road visual: smooth road that follows the chunk's heading ---
        // Each chunk is a straight BoxMesh from start to end. At curve
        // joints the heading changes between chunks, creating a wedge gap.
        // Extend the mesh slightly past each joint (overlap ~chunk width)
        // so the gap is covered. The overlap is along the direction vector.
        var direction = (end - start).Normalized();
        var roadLength = (end - start).Length();
        // Extend mesh by half-width at each end to cover the curve joint gap.
        var overlap = chunk.Width * 0.5f;
        var meshStart = start - direction * overlap;
        var meshEnd = end + direction * overlap;
        var meshLength = roadLength + overlap * 2f;
        var meshMid = (meshStart + meshEnd) / 2f;

        // Sample terrain at multiple points across the mesh area and use the
        // MAX height so the flat road box always sits above terrain bumps.
        // A single midpoint sample lets terrain poke through the road surface
        // and block the car (the terrain HeightMapShape3D is continuous under
        // the road).

        var roadWidth = chunk.Width;
        var perpVec = new Vector3(-direction.Z, 0, direction.X);
        var halfW = roadWidth / 2f;
        var maxH = float.MinValue;
        var steps = new[] { 0f, 0.25f, 0.5f, 0.75f, 1f };
        var sides = new[] { -halfW, halfW };
        foreach (var t in steps)
        {
            var bx = start.X + (end.X - start.X) * t;
            var bz = start.Z + (end.Z - start.Z) * t;
            foreach (var s in sides)
            {
                var px = bx + perpVec.X * s;
                var pz = bz + perpVec.Z * s;
                var h = TerrainHeight.Sample(px, pz);
                if (h > maxH) maxH = h;
            }
        }
        var roadY = maxH + 0.05f;

        // Main road surface: extended BoxMesh that overlaps into curve joints.
        var roadBox = new BoxMesh
        {
            Size = new Vector3(chunk.Width, 0.3f, meshLength),
            Material = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.10f, 0.10f, 0.12f),
                Roughness = 0.95f,
                Metallic = 0.0f,
            },
        };
        var roadInstance = new MeshInstance3D { Mesh = roadBox, Name = "road_surface" };
        roadInstance.Position = meshMid + new Vector3(0, roadY, 0);
        roadInstance.Rotation = new Vector3(0, Mathf.Atan2(direction.X, -direction.Z), 0);
        node.AddChild(roadInstance);

        // Flat collision surface at the road visual height. Without this the
        // car drives on the bumpy terrain HeightMapShape3D, getting stuck on
        // hills. The box is 0.3m thick (matching the visual mesh depth),
        // centered at roadY so its top surface is at roadY+0.15.
        var roadCol = new StaticBody3D { Name = "road_collision" };
        var colShape = new BoxShape3D
        {
            Size = new Vector3(chunk.Width, 0.3f, meshLength),
        };
        roadCol.AddChild(new CollisionShape3D { Shape = colShape });
        roadCol.Position = meshMid + new Vector3(0, roadY, 0);
        roadCol.Rotation = new Vector3(0, Mathf.Atan2(direction.X, -direction.Z), 0);
        node.AddChild(roadCol);

        // Yellow center line: extended to match road mesh.
        var centerLine = new BoxMesh
        {
            Size = new Vector3(0.3f, 0.05f, meshLength - 1f),
            Material = new StandardMaterial3D
            {
                AlbedoColor = new Color(1.0f, 0.95f, 0.2f),
                EmissionEnabled = true,
                Emission = new Color(0.7f, 0.65f, 0.05f),
                EmissionEnergyMultiplier = 3.0f,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        var lineInstance = new MeshInstance3D { Mesh = centerLine, Name = "center_line" };
        lineInstance.Position = meshMid + new Vector3(0, roadY + 0.2f, 0);
        lineInstance.Rotation = new Vector3(0, Mathf.Atan2(direction.X, -direction.Z), 0);
        node.AddChild(lineInstance);

        // --- Invisible side walls to keep the car on the road ---
        // Two thin tall StaticBody3D boxes along the road edges. They have
        // collision but no visible mesh — pure physical barriers.
        //
        // The wall INNER edge is at width/2 + carBodyHalfWidth + safety,
        // so the car's outer edge never overlaps the wall geometry. Without
        // this clearance, the car body (1.8m wide, half=0.9m) was wedging
        // 0.04m into the wall at perpendicular chunks, triggering 8-contact
        // stuck states that the tractor beam + off-road penalty couldn't
        // escape (Bug 2026-07-09: car wedged in left wall of chunk at Z=-160).
        var perp = new Vector3(-direction.Z, 0, direction.X);
        var wallHeight = 1.5f;
        var wallThickness = 0.3f;
        var carBodyHalfWidth = 0.9f;     // matches PlayerCar.CollisionSize.X/2
        var wallInnerClearance = 0.2f;   // safety margin
        var wallInnerEdge = chunk.Width / 2f + carBodyHalfWidth + wallInnerClearance;
        var wallOffset = wallInnerEdge + wallThickness / 2f;

        // Lift walls to road height so they don't intersect terrain below
        // and stay flush with the raised road surface.
        var wallMidY = roadY + 0.05f + wallHeight / 2f;

        SpawnSideWall(node, start, end, direction, perp, wallOffset, wallThickness, wallHeight, -1, wallMidY);
        SpawnSideWall(node, start, end, direction, perp, wallOffset, wallThickness, wallHeight, +1, wallMidY);

        // --- Side obstacles ---
        if (!string.IsNullOrWhiteSpace(chunk.SideLeft) && chunk.SideLeft != "none")
        {
            var leftSeed = (uint)(chunk.ChunkId * 7 + 1);
            BuildingScatter.Scatter(
                node, start, end, chunk.Width,
                chunk.SideLeft, -1, leftSeed);
        }
        if (!string.IsNullOrWhiteSpace(chunk.SideRight) && chunk.SideRight != "none")
        {
            var rightSeed = (uint)(chunk.ChunkId * 7 + 2);
            BuildingScatter.Scatter(
                node, start, end, chunk.Width,
                chunk.SideRight, 1, rightSeed);
        }

        // --- Dead-end blocker ---
        if (chunk.DeadEnd && _chunks.Count > 0) // dead-end only after the first chunk frees up from seed
        {
            var blocker = DeadEndBlocker.Build(start, end, chunk.Width);
            node.AddChild(blocker);
            GD.Print($"[ChunkedRoadManager] Dead-end at chunk {chunk.ChunkId}");
        }

        AddChild(node);
        _chunks.Add(new ChunkNode(chunk.ChunkId, start, end, chunk.Width, node));
    }

    /// <summary>Minimal in-memory fallback RNG.</summary>
    private sealed class Random
    {
        private uint _state;
        public Random(uint seed) => _state = seed == 0 ? 0xdeadbeefu : seed;
        public float Range(float lo, float hi)
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return lo + (hi - lo) * ((_state & 0x00FFFFFF) / (float)0x01000000);
        }
        public uint Next() { Range(0, 1); return _state; }
    }

    /// <summary>Tracked chunk state.</summary>
    public sealed record ChunkNode(int Id, Vector3 StartPosition, Vector3 EndPosition, float Width, Node3D? Node);

    /// <summary>
    /// Find the chunk whose road segment the given world position projects
    /// onto. Returns null if the position is not within any active chunk
    /// (e.g. car is between freed chunks during streaming).
    /// </summary>
    public ChunkNode? FindChunkFor(Vector3 worldPos)
    {
        ChunkNode? best = null;
        float bestT = float.MaxValue;
        foreach (var c in _chunks)
        {
            // Parametric distance along the segment, clamped 0..1.
            var seg = c.EndPosition - c.StartPosition;
            var segLen = seg.Length();
            if (segLen < 0.01f) continue;
            var t = ((worldPos - c.StartPosition).Dot(seg) / (segLen * segLen));
            t = Math.Clamp(t, 0f, 1f);
            var projection = c.StartPosition + seg * t;
            var perpDist = (worldPos - projection).Length();
            // Prefer the chunk where the projection falls inside [0,1]
            // (the car's "on this road segment" case).
            var clampedT = Math.Clamp(((worldPos - c.StartPosition).Dot(seg) / (segLen * segLen)), 0f, 1f);
            var score = (clampedT == t ? 0f : 1f) + perpDist * 0.01f;
            if (score < bestT)
            {
                bestT = score;
                best = c;
            }
        }
        return best;
    }

    /// <summary>
    /// Pure-math: given the Z-positions of all active chunk ends and the
    /// car's Z, return the distance from the car to the FURTHEST-ahead
    /// chunk end. This is the trigger for refetching — when the road
    /// itself is about to run out (not just the closest chunk).
    ///
    /// "Ahead" in Godot is more-negative Z (car drives in -Z). Chunks with
    /// EndZ >= carZ are not ahead (they're beside or behind the car) and
    /// are ignored. Returns 0f if no chunks are ahead, meaning a fetch
    /// should fire immediately.
    ///
    /// (Previous bug: this used the highest endZ — i.e. the seed chunk at
    /// 60m — and triggered an infinite fetch loop spawning chunks at
    /// z=-215000m. Use this helper to get the right answer.)
    /// </summary>
    public static float DistanceToFurthestChunkEnd(IEnumerable<float> chunkEndZs, float carZ)
    {
        var furthestEndZ = float.PositiveInfinity;
        var hasAhead = false;
        foreach (var endZ in chunkEndZs)
        {
            if (endZ < carZ)
            {
                hasAhead = true;
                if (endZ < furthestEndZ)
                    furthestEndZ = endZ;
            }
        }
        return hasAhead ? carZ - furthestEndZ : 0f;
    }

    /// <summary>
    /// Pure-math: for a road segment running from chunkStart to
    /// chunkEnd with given width, compute the lateral distance from worldPos
    /// to the centerline. Returns (perpDistance, halfWidth, isOnRoad).
    /// Extracted as a static so it's testable without instantiating Godot nodes.
    /// </summary>
    public static (float distanceFromCenterline, float roadHalfWidth, bool isOnRoad) ComputeRoadDistance(
        Vector3 chunkStart, Vector3 chunkEnd, float width, Vector3 worldPos)
    {
        var seg = chunkEnd - chunkStart;
        var segLen = seg.Length();
        if (segLen < 0.01f) return (float.PositiveInfinity, width / 2f, false);

        var t = (worldPos - chunkStart).Dot(seg) / (segLen * segLen);
        t = Math.Clamp(t, 0f, 1f);
        var projection = chunkStart + seg * t;
        var perpDist = (worldPos - projection).Length();
        var halfWidth = width / 2f;
        // Tight on-road boundary — any car body overlap with the road edge
        // (or beyond) is treated as off-road. The 1.1× tolerance allowed
        // the car to drive halfway into a wall while still considered
        // "on road", causing invisible obstacle contacts (Bug 2026-07-09).
        return (perpDist, halfWidth, perpDist <= halfWidth);
    }

    /// <summary>
    /// Pure-math: for a road segment running from chunkStart to chunkEnd,
    /// return the nearest centerline point to worldPos AND the lateral
    /// vector from that point to worldPos (i.e. the direction the car
    /// needs to be pulled to get back on the road). Returns false if
    /// the segment is degenerate.
    /// Used by CarController to apply a soft "tractor beam" force when
    /// the car is off-road, so it can't get stranded past the side walls.
    /// </summary>
    public static bool TryNearestCenterlinePoint(
        Vector3 chunkStart, Vector3 chunkEnd, Vector3 worldPos,
        out Vector3 centerlinePoint, out Vector3 pullVector)
    {
        var seg = chunkEnd - chunkStart;
        var segLen = seg.Length();
        if (segLen < 0.01f) { centerlinePoint = default; pullVector = default; return false; }

        var t = Math.Clamp((worldPos - chunkStart).Dot(seg) / (segLen * segLen), 0f, 1f);
        centerlinePoint = chunkStart + seg * t;
        pullVector = worldPos - centerlinePoint;  // points FROM road TO car
        return true;
    }

    /// <summary>
    /// Compute the lateral distance from a world position to the nearest
    /// road centerline. Returns a positive number = distance off-road in
    /// metres; the chunk's Width/2 is the road's edge.
    /// </summary>
    public (float distanceFromCenterline, float roadHalfWidth, bool isOnRoad) GetRoadDistance(Vector3 worldPos)
    {
        var chunk = FindChunkFor(worldPos);
        if (chunk is null) return (float.PositiveInfinity, 0f, false);

        return ComputeRoadDistance(chunk.StartPosition, chunk.EndPosition, chunk.Width, worldPos);
    }

    /// <summary>
    /// Returns the vector pointing from the nearest road centerline point
    /// to the given world position. Length is the off-road distance.
    /// Used by CarController to apply a soft "tractor beam" force when
    /// the car has gone past the side walls, so it can return to the
    /// road instead of being stranded.
    /// Returns Vector3.Zero if no chunk is nearby.
    /// </summary>
    public Vector3 GetPullVector(Vector3 worldPos)
    {
        var chunk = FindChunkFor(worldPos);
        if (chunk is null) return Vector3.Zero;
        if (!TryNearestCenterlinePoint(chunk.StartPosition, chunk.EndPosition, worldPos,
                out _, out var pull))
            return Vector3.Zero;
        return pull;
    }
}

/// <summary>
/// DTO for deserializing JSON road chunk plans in the Godot client.
/// Mirrors the Shared model but avoids assembly reference conflicts.
/// </summary>
internal sealed class RoadChunkData
{
    public IReadOnlyList<ChunkSegment>? Chunks { get; init; }
}

internal sealed class ChunkSegment
{
    [JsonPropertyName("chunk_id")]
    public int ChunkId { get; init; }
    [JsonPropertyName("start")]
    public float[] Start { get; init; } = Array.Empty<float>();
    [JsonPropertyName("end")]
    public float[] End { get; init; } = Array.Empty<float>();
    [JsonPropertyName("width")]
    public float Width { get; init; } = 8f;
    [JsonPropertyName("dead_end")]
    public bool DeadEnd { get; init; }
    [JsonPropertyName("side_left")]
    public string SideLeft { get; init; } = "none";
    [JsonPropertyName("side_right")]
    public string SideRight { get; init; } = "none";
    [JsonPropertyName("terrain_bias")]
    public string TerrainBias { get; init; } = "";
}