# AI Infinite Journey — Hackathon MVP Blueprint (Revised)

**"AI builds this world while you play."**

**Deadline:** 2026-07-11 21:00 IST
**Repository:** https://github.com/rajasingh012/ai-infinite-journey

---

## 1. Vision

Press Enter. An LLM generates a world — theme, weather, time of day. Road chunks spawn infinitely ahead, dressed to match the AI's vision. You drive forever. Every run is different.

There is no HUD, no currency, no delivery math. The demo pitch:

> *"Watch an AI build an infinite world while you drive through it."*

## 2. Scope decisions

| Question | Choice | Why |
|----------|--------|-----|
| Road network | **Endless** (tileable chunks, spawn/despawn) | "AI builds world while playing" is the demo pitch |
| HUD / UI | **Flavor overlay** (LLM description, weather, time) | No delivery interaction — show the AI output |
| Cargo / payout | **Removed** | Not needed for the pitch; adds complexity |
| Pickup/dropoff markers | **Removed** | No delivery loop |
| E-key interaction | **Removed** | No interaction beyond driving |
| Weather / time dressing | **Visual only** | Sky, fog, ground tint per LLM output |
| Save / progression | **None** | Fresh run each launch |

## 3. What the LLM outputs (`JourneyPlan`)

```json
{
  "theme": "Mountain",
  "description": "A stormy night through a mountain pass.",
  "landmarks": ["Forest", "Lake", "Tunnel", "Village"],
  "hazards": ["Flash Floods"],
  "weather": "Stormy",
  "time_of_day": "Night",
  "recommended_chunks": 0,
  "contract": null
}
```

`contract` stays in the schema (backward-compatible) but is not shown in the demo. The `description` is the main payload — displayed as flavor text.

## 4. Game-side loop

1. **Title screen** — "Press Enter to start."
2. **LLM call** — Game hits backend → gets JourneyPlan.
3. **World dressing** — Apply weather→fog, time_of_day→sky, theme→ground tint.
4. **Chunk streaming** — Road chunks spawn indefinitely ahead of the car.
5. **Flavor HUD** — Show the LLM description + weather + time.
6. **Drive forever.**

## 5. Stack

- Engine: Godot 4.x with C# (.NET 8)
- AI Backend: ASP.NET Core Web API
- LLM: MiniMax-M3 (dev) → Gemma 4 31B (production)
- Provider-agnostic via Microsoft.Extensions.AI

## 6. Sprint plan (revised)

| Day | Deliverable |
|-----|-------------|
| Day 1 (Tue 7) | Backend live, HTTP client, car moves |
| Day 2 (Wed 8) | Endless road chunk system — RoadChunk prefab, RoadManager spawn/despawn, theme tint |
| Day 3 (Thu 9) | Sky/weather/time visual dressing + flavor HUD overlay |
| Day 4 (Fri 10) | End-to-end from clean checkout, polish, record demo video |
| Day 5 (Sat 11) | README, submission, **ship by 21:00 IST** |

## 7. Post-hackathon

- Delivery gameplay (re-add contract, markers, payout)
- Spline-based procedural roads with curves
- Building placement at landmarks
- Multi-cargo, progression, save
- Gemma 4 31B as production LLM
