# AI Infinite Journey — Hackathon MVP Blueprint

**Status:** Draft 2 — 2026-07-07, after deadline re-scoping
**Deadline:** 2026-07-11 21:00 IST (4.5 days from this draft)
**Repository:** https://github.com/rajasingh012/ai-infinite-journey

---

## 1. Vision

Build a small open-world driving game where an LLM acts as the **World
Director**. The LLM never creates meshes or physics. It outputs a
high-level journey plan and a contract. A procedural layer assembles
reusable prefabs and runs the delivery loop on top.

**For the hackathon demo (4.5 days):** the road is a single straight
segment. The LLM customises the world's *look* (sky, sun, fog, weather
textures) and the *gameplay* (one contract per journey, with
pickup → dropoff → payout). After the hackathon, the road and building
generation become fully procedural (see §6 Stretch Goals).

## 2. Why a delivery game?

The blueprint originally said "open-world driving." A delivery game
gives that a sharp reason to exist: every journey has a start, a goal,
and a payoff. It also makes the LLM's role observable — the LLM
generates the *contract*, not just the scenery.

## 3. Why not GTA?

- Too large for a solo project.
- Delivery gives the open-world structure GTA does, with much smaller scope.

---

## 4. Hackathon MVP (ship by 2026-07-11 21:00 IST)

### 4.1 Scope decisions (all conservative for the 4.5-day window)

| Question                                  | Choice | Why                                          |
|-------------------------------------------|--------|----------------------------------------------|
| Cargo/contract system breadth             | Minimal (1 cargo type, 1 active contract) | Loops must work end-to-end; breadth later    |
| Road network shape                        | Linear (one straight segment)            | Already built; don't rebuild                 |
| Building generation variety               | Two anchor buildings (pickup, dropoff)   | Demo only; full villages in stretch          |
| Depot location                            | Pickup at start, dropoff at end          | Matches current road geometry                |
| Contract shape                            | Bare ({pickup, dropoff, payout})         | Minimal viable                               |
| Weather / time of day                     | Visual only                              | Re-balance later; visuals ship fast           |
| Save / progression                        | None (fresh run each launch)             | Persistence = risk; skip for demo            |
| Victory / end state                       | Endless (no quota)                       | Simpler; no summary screen needed            |

### 4.2 What the LLM outputs (the `JourneyPlan`)

Existing shape (keep it, just extend with one new field for the contract):

```json
{
  "theme": "Mountain",
  "description": "One short sentence about the journey.",
  "landmarks": ["Forest", "Lake", "Tunnel", "Village"],
  "hazards": [],
  "weather": "Clear",
  "time_of_day": "Midday",
  "recommended_chunks": 8,
  "contract": {
    "pickup_label": "Warehouse",
    "dropoff_label": "Mountain Lodge",
    "cargo": "Medical Supplies",
    "payout": 500,
    "narrative": "Storm warning — drive carefully."
  }
}
```

The `contract` block is what makes this a delivery game. Everything else
is visual.

### 4.3 Game-side loop

1. **Title screen** — "Press Enter to start a new journey."
2. **LLM call** — Game hits the backend (`POST /api/world/direct` with a
   default prompt like "an interesting drive"). Backend returns a
   `JourneyPlan`.
3. **World dressing** — Apply `weather` to fog density, `time_of_day`
   to sun angle and sky color, `theme` to a ground texture tint (single
   tint multiplier; no asset swap).
4. **HUD** — Show the contract: pickup label, dropoff label, cargo, payout.
5. **Drive** — Player drives to a yellow pickup marker at the start of
   the road, presses E to load cargo, drives to a red dropoff marker at
   the end, presses E to deliver.
6. **Result** — "Delivered for $500. Press Enter for a new journey."

### 4.4 What the LLM does NOT do (for this demo)

- No road generation. The road is the one straight segment we have.
- No building generation. Pickup and dropoff are simple coloured cubes
  placed at the road endpoints.
- No chunk streaming. The world is small enough to fit in one scene.
- No save/load. Each launch is fresh.

