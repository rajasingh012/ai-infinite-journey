using System.Text.Json;
using AiInfiniteJourney.Shared;
using Microsoft.Extensions.AI;

namespace AiInfiniteJourney.Api.Services;

/// <summary>
/// LLM-backed World Director. Wraps a Microsoft.Extensions.AI IChatClient and
/// parses the response as a JourneyPlan. Provider-agnostic: the IChatClient
/// is registered in Program.cs (MiniMax today, Gemma tomorrow).
///
/// Uses JSON-mode nudging (request + system instructions) instead of
/// structured-output, so it works with any chat client — including local
/// models via Ollama that may not support typed-result extensions.
/// </summary>
public sealed class LlmWorldDirector : IWorldDirector
{
    private readonly IChatClient _chat;
    private readonly ILogger<LlmWorldDirector> _log;

    private const string SystemPrompt = """
        You are a World Director for an open-world driving game. The player
        gives you a journey prompt (e.g. "a peaceful mountain drive", "a
        stormy night through a desert"). Respond with a single JSON object
        matching this shape and nothing else:

          {
            "theme": "Mountain",
            "description": "One short sentence about the journey.",
            "landmarks": ["Forest", "Lake", "Tunnel", "Village"],
            "hazards": [],
            "weather": "Clear",
            "time_of_day": "Midday",
            "recommended_chunks": 8
          }

        Rules:
        - 3 to 6 landmarks, ordered along the route
        - hazards is a list, can be empty
        - weather and time_of_day are single short words
        - recommended_chunks is an integer between 4 and 12
        - output ONLY the JSON, no markdown fences, no commentary
        """;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public LlmWorldDirector(IChatClient chat, ILogger<LlmWorldDirector> log)
    {
        _chat = chat;
        _log = log;
    }

    public async Task<JourneyPlan> DirectAsync(string prompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt is required.", nameof(prompt));

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, prompt),
        };

        var options = new ChatOptions
        {
            // We rely on the system prompt + a temperature nudge to get JSON back,
            // then parse it manually. This stays compatible with any IChatClient
            // (MiniMax today, Ollama/Gemma tomorrow) regardless of whether the
            // provider supports structured-output schemas.
            Temperature = 0.7f,
        };

        _log.LogInformation("Directing journey: {Prompt}", prompt);
        var response = await _chat.GetResponseAsync(messages, options, ct);

        var text = ExtractText(response);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("LLM returned an empty response.");

        // Log a short preview of the raw text for debugging bad responses.
        var preview = text.Length > 500 ? text[..500] + "..." : text;
        _log.LogInformation("LLM raw response (first 500 chars): {Preview}", preview);

        // Some models wrap JSON in ```json ... ``` fences; strip them.
        text = StripCodeFences(text);

        try
        {
            var plan = JsonSerializer.Deserialize<JourneyPlan>(text, JsonOpts);
            if (plan is null)
                throw new InvalidOperationException("LLM response deserialized to null.");
            return plan;
        }
        catch (JsonException ex)
        {
            _log.LogError(ex, "Failed to parse LLM response as JourneyPlan: {Text}", text);
            throw new InvalidOperationException(
                "LLM response was not valid JSON. See logs for the raw text.", ex);
        }
    }

    private static string ExtractText(ChatResponse response)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var msg in response.Messages)
        foreach (var part in msg.Contents)
        {
            if (part is TextContent tc)
                sb.Append(tc.Text);
        }
        return sb.ToString();
    }

    private static string StripCodeFences(string text)
    {
        var t = text.Trim();

        // Strip <think>...</think> reasoning blocks emitted by some models
        // (DeepSeek, MiniMax M-series, etc.). They appear BEFORE the actual answer
        // and break naive JSON parsing.
        while (true)
        {
            var open = t.IndexOf("<think>", StringComparison.Ordinal);
            if (open < 0) break;
            var close = t.IndexOf("</think>", open, StringComparison.Ordinal);
            if (close < 0) break;
            t = t.Remove(open, close - open + "</think>".Length);
        }

        // Strip markdown code fences ```json ... ```
        if (t.StartsWith("```"))
        {
            var firstNewline = t.IndexOf('\n');
            if (firstNewline > 0)
                t = t[(firstNewline + 1)..];
            if (t.EndsWith("```"))
                t = t[..^3];
        }
        else
        {
            // Find the first balanced { ... } object. Reasoning models often
            // put prose around the JSON; this rescues the actual payload.
            var start = t.IndexOf('{');
            if (start >= 0)
            {
                var depth = 0;
                for (var i = start; i < t.Length; i++)
                {
                    if (t[i] == '{') depth++;
                    else if (t[i] == '}') depth--;
                    if (depth == 0)
                    {
                        t = t[start..(i + 1)];
                        break;
                    }
                }
            }
        }

        return t.Trim();
    }
}
