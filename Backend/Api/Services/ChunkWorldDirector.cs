using System.Text.Json;
using AiInfiniteJourney.Shared;
using Microsoft.Extensions.AI;

namespace AiInfiniteJourney.Api.Services;

/// <summary>
/// LLM-backed road chunk planner. Same shape as <see cref="LlmWorldDirector"/>:
/// wrap an IChatClient (MiniMax by default), ask for JSON, parse it.
///
/// Returns 2-3 road chunks per request so the road ahead of the player always
/// has visible content. The game polls this on a timer (8s) and keeps a
/// 3-chunk queue. Falls back to <see cref="SyntheticChunkFallback"/> if the
/// LLM errors or times out.
/// </summary>
public sealed class ChunkWorldDirector : IChunkWorldDirector
{
    private readonly IChatClient _chat;
    private readonly ILogger<ChunkWorldDirector> _log;

    private const string SystemPrompt = """
        You are a road planner for an open-world driving game. Given the
        previous road chunk's end point, the active theme, and how far ahead
        the player needs, output 2 or 3 road chunks as a single JSON object
        matching this exact shape and nothing else:

          {
            "chunks": [
              {
                "chunk_id": 0,
                "start": [0.0, 0.0, 0.0],
                "end":   [1.5, 0.0, -60.0],
                "width": 8.0,
                "dead_end": false,
                "side_left": "buildings",
                "side_right": "trees",
                "terrain_bias": ""
              },
              {
                "chunk_id": 1,
                "start": [1.5, 0.0, -60.0],
                "end":   [-2.0, 0.0, -130.0],
                "width": 8.0,
                "dead_end": true,
                "side_left": "rocks",
                "side_right": "sparse_buildings",
                "terrain_bias": "hill"
              }
            ]
          }

        Rules:
        - 2 to 3 chunks total
        - Each chunk is 50 to 80 metres long
        - First chunk's "start" must equal the previous end (passed as
          prev_end_x, prev_end_z)
        - Each chunk's "end" must equal the next chunk's "start" — the road
          is continuous, no gaps
        - "width" is the road width in metres (always 8)
        - "dead_end" is true on the FINAL chunk roughly 30% of the time —
          the road terminates and a building wall blocks the way
        - Lateral drift: end points can vary ±15m in X from straight-ahead
          (negative Z is forward), so the road feels curvy
        - "side_left" and "side_right" are short keywords: "buildings",
          "sparse_buildings", "trees", "rocks", or "none"
        - "terrain_bias" is one of "" (none), "hill", "valley", "flat" —
          empty string if you don't want to specify
        - The forward direction is -Z (Godot convention); stay close to it
        - Output ONLY the JSON, no markdown fences, no commentary
        """;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ChunkWorldDirector(IChatClient chat, ILogger<ChunkWorldDirector> log)
    {
        _chat = chat;
        _log = log;
    }

    public async Task<RoadChunkPlan> DirectAsync(
        float prevEndX, float prevEndZ, string theme, float distanceAhead,
        CancellationToken ct = default)
    {
        var userPrompt = $"theme={theme}\nprev_end_x={prevEndX:F2}\nprev_end_z={prevEndZ:F2}\n" +
                         $"distance_ahead={distanceAhead:F0}\n\nPlan the next road chunks.";
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, userPrompt),
        };
        var options = new ChatOptions { Temperature = 0.7f };

        _log.LogInformation("Planning chunks: theme={Theme}, prev=({X},{Z}), ahead={Dist}",
            theme, prevEndX, prevEndZ, distanceAhead);

        // Same generous 180s timeout as the journey director — MiniMax M-series
        // regularly takes 60-120s, default HttpClient timeout cancels them.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(180));

        var response = await _chat.GetResponseAsync(messages, options, timeoutCts.Token);
        var text = ExtractText(response);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("LLM returned an empty chunk plan.");

        var preview = text.Length > 500 ? text[..500] + "..." : text;
        _log.LogInformation("Chunk LLM raw response (first 500 chars): {Preview}", preview);

        text = StripCodeFences(text);
        try
        {
            var plan = JsonSerializer.Deserialize<RoadChunkPlan>(text, JsonOpts);
            if (plan is null || plan.Chunks.Count == 0)
                throw new InvalidOperationException("LLM chunk plan was empty.");
            return Validate(plan, prevEndX, prevEndZ);
        }
        catch (JsonException ex)
        {
            _log.LogError(ex, "Failed to parse LLM chunk plan: {Text}", text);
            throw new InvalidOperationException(
                "LLM chunk response was not valid JSON. See logs for the raw text.", ex);
        }
    }

    /// <summary>
    /// Repair common LLM mistakes: missing chunk_id, missing start/end arrays,
    /// first chunk not starting at prev_end, gaps between chunks.
    /// </summary>
    private static RoadChunkPlan Validate(RoadChunkPlan plan, float prevEndX, float prevEndZ)
    {
        var fixedChunks = new List<RoadChunk>(plan.Chunks.Count);
        float curX = prevEndX, curZ = prevEndZ;
        for (int i = 0; i < plan.Chunks.Count; i++)
        {
            var c = plan.Chunks[i];
            var start = c.Start.Length == 3 ? c.Start : new[] { curX, 0f, curZ };
            var end = c.End.Length == 3 ? c.End : new[] { curX, 0f, curZ - 60f };

            // Force continuity: this chunk's start must be at the previous end.
            var sx = i == 0 ? prevEndX : curX;
            var sz = i == 0 ? prevEndZ : curZ;

            fixedChunks.Add(new RoadChunk
            {
                ChunkId = i,
                Start = new[] { sx, 0f, sz },
                End = end,
                Width = c.Width > 0 ? c.Width : 8f,
                DeadEnd = c.DeadEnd,
                SideLeft = string.IsNullOrWhiteSpace(c.SideLeft) ? "none" : c.SideLeft,
                SideRight = string.IsNullOrWhiteSpace(c.SideRight) ? "none" : c.SideRight,
                TerrainBias = c.TerrainBias ?? "",
            });

            curX = end[0];
            curZ = end[2];
        }
        return new RoadChunkPlan { Chunks = fixedChunks };
    }

    private static string ExtractText(ChatResponse response)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var msg in response.Messages)
        foreach (var part in msg.Contents)
            if (part is TextContent tc) sb.Append(tc.Text);
        return sb.ToString();
    }

    private static string StripCodeFences(string text)
    {
        var t = text.Trim();
        while (true)
        {
            var open = t.IndexOf("<think>", StringComparison.Ordinal);
            if (open < 0) break;
            var close = t.IndexOf("</think>", open, StringComparison.Ordinal);
            if (close < 0) break;
            t = t.Remove(open, close - open + "</think>".Length);
        }
        if (t.StartsWith("```"))
        {
            var firstNewline = t.IndexOf('\n');
            if (firstNewline > 0) t = t[(firstNewline + 1)..];
            if (t.EndsWith("```")) t = t[..^3];
        }
        else
        {
            var start = t.IndexOf('{');
            if (start >= 0)
            {
                var depth = 0;
                for (var i = start; i < t.Length; i++)
                {
                    if (t[i] == '{') depth++;
                    else if (t[i] == '}') depth--;
                    if (depth == 0) { t = t[start..(i + 1)]; break; }
                }
            }
        }
        return t.Trim();
    }
}