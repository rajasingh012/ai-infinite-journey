using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AiInfiniteJourney.Shared;

/// <summary>
/// High-level journey plan produced by the AI World Director and consumed by
/// the procedural generator in the game client. Keep this stable — both the
/// backend and Godot project reference it.
/// </summary>
public sealed class JourneyPlan
{
    [JsonPropertyName("theme")]
    public string Theme { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("landmarks")]
    public IReadOnlyList<string> Landmarks { get; init; } = Array.Empty<string>();

    [JsonPropertyName("hazards")]
    public IReadOnlyList<string> Hazards { get; init; } = Array.Empty<string>();

    [JsonPropertyName("weather")]
    public string Weather { get; init; } = "Clear";

    [JsonPropertyName("time_of_day")]
    public string TimeOfDay { get; init; } = "Midday";

    [JsonPropertyName("recommended_chunks")]
    public int RecommendedChunks { get; init; } = 8;
}

/// <summary>
/// Request body for POST /api/world/direct
/// </summary>
public sealed class DirectRequest
{
    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = "";
}
