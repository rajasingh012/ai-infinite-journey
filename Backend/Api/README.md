# Backend/Api

ASP.NET Core 8 Web API that acts as the **AI World Director**.

- `Program.cs` - top-level DI + provider switch (Mock / MiniMax today; add Gemma/Ollama/OpenAI by adding a switch case)
- `Controllers/WorldController.cs` - `POST /api/world/direct`, `GET /api/world/health`
- `Services/IWorldDirector.cs` - the abstraction
- `Services/LlmWorldDirector.cs` - JSON-mode prompt + manual parse, works with any IChatClient
- `Services/MockChatClient.cs` - in-process deterministic client for CI / offline dev
- `AiInfiniteJourney.Api.csproj` - Microsoft.Extensions.AI 10.5.0 + Microsoft.Extensions.AI.OpenAI 10.5.0

## Run

```bash
cd Backend/Api
dotnet run
# listens on http://localhost:5080
```

## Configure the LLM

The `Llm:Provider` config key selects the backend:

| Provider | Behavior | Needs |
|---|---|---|
| `Mock` (default) | Returns a deterministic sample plan, no network | nothing |
| `MiniMax` | Calls MiniMax's OpenAI-compatible endpoint | `Llm:ApiKey`, optional `Llm:Endpoint`, `Llm:Model` |

Set your key with user-secrets (recommended) or env var:

```bash
# user-secrets (lives in ~/.microsoft/usersecrets/, never in the repo)
cd Backend/Api
dotnet user-secrets set "Llm:ApiKey" "sk-..."
dotnet user-secrets set "Llm:Provider" "MiniMax"

# ...or env var (overrides appsettings):
export Llm__ApiKey="sk-..."
export Llm__Provider="MiniMax"
```

## Try it

```bash
curl -s http://localhost:5080/api/world/health

curl -s -X POST http://localhost:5080/api/world/direct \
  -H 'Content-Type: application/json' \
  -d '{"prompt": "a peaceful mountain drive"}'
```

## Adding a new provider (Gemma, OpenAI, Anthropic, Ollama...)

1. Add a `case "gemma":` (or whatever) in `Program.cs` next to the existing `Mock` / `MiniMax` cases.
2. Register the matching `IChatClient` in DI.
3. That's it — `LlmWorldDirector` and `WorldController` don't change.

For local Ollama + Gemma:
```csharp
// dotnet add package Microsoft.Extensions.AI.Ollama
case "ollama":
    var ollama = new OllamaChatClient(new Uri("http://localhost:11434"), "gemma3:4b");
    builder.Services.AddSingleton<IChatClient>(ollama);
    break;
```
