using AiInfiniteJourney.Shared;

namespace AiInfiniteJourney.Api.Services;

/// <summary>
/// Last-resort fallback used when the live LLM call fails or times out.
/// Builds a deterministic but variation-rich JourneyPlan from the prompt
/// so the game keeps working even when the network is down or the model
/// is slow. Not as good as a real LLM response — no real reasoning about
/// the journey — but enough for the demo to keep moving.
/// </summary>
internal static class SyntheticFallback
{
    public static JourneyPlan Build(string prompt)
    {
        // Hash the prompt so different prompts still give different plans,
        // but the same prompt always produces the same plan (deterministic).
        var h = unchecked((uint)(prompt?.GetHashCode() ?? 0));
        var theme = Themes[(int)(h % (uint)Themes.Length)];
        var weather = Weathers[(int)((h >> 4) % (uint)Weathers.Length)];
        var time = Times[(int)((h >> 8) % (uint)Times.Length)];
        var pickup = PickupLabels[(int)((h >> 12) % (uint)PickupLabels.Length)];
        var dropoff = DropoffLabels[(int)((h >> 16) % (uint)DropoffLabels.Length)];
        var cargo = Cargos[(int)((h >> 20) % (uint)Cargos.Length)];

        // Payout scales loosely with weather danger — keeps the demo feeling
        // like the LLM is making the call.
        var payout = weather switch
        {
            "Stormy" => 1200,
            "Foggy" => 900,
            "Rainy" => 700,
            "Overcast" => 500,
            _ => 300,
        };

        var landmarks = new[]
        {
            $"Old {theme} Bridge",
            $"{theme} Outlook",
            $"Quiet {theme} Hamlet",
            $"{theme} Market",
            $"{theme} Trailhead",
        };

        return new JourneyPlan
        {
            Theme = theme,
            Description = $"A {weather.ToLowerInvariant()} drive through a {theme.ToLowerInvariant()} region.",
            Landmarks = landmarks,
            Hazards = weather is "Stormy" or "Foggy" ? new[] { "Low Visibility" } : Array.Empty<string>(),
            Weather = weather,
            TimeOfDay = time,
            RecommendedChunks = 8,
            Contract = new DeliveryContract
            {
                PickupLabel = pickup,
                DropoffLabel = dropoff,
                Cargo = cargo,
                Payout = payout,
                Narrative = $"Synthetic plan (live LLM was unavailable for prompt: \"{Truncate(prompt ?? "", 60)}\").",
            },
        };
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    private static readonly string[] Themes = { "Mountain", "Desert", "Coastal", "Forest", "Countryside", "Urban" };
    private static readonly string[] Weathers = { "Clear", "Overcast", "Foggy", "Rainy", "Stormy" };
    private static readonly string[] Times = { "Dawn", "Morning", "Midday", "Afternoon", "Dusk", "Night" };
    private static readonly string[] PickupLabels = { "Town Depot", "Central Warehouse", "Harbor Office", "Trading Post", "Factory Gate" };
    private static readonly string[] DropoffLabels = { "Mountain Lodge", "Village Market", "Hilltop Station", "River Crossing", "Coastal Pier" };
    private static readonly string[] Cargos = { "Medical Supplies", "Fresh Produce", "Spare Parts", "Construction Material", "Mail Packets" };
}
