# AI Infinite Journey

**"AI builds this world while you play."**

Press Enter. An LLM generates a world — theme, weather, time of day. Drive forever through an infinite procedurally-generated road, dressed by the AI's vision. Every run is different.

## Demo flow (hackathon — July 11)

1. **Press Enter** → LLM returns a JourneyPlan (Mountain, Stormy, Night)
2. **World dresses** → sky, fog, ground tint match the AI's output
3. **Drive forever** → road chunks spawn infinitely ahead, despawn behind
4. **Flavor overlay** → shows the LLM's world description on screen

No HUD, no currency, no delivery interaction — just you and an AI-building world.

## Stack

- Engine: Godot 4.x with C# (.NET 8)
- AI Backend: ASP.NET Core Web API
- LLM: MiniMax-M3 (dev) → Gemma 4 31B (production)
- Provider-agnostic via `Microsoft.Extensions.AI`

## Architecture

```
Player → Backend API → LLM (World Director) → JourneyPlan
                                                    ↓
Chunk Manager ← spawns road chunks dressed by theme/weather/time
```

The LLM *directs* — it outputs JSON. The engine *builds* — it spawns and dresses prefabs.

## Folder structure

```
Game/       Godot 4 C# project (scenes, scripts, assets)
Backend/    ASP.NET Core Web API
Shared/     Shared models (JourneyPlan, DeliveryContract)
```

## Hackathon sprint (July 7–11)

| Day | Deliverable |
|-----|-------------|
| Day 1 (Tue) | Backend live, HTTP client working, car moves |
| Day 2 (Wed) | Endless road chunk system — spawn/despawn, theme dressing |
| Day 3 (Thu) | Sky/weather/time visual dressing + flavor HUD |
| Day 4 (Fri) | Polish, end-to-end testing, demo video |
| Day 5 (Sat) | README, submission, ship by 21:00 IST |

## Free assets used

- https://kenney.nl/assets
- https://quaternius.com
- https://poly.pizza
- https://opengameart.org
