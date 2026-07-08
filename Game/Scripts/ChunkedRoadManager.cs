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
        var spawnZ = _car.Position.Z;
        var seedEnd = new Vector3(0, 0, spawnZ - 60f);
        AddChunkNode(
            new ChunkSegment
            {
                ChunkId = -1,
                Start = new[] { 0f, 0f, spawnZ },
                End = new[] { 0f, 0f, spawnZ - 60f },
                Width = 8f,
                DeadEnd = false,
                SideLeft = "none",
                SideRight = "none",
                TerrainBias = "",
            });

        _nextChunkEndX = 0f;
        _nextChunkEndZ = spawnZ - 60f;

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

        // Free chunks the car has passed (more than 100m behind).
        var carZ = _car.Position.Z;
        for (int i = _chunks.Count - 1; i >= 0; i--)
        {
            if (_chunks[i].EndPosition.Z > carZ + 100f) // chunk is ahead
                continue;
            if (_chunks[i].EndPosition.Z < carZ - 100f) // chunk is far behind
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
        if (_api is not null)
        {
            json = await _api.ChunkAsync(
                _nextChunkEndX, _nextChunkEndZ,
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
    /// ahead of the last known end point, same logic as the backend's
    /// SyntheticChunkFallback but running locally so the game works
    /// even without the backend server running.
    /// </summary>
    private List<ChunkSegment> BuildFallback()
    {
        var rng = new Random(
            (uint)(_nextChunkEndX.GetHashCode() ^ _nextChunkEndZ.GetHashCode() ^ Theme.GetHashCode()));
        var chunks = new List<ChunkSegment>();
        var posX = _nextChunkEndX;
        var posZ = _nextChunkEndZ;
        var headingDeg = rng.Range(-20f, 20f);

        for (int i = 0; i < 2; i++)
        {
            var length = rng.Range(50f, 80f);
            var turn = rng.Range(-12f, 12f);
            headingDeg += turn;
            headingDeg = Math.Clamp(headingDeg, -40f, 40f);

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

    private void AddChunkNode(ChunkSegment chunk)
    {
        var start = new Vector3(chunk.Start[0], 0, chunk.Start[2]);
        var end = new Vector3(chunk.End[0], 0, chunk.End[2]);

        GD.Print($"[ChunkedRoadManager] spawn chunk {chunk.ChunkId}: ({start.X:F1},{start.Z:F1})→({end.X:F1},{end.Z:F1}) L={chunk.SideLeft} R={chunk.SideRight}");

        var node = new Node3D();
        node.Name = $"chunk_{chunk.ChunkId}";

        // --- Road visual: flat box between start and end ---
        var direction = (end - start).Normalized();
        var roadLength = (end - start).Length();

        var roadBox = new BoxMesh
        {
            Size = new Vector3(roadLength, 0.1f, chunk.Width),
            Material = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.25f, 0.25f, 0.25f),
                Roughness = 0.8f,
            },
        };
        var roadBoxInstance = new MeshInstance3D { Mesh = roadBox };
        roadBoxInstance.Position = (start + end) / 2 - new Vector3(0, 0.05f, 0);
        roadBoxInstance.Rotation = new Vector3(0, Mathf.Atan2(direction.X, -direction.Z), 0);
        node.AddChild(roadBoxInstance);

        // Center line marking (thin bright strip along the center)
        var centerLine = new BoxMesh
        {
            Size = new Vector3(roadLength - 1f, 0.02f, 0.15f),
            Material = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.9f, 0.9f, 0.5f), // yellow dashed feel
                EmissionEnabled = true,
                Emission = new Color(0.1f, 0.1f, 0.05f),
            },
        };
        var lineInstance = new MeshInstance3D { Mesh = centerLine };
        lineInstance.Position = (start + end) / 2 - new Vector3(0, 0.05f, 0);
        lineInstance.Rotation = new Vector3(0, Mathf.Atan2(direction.X, -direction.Z), 0);
        node.AddChild(lineInstance);

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
        _chunks.Add(new ChunkNode(chunk.ChunkId, end, node));
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
    private sealed record ChunkNode(int Id, Vector3 EndPosition, Node3D? Node);
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