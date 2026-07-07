# Progress

Tracked tasks, decisions, and known issues for the AI Infinite Journey hackathon.

**Current date:** 2026-07-07
**Deadline:** 2026-07-11 21:00 IST

---

## Completed (80%)

### Backend
- [x] Create ASP.NET API with `POST /api/world/direct` + `GET /api/world/health`
- [x] `LlmWorldDirector.cs` — LLM call with 180s timeout
- [x] `SyntheticFallback.cs` — fallback plan when LLM times out
- [x] `WorldController.cs` — catch LLM errors → return SyntheticFallback
- [x] `MockChatClient.cs` — deterministic plan for offline dev
- [x] `DeliveryContract` record in `JourneyPlan.cs`
- [x] Tests: live MiniMax roundtrip, payout-scales-with-danger verified

### Godot — Core
- [x] Car movement: EngineForce=120000 N (empirically required — Godot 4 solver
      swallows small per-tick forces on resting bodies)
- [x] Car physics: ContinuousCd, Sleep disabled, ContactMonitor, Freeze=false,
      CallDeferred collision shape, lowered COM
- [x] Car downforce: base (Mass×5) + speed-scaled (Downforce×fwdSpeed)
- [x] ChaseCamera: offset (0, 2.5, 6), follows RigidBody3D
- [x] `JourneyApi.cs` — async HTTP client, parses JourneyPlan
- [x] `TitleScreen.cs` — Enter to start journey, stores plan in JourneyState
- [x] `JourneyState.cs` — autoload singleton, PlanChanged signal, cargo state

### Godot — Terrain & Road
- [x] `RoadChunk.cs` — 50×50m heightmap terrain (SurfaceTool mesh +
      HeightMapShape3D collision, sine-wave hills, 33×33 samples)
- [x] `TerrainManager.cs` — spawn/despawn terrain tiles in grid (4 ahead,
      2 behind, 1 to each side)
- [x] `RoadStrip.cs` — 50m tileable road segment with dashes, SetColor
- [x] `RoadManager.cs` — spawn/despawn road strips (6 ahead, 4 behind),
      theme coloring from JourneyState.PlanChanged
- [x] `TreeScatter.cs` — 60 trees along 500m of road (temporary, visual only)

### Project & Docs
- [x] Git: all commits pushed (7 commits so far)
- [x] README.md — project overview (pitch, stack, architecture)
- [x] BLUEPRINT.md — design doc with scope decisions
- [x] PROGRESS.md — this file

---

## Remaining (20%)

### Day 2 — Wed Jul 8
- **Spline road** — Catmull-Rom control points → curved road mesh through terrain
- **Bridge/tunnel detection** — terrain above road = tunnel shading,
  terrain below = exposed bridge girders
- **Object collision** — give existing trees physics shapes so car crashes into them

### Day 3 — Thu Jul 9
- **Box buildings** — houses (box+triangle roof), factories (tall boxes+chimney),
  placed along road with collision
- **Option B ground** — terrain collision below road level; drive off road → drop
- **Endless object spawning** — objects spawn/despawn with terrain chunks

### Day 4 — Fri Jul 10
- **End-to-end test** from clean checkout
- **Fix whatever breaks**
- **Record demo video** (3-5 min)

### Day 5 — Sat Jul 11
- **README polish**
- **Submission form**
- **Ship by 21:00 IST**

---

## Known bugs

1. **Car launches off terrain** — 120000 N engine force is ~10× stronger than
   downforce on steep inclines. Partial fix: base downforce (Mass×5). Real fix
   needs wheel colliders (post-hackathon).
2. **Flat road strips on heightmap terrain** — road strips are flat at Y=0 while
   terrain has hills above/below. Spline road (Day 2) fixes this.
3. **Trees have no collision** — visual only. You drive through them. Day 2 fix.
4. **No buildings** — no houses, factories etc. Day 3.

## Technical decisions

- **Car physics**: RigidBody3D with box collision. EngineForce=120000 N empirically
  required (1200 N and 3000 N both failed — Godot 4 solver swallows small per-tick
  forces). Real fix needs wheel colliders.
- **Heightmap**: 33×33 samples per 50×50 tile. Layered sine waves seeded by world
  position — deterministic, seamless between tiles. Triangle strip mesh via SurfaceTool.
- **Downforce**: Base component (Mass×5) keeps car planted at low speed. Speed-scaled
  (Downforce×fwdSpeed) adds grip at velocity. Without base component the car floats
  because 120000 N engine force > gravity.
- **Model switching**: This session has been on MiniMax-M3 → GLM-5.1 → GLM-5.2 →
  DeepSeek V4 Flash. Provider chain configurable in Hermes (`ai-game` profile).

## Git log

```
920ff03  docs: create PROGRESS.md, slim README
e5ec94d  feat: terrain heightmap + gravity fix
ad41619  feat: endless road chunk system + revised terrain roadmap
9321cda  docs: revise scope — endless world, drop delivery mechanics
78c68b3  fix: car movement, LLM timeout, and SyntheticFallback
a615d0b  feat: Godot HTTP client + title screen + journey state
db2eb92  feat: backend contract generation (DeliveryContract, payout scaling)
f45594e  docs: initial BLUEPRINT.md
```
