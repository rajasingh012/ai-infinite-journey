# AI Infinite Journey

A small open-world driving game where an LLM acts as a **World Director**.
The LLM never creates meshes or physics. It outputs a high-level journey plan.
A procedural generator assembles reusable prefabs into a playable world.

## Stack

- Engine: Godot 4.x with C#
- Language: C# (.NET 8)
- AI Backend: ASP.NET Core Web API
- LLM: OpenAI Chat Completions / Responses API
- Storage: SQLite
- Source Control: Git

## Architecture

Player -> Chunk Manager -> AI World Director -> Procedural Generator -> Prefabs -> Persistent World Database

## Folder Structure

    Game/      Godot 4 C# project (Assets, Prefabs, Scripts, World)
    Backend/   ASP.NET Core Web API (Api project)
    Shared/    Shared models / contracts between Game and Backend

## Free Assets

- https://kenney.nl/assets
- https://quaternius.com
- https://poly.pizza
- https://opengameart.org

Use assets whose licenses allow commercial use; prefer CC0 / Public Domain.

## Milestones

- Phase 1 - Car controller, camera, one road, chunk loading
- Phase 2 - Procedural roads (splines), terrain, trees, buildings, save chunks
- Phase 3 - AI World Director returns JSON (theme, landmarks, hazards)
- Phase 4 - Cargo missions, fuel, weather, dynamic traffic

## Hackathon MVP

Prompt: "Generate a peaceful mountain drive."
Output: City -> Forest -> Lake -> Tunnel -> Village

The player can immediately drive through the generated journey.