# AI Infinite Journey

**"AI builds this world while you play."**

Press Enter. An LLM generates a world — terrain shape, theme, weather, time of day. A procedural builder carves hills and valleys, lays a winding road with bridges and tunnels, places trees and buildings. Drive through a unique 3D world that's different every run.

## Demo flow (hackathon — July 11)

1. **Press Enter** → LLM returns a JourneyPlan (theme, terrain type, weather, time)
2. **World builds** → terrain rises into hills, road snakes through with spline curves, buildings and trees appear
3. **Drive** → follow the road through tunnels, over bridges, past factories and houses
4. **Crash** → trees and buildings are physical. Drive off the road → fall into the terrain

## Stack

- Engine: Godot 4.x with C# (.NET 8)
- AI Backend: ASP.NET Core Web API
- LLM: MiniMax-M3 (dev) → Gemma 4 31B (production)
- Provider-agnostic via `Microsoft.Extensions.AI`

## Architecture

```
Player → Backend API → LLM (World Director) → JourneyPlan
                                                    ↓
              TerrainBuilder (heightmap + collision)
              SplineRoad (curved road mesh + bridge/tunnel detection)
              ObjectPlacer (trees, houses, factories with physics)
```

The LLM *directs* — it outputs JSON. The engine *builds* — terrain, road, and objects from reusable primitives.

## Folder structure

```
Game/       Godot 4 C# project (scenes, scripts, assets)
Backend/    ASP.NET Core Web API
Shared/     Shared models (JourneyPlan, DeliveryContract)
PROGRESS.md Sprint plan, task list, known bugs
```

## Free assets used

- https://kenney.nl/assets
- https://quaternius.com
- https://poly.pizza
- https://opengameart.org

See [PROGRESS.md](./PROGRESS.md) for the full sprint plan and task list.
