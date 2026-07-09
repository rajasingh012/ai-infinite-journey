using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AiInfiniteJourney.Shared;
using Godot;

namespace AiInfiniteJourney;

/// <summary>
/// Calls the ASP.NET World Director and parses the response into a
/// JourneyPlan. The HTTP layer is plain System.Net.Http — no Godot
/// HTTPRequest node needed, because we want a simple async Task the
/// title-screen script can await.
///
/// Endpoint and prompt are configurable via the [Export] properties so
/// they can be tweaked in the editor or overridden by environment in
/// future. The base URL defaults to the local dev API on :5080.
///
/// Signals carry only primitive types (Godot constraint — see GD0202);
/// callers fetch the parsed plan from the Task return value.
/// </summary>
public partial class JourneyApi : Node
{
    [Export] public string BaseUrl { get; set; } = "http://localhost:5080";
    [Export] public string EndpointPath { get; set; } = "/api/world/direct";

    [Signal] public delegate void JourneyLoadedEventHandler();
    [Signal] public delegate void JourneyFailedEventHandler(string error);

    /// <summary>Last chunk endpoint fetched — used by the road manager.</summary>
    public float LastChunkEndX { get; private set; }
    public float LastChunkEndZ { get; private set; }
    public int LastChunkCount { get; private set; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly System.Net.Http.HttpClient Http = new()
    {
        // The LLM backend can take 60-180s for reasoning models.
        Timeout = TimeSpan.FromSeconds(180),
    };

    public async Task<JourneyPlan?> DirectAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            EmitSignal(SignalName.JourneyFailed, "prompt is empty");
            return null;
        }

        var url = BaseUrl.TrimEnd('/') + EndpointPath;
        var payload = JsonSerializer.Serialize(new DirectRequest { Prompt = prompt });

        try
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var resp = await Http.PostAsync(url, content);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                var msg = $"HTTP {(int)resp.StatusCode} from {url}: {Truncate(body, 200)}";
                GD.PushError($"[JourneyApi] {msg}");
                EmitSignal(SignalName.JourneyFailed, msg);
                return null;
            }

            var plan = JsonSerializer.Deserialize<JourneyPlan>(body, JsonOpts);
            if (plan is null)
            {
                EmitSignal(SignalName.JourneyFailed, "response deserialized to null");
                return null;
            }

            GD.Print($"[JourneyApi] loaded plan: theme={plan.Theme}, " +
                     $"contract={(plan.Contract is null ? "none" : plan.Contract.Cargo)}");
            EmitSignal(SignalName.JourneyLoaded);
            return plan;
        }
        catch (TaskCanceledException)
        {
            EmitSignal(SignalName.JourneyFailed, "request timed out");
            return null;
        }
        catch (HttpRequestException e)
        {
            var msg = $"network error: {e.Message}";
            GD.PushError($"[JourneyApi] {msg}");
            EmitSignal(SignalName.JourneyFailed, msg);
            return null;
        }
        catch (JsonException e)
        {
            var msg = $"response was not valid JSON: {e.Message}";
            GD.PushError($"[JourneyApi] {msg}");
            EmitSignal(SignalName.JourneyFailed, msg);
            return null;
        }
    }

    /// <summary>
    /// Fetch the next batch of road chunks ahead of the player. Returns raw JSON
    /// string — the caller parses it into a RoadChunkPlan. This avoids adding a
    /// Shared project reference to the Godot game assembly (it already references
    /// the Shared DLL via the .csproj, but plain System.Text.Json is lighter).
    /// </summary>
    public async Task<string?> ChunkAsync(
        float prevEndX, float prevEndZ, string theme, float distanceAhead)
    {
        var url = BaseUrl.TrimEnd('/') + "/api/world/chunk";
        var payload = JsonSerializer.Serialize(new
        {
            prev_end_x = prevEndX,
            prev_end_z = prevEndZ,
            theme = theme ?? "",
            distance_ahead = distanceAhead,
        });

        try
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var resp = await Http.PostAsync(url, content);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                var msg = $"HTTP {(int)resp.StatusCode} from {url}: {Truncate(body, 200)}";
                GD.PushError($"[JourneyApi] {msg}");
                return null;
            }

            // Track the last chunk's end point for chaining.
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("chunks", out var chunks) &&
                    chunks.GetArrayLength() > 0)
                {
                    var last = chunks[chunks.GetArrayLength() - 1];
                    if (last.TryGetProperty("end", out var end) && end.GetArrayLength() == 3)
                    {
                        LastChunkEndX = (float)end[0].GetDouble();
                        LastChunkEndZ = (float)end[2].GetDouble();
                        LastChunkCount = chunks.GetArrayLength();
                    }
                }
            }
            catch { /* leave LastChunk* at zero on parse failure */ }

            return body;
        }
        catch (TaskCanceledException)
        {
            GD.Print("[JourneyApi] chunk request timed out (LLM slow)");
            return null;
        }
        catch (HttpRequestException e)
        {
            GD.PushError($"[JourneyApi] chunk network error: {e.Message}");
            return null;
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "\u2026";
}
