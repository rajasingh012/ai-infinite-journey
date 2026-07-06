# Backend

ASP.NET Core (.NET 8) Web API that acts as the **AI World Director**.

Responsibilities:
- Accept a journey prompt from the game client
- Call OpenAI Chat Completions / Responses API
- Return a structured JSON plan: theme, landmarks, hazards, weather, events
- Validate the LLM output and forward it to the procedural generator

Subfolders:
- `Api/`  - Web API project (controllers, DI, OpenAI client)

Configuration:
- OpenAI API key goes in `appsettings.Development.json` (gitignored) or env vars.