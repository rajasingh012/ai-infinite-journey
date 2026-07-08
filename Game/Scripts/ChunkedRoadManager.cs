using System;
using System.Collections.Generic;
using System.Text.Json;
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

            // Keep window: chunk must overlap [-100, +200] metres around car
            // in the Z axis (ahead = more negative, behind = more positive).
            var behind = endZ > carZ + 100f;        // chunk end is more positive than 100m behind car
            var tooFarAhead = startZ < carZ - 200f;  // chunk start is more than 200m ahead

            if (behind || tooFarAhead)
            {
                _chunks[i].Node?.QueueFree();
                _chunks.RemoveAt(i);
            }
        }
    }

    private void OnTimerTick()
    {
        if (_fetching) return;
        // Only fetch more if we have fewer than 3 active chunks.
        if (_chunks.Count >= 3) return;
        FetchNextBatch();
    }

    private async void FetchNextBatch()
    {
        if (_fetching) return;
        _fetching = true;

        string? json;
        if (_api is not null && _car is not null)
        {
            // Anchor the API request to the car's current position so chunks
            // always appear immediately in front of the player, not chained
            // from a far-away historical end-point.
            json = await _api.ChunkAsync(
                _car.Position.X, _car.Position.Z,
                Theme, 150f);
        }
        else
        {
            json = null;
        }

        if (json is null)
        {
            GD.Print("[ChunkedRoadManager] API unavailable — using fallback generator");
            var fallback = BuildFallback();
            foreach (var chunk in fallback)
                AddChunkNode(chunk);
        }
        else
        {
            try
            {
                var plan = JsonSerializer.Deserialize<RoadChunkData>(json, JsonOpts);
                if (plan?.Chunks is { Count: > 0 })
                {
                    foreach (var chunk in plan.Chunks)
                        AddChunkNode(chunk);

                    // Update next end from API's tracked value.
                    if (_api is not null)
                    {
                        _nextChunkEndX = _api.LastChunkEndX;
                        _nextChunkEndZ = _api.LastChunkEndZ;
                    }
                }
            }
            catch (JsonException)
            {
                GD.PushWarning("[ChunkedRoadManager] Failed to parse chunk JSON, using fallback");
                var fallback = BuildFallback();
                foreach (var chunk in fallback)
                    AddChunkNode(chunk);
            }
        }

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
        // Anchor the fallback to the car's current position so chunks always
        // appear immediately in front of the player, not chained from a
        // far-away historical end-point. The car moves in -Z; if it's ahead
        // of the previous batch's end, we use car position. Otherwise we
        // chain from the previous end (which is still ahead).
        var carPos = _car?.Position;
        var anchorX = carPos?.X ?? _nextChunkEndX;
        var anchorZ = carPos?.Z ?? _nextChunkEndZ;
        // If the car is already past where the last chunk ended (it drove
        // forward), anchor from the car so the new batch starts right at
        // the wheels.
        if (anchorZ > _nextChunkEndZ)
        {
            anchorZ = _nextChunkEndZ; // car is behind previous batch end; chain normally
        }

        var rng = new Random(
            (uint)(anchorX.GetHashCode() ^ anchorZ.GetHashCode() ^ Theme.GetHashCode()) ^ 0x9e3779b9u);
        var chunks = new List<ChunkSegment>();
        var posX = anchorX;
        var posZ = anchorZ;
        // Mild initial heading so the road starts straight ahead, then drifts.
        var headingDeg = rng.Range(-5f, 5f);

        for (int i = 0; i < 2; i++)
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

            var deadEnd = (i == 1 && rng.Next() < 0.30f);

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
            Size = new Vector3(wallLength, height, thickness),
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

        // --- Road visual: bright BoxMesh plane that sits above the terrain ---
        var direction = (end - start).Normalized();
        var roadLength = (end - start).Length();

        // Sample terrain heights at multiple points. Use a single average
        // (terrain is smooth within a chunk) for the simple BoxMesh approach —
        // ImmediateMesh was failing to render visibly.
        var midPoint = (start + end) / 2;
        var midHeight = TerrainHeight.Sample(midPoint.X, midPoint.Z);

        // Main road surface: a bright, opaque BoxMesh that's GUARANTEED to
        // render. BoxMesh has no backface-culling issues and survives
        // compression artefacts that ImmediateMesh suffered from.
        var roadBox = new BoxMesh
        {
            Size = new Vector3(roadLength, 0.3f, chunk.Width),
            Material = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.10f, 0.10f, 0.12f), // very dark grey
                Roughness = 0.95f,
                Metallic = 0.0f,
            },
        };
        var roadInstance = new MeshInstance3D { Mesh = roadBox, Name = "road_surface" };
        // Lift the box above the terrain so the top face sits ~0.5m above
        // the terrain at the chunk midpoint. Without this the road sinks
        // under hills (terrain at +3m) or floats above valleys (terrain at
        // -3m), making it invisible from the chase camera.
        roadInstance.Position = midPoint + new Vector3(0, midHeight + 0.5f, 0);
        roadInstance.Rotation = new Vector3(0, Mathf.Atan2(direction.X, -direction.Z), 0);
        node.AddChild(roadInstance);

        // Yellow center line: bright, slightly-raised thin box.
        var centerLine = new BoxMesh
        {
            Size = new Vector3(roadLength - 1f, 0.05f, 0.3f),
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
        // Stack on top of road box (Y offset = road box top + small gap).
        lineInstance.Position = midPoint + new Vector3(0, midHeight + 0.7f, 0);
        lineInstance.Rotation = new Vector3(0, Mathf.Atan2(direction.X, -direction.Z), 0);
        node.AddChild(lineInstance);

        // --- Invisible side walls to keep the car on the road ---
        // Two thin tall StaticBody3D boxes along the road edges. They have
        // collision but no visible mesh — pure physical barriers.
        var perp = new Vector3(-direction.Z, 0, direction.X);
        var wallHeight = 1.5f;
        var wallThickness = 0.3f;
        var wallOffset = chunk.Width / 2f + wallThickness / 2f;

        // Lift walls to terrain height so they don't intersect terrain below.
        var wallMidY = TerrainHeight.Sample((start.X + end.X) / 2f, (start.Z + end.Z) / 2f) + wallHeight / 2f;

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
    /// Pure-math projection: for a road segment running from chunkStart to
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
        return (perpDist, halfWidth, perpDist <= halfWidth);
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
    public int ChunkId { get; init; }
    public float[] Start { get; init; } = Array.Empty<float>();
    public float[] End { get; init; } = Array.Empty<float>();
    public float Width { get; init; } = 8f;
    public bool DeadEnd { get; init; }
    public string SideLeft { get; init; } = "none";
    public string SideRight { get; init; } = "none";
    public string TerrainBias { get; init; } = "";
}