---

## 5. Stack (unchanged from the original blueprint)

- **Engine:** Godot 4.x with C# (`.NET 8`).
- **AI backend:** ASP.NET Core Web API.
- **LLM:** MiniMax Token Plan (today) → Gemma 4 31B (production).
  Provider-agnostic via `Microsoft.Extensions.AI` — swap is a one-line
  DI change.
- **Storage:** SQLite (used in stretch goals, not for the demo).
- **Source control:** Git.

---

## 6. 4.5-day sprint plan

| Day       | Hours | Deliverable                                                   |
|-----------|-------|---------------------------------------------------------------|
| Day 1 (Tue 7) | 4 | Update `JourneyPlan` schema with `contract` block.            |
|               |   | Backend: validate and return the new shape.                   |
|               |   | Godot: HTTP client script that calls the backend, parses JSON, |
|               |   | and exposes the result to the scene.                          |
| Day 2 (Wed 8) | 4 | HUD overlay: title screen, contract panel, delivery banner.   |
|               |   | World dressing: weather → fog, time_of_day → sun/sky, theme → |
|               |   | ground tint.                                                  |
| Day 3 (Thu 9) | 4 | Pickup/dropoff markers (coloured cubes) at road endpoints.    |
|               |   | Trigger volumes: press E to load cargo, press E to deliver.   |
|               |   | Payout + result screen.                                       |
| Day 4 (Fri 10)| 4 | End-to-end test from clean checkout. Fix whatever breaks.     |
|               |   | Record demo video (5-7 min).                                  |
| Day 5 (Sat 11)| 4 | Write README + submission form. Polish edges.                 |
|               |   | **Submit by 21:00 IST.**                                      |

(Only ~4 hours/day assumed; adjust as real time allows.)

---

## 7. Stretch goals (post-hackathon)

These are the things the original blueprint listed that we are NOT
shipping on July 11. They are the natural follow-ups for the next
iteration.

1. **Spline-based procedural road generator.** Input: `landmarks` from
   the `JourneyPlan`. Output: a connected network of spline curves with
   a road mesh (curbs, dashed line). Deterministic per (plan, seed).
2. **Procedural building placement.** Input: `landmarks` + the
   `theme`. Output: small villages at each landmark, depots at the
   pickup/dropoff, with spacing and road-edge constraints.
3. **Chunk streaming.** World divided into 100m chunks. Load/unload as
   the car crosses boundaries. Cache generated chunks in SQLite.
4. **Multiple cargo types and contract variety.** Phase 4 of the
   original blueprint.
5. **Weather and time-of-day as gameplay effects** (fog reduces
   visibility, night requires headlights, etc.).
6. **Reputation and progression.** Save contracts, money, unlocks.
7. **Gemma 4 31B as the production LLM.** Drop-in replacement of the
   MiniMax provider in `Program.cs` once Gemma is wired up.

---

## 8. Why this scope works for a hackathon

- **Demo length:** 60-90 seconds. Press Enter → see contract → drive →
  deliver → see payout. Loop a couple of times with different LLM
  prompts to show variety.
- **The LLM is visibly the director:** every demo run produces a
  different contract, different weather, different time of day, all
  from a single prompt to the same backend.
- **No broken promises:** we don't claim procedural roads exist if
  they don't. The stretch section is honest about what's still to do.
- **Provable end-to-end:** backend works, game calls it, output drives
  gameplay. No mock-ups.

---

## 9. Open questions for after the demo

These don't block shipping on July 11 but are worth deciding before
the next iteration:

- Should depots be landmarks the LLM picks, or always start/end?
- Branching roads vs linear (player choice) — branching is more
  interesting but needs a routing layer.
- Multi-leg contracts (pickup A, then B, then C) — increases delivery
  feel but triples the test surface.
- How visible should the LLM prompt be to the player? (Show the prompt
  on the title screen? Hide it?)

These map to the 8 design questions in the conversation; revisit them
once the demo is shipped.
