using System.ClientModel;
using AiInfiniteJourney.Api.Services;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

var builder = WebApplication.CreateBuilder(args);

// Load user-secrets explicitly. WebApplication.CreateBuilder only adds them
// automatically in Development, but this is a local dev tool — the secrets
// live in ~/.microsoft/usersecrets/ (chmod 0600, outside the repo) and must
// be available regardless of ASPNETCORE_ENVIRONMENT.
builder.Configuration.AddUserSecrets<Program>();

// --- Configuration: provider switch -----------------------------------------
// The "Provider" config key selects the LLM backend. Today we ship:
//   - "MiniMax"   - uses MiniMax (MiniMax M2 / MiniMax-Text-01) via its OpenAI-compatible endpoint
//   - "Mock"      - returns a deterministic sample plan without any network call (for tests/CI)
// Add more providers (Gemma, OpenAI, Anthropic, Ollama) as separate methods below.
var provider = builder.Configuration["Llm:Provider"] ?? "Mock";
Console.WriteLine($"[startup] LLM provider = {provider}");

// `provider` is case-folded before matching so "MiniMax", "MINIMAX", "minimax"
// all hit the same arm. Set Llm:Provider in user-secrets (or appsettings) to
// one of: "MiniMax" or "Mock".
switch (provider.ToLowerInvariant())
{
    case "minimax":
        ConfigureMiniMax(builder);
        break;
    case "mock":
        ConfigureMock(builder);
        break;
    default:
        throw new InvalidOperationException(
            $"Unknown Llm:Provider '{provider}'. Supported: MiniMax, Mock.");
}

// --- DI for the World Director itself --------------------------------------
builder.Services.AddSingleton<IWorldDirector, LlmWorldDirector>();

// --- Standard ASP.NET plumbing ---------------------------------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();
app.MapControllers();
app.MapGet("/", () => Results.Ok(new
{
    name = "AI Infinite Journey - World Director API",
    endpoints = new[] { "POST /api/world/direct", "GET /api/world/health" },
}));
app.Run();


// --- Provider implementations ------------------------------------------------

static void ConfigureMiniMax(WebApplicationBuilder builder)
{
    var apiKey = builder.Configuration["Llm:ApiKey"]
        ?? Environment.GetEnvironmentVariable("MINIMAX_API_KEY")
        ?? throw new InvalidOperationException(
            "MiniMax provider requires Llm:ApiKey config or MINIMAX_API_KEY env var.");

    var endpoint = builder.Configuration["Llm:Endpoint"] ?? "https://api.MiniMax.io/v1";
    var model = builder.Configuration["Llm:Model"] ?? "MiniMax-Text-01";

    // MiniMax is OpenAI-API-compatible, so we use the official OpenAI client
    // pointed at MiniMax's endpoint. The MS Extensions AI adapter wraps it.
    var openAiClient = new OpenAIClient(
        new ApiKeyCredential(apiKey),
        new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

    var chatClient = openAiClient.GetChatClient(model).AsIChatClient();
    builder.Services.AddSingleton<IChatClient>(chatClient);
    Console.WriteLine($"[startup] MiniMax endpoint={endpoint} model={model}");
}

static void ConfigureMock(WebApplicationBuilder builder)
{
    // For local dev / CI / when the player wants to drive without an LLM call.
    // Returns a deterministic plan that exercises the full pipeline shape.
    var mockClient = new MockChatClient();
    builder.Services.AddSingleton<IChatClient>(mockClient);
    Console.WriteLine("[startup] Mock LLM client (no network)");
}
