using System.Runtime.CompilerServices;
using System.Text.Json;
using AiInfiniteJourney.Shared;
using Microsoft.Extensions.AI;

namespace AiInfiniteJourney.Api.Services;

/// <summary>
/// Deterministic in-process IChatClient. Used in two places:
///   1. Local dev / CI when MINIMAX_API_KEY is missing
///   2. The "Mock" provider slot in Program.cs (default when no provider is configured)
/// Always returns a sensible sample JourneyPlan so the game client can be
/// tested end-to-end without a network round-trip.
/// </summary>
public sealed class MockChatClient : IChatClient
{
    public ChatClientMetadata Metadata { get; } = new("mock", new Uri("http://localhost"));

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Simulate a small bit of latency so the API feels real.
        await Task.Delay(10, cancellationToken);

        var userPrompt = messages
            .OfType<ChatMessage>()
            .LastOrDefault(m => m.Role == ChatRole.User)
            ?.Text ?? "an unspecified journey";

        var plan = BuildSamplePlan(userPrompt);
        var json = JsonSerializer.Serialize(plan);
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, json));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(response.Messages[0].Role, response.Messages[0].Text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }

    private static JourneyPlan BuildSamplePlan(string prompt) => new()
    {
        Theme = DeriveTheme(prompt),
        Description = $"A {DeriveTheme(prompt).ToLowerInvariant()} drive inspired by: \"{prompt}\".",
        Landmarks = new[] { "Forest", "Lake", "Tunnel", "Village" },
        Hazards = Array.Empty<string>(),
        Weather = "Clear",
        TimeOfDay = "Midday",
        RecommendedChunks = 8,
        Contract = new DeliveryContract
        {
            PickupLabel = "Depot",
            DropoffLabel = "Lodge",
            Cargo = "Supplies",
            Payout = 500,
            Narrative = "Drive safe.",
        },
    };

    private static string DeriveTheme(string prompt)
    {
        var p = prompt.ToLowerInvariant();
        if (p.Contains("mountain")) return "Mountain";
        if (p.Contains("desert")) return "Desert";
        if (p.Contains("city") || p.Contains("urban")) return "Urban";
        if (p.Contains("night")) return "Night";
        if (p.Contains("storm") || p.Contains("rain")) return "Stormy";
        return "Countryside";
    }
}
