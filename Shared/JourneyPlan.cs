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

    [JsonPropertyName("contract")]
    public DeliveryContract? Contract { get; init; }
}

/// <summary>
/// Delivery contract for a single journey. Produced by the LLM, consumed
/// by the game UI. For the hackathon MVP this is intentionally minimal:
/// pickup/dropoff labels for the HUD, a cargo name, and a payout.
/// </summary>
public sealed class DeliveryContract
{
    [JsonPropertyName("pickup_label")]
    public string PickupLabel { get; init; } = "Pickup";

    [JsonPropertyName("dropoff_label")]
    public string DropoffLabel { get; init; } = "Dropoff";

    [JsonPropertyName("cargo")]
    public string Cargo { get; init; } = "Cargo";

    [JsonPropertyName("payout")]
    public int Payout { get; init; } = 100;

    [JsonPropertyName("narrative")]
    public string Narrative { get; init; } = "";
}

/// <summary>
/// Request body for POST /api/world/direct
/// </summary>
public sealed class DirectRequest
{
    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = "";
}
