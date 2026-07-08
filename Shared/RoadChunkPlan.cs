using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AiInfiniteJourney.Shared;

/// <summary>
/// A multi-segment road plan produced by the World Director while the player
/// drives. The game keeps a small queue of these ahead of the car and frees
/// them once the car passes.
///
/// Each chunk describes a single straight road segment with width and side
/// obstacle themes. Chunks connect end-to-start to form a curvy road.
/// </summary>
public sealed class RoadChunkPlan
{
    [JsonPropertyName("chunks")]
    public IReadOnlyList<RoadChunk> Chunks { get; init; } = Array.Empty<RoadChunk>();
}

/// <summary>
/// One road segment. <see cref="Start"/> and <see cref="End"/> are world-space
/// (x, y, z). The next chunk's Start MUST equal this chunk's End so the road
/// is continuous.
/// </summary>
public sealed class RoadChunk
{
    [JsonPropertyName("chunk_id")]
    public int ChunkId { get; init; }

    [JsonPropertyName("start")]
    public float[] Start { get; init; } = Array.Empty<float>();

    [JsonPropertyName("end")]
    public float[] End { get; init; } = Array.Empty<float>();

    /// <summary>Road width in metres (perpendicular to segment direction).</summary>
    [JsonPropertyName("width")]
    public float Width { get; init; } = 8f;

    /// <summary>If true, the road ends here — game spawns a solid blocker.</summary>
    [JsonPropertyName("dead_end")]
    public bool DeadEnd { get; init; }

    /// <summary>
    /// Keyword for left-side obstacles. One of:
    /// "buildings", "trees", "rocks", "sparse_buildings", "none".
    /// </summary>
    [JsonPropertyName("side_left")]
    public string SideLeft { get; init; } = "none";

    /// <summary>Same vocabulary as <see cref="SideLeft"/>.</summary>
    [JsonPropertyName("side_right")]
    public string SideRight { get; init; } = "none";

    /// <summary>
    /// Optional terrain nudge for this chunk. The shared TerrainHeight function
    /// already produces a deterministic shape, but this lets the LLM request
    /// a hill, valley, or flat stretch. Free-form string, game interprets best-effort.
    /// </summary>
    [JsonPropertyName("terrain_bias")]
    public string TerrainBias { get; init; } = "";
}

/// <summary>
/// Request body for POST /api/world/chunk. Caller reports where the previous
/// chunk ended and how far ahead they want planned, plus the active theme so
/// the LLM keeps continuity.
/// </summary>
public sealed class ChunkRequest
{
    [JsonPropertyName("prev_end_x")]
    public float PrevEndX { get; init; }

    [JsonPropertyName("prev_end_z")]
    public float PrevEndZ { get; init; }

    [JsonPropertyName("theme")]
    public string Theme { get; init; } = "";

    [JsonPropertyName("distance_ahead")]
    public float DistanceAhead { get; init; } = 150f;
}