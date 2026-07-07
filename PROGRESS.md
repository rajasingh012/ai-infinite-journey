# Progress

Tracked tasks, decisions, and known issues for the AI Infinite Journey hackathon.

---

## Done

### Day 1 — Tue Jul 7

- [x] BLUEPRINT.md written with scope decisions
- [x] Backend API live (`POST /api/world/direct`, `GET /api/world/health`)
- [x] Godot HTTP client (`JourneyApi.cs`) calls backend, parses JSON
- [x] Title screen → Enter → fetch journey plan → store in `JourneyState`
- [x] `SyntheticFallback.cs` — fallback plan when LLM times out
- [x] 180s timeout on MiniMax LLM calls
- [x] Car physics fixed: EngineForce=120000 N, ContinuousCd, Sleep disabled,
      ContactMonitor enabled, CallDeferred collision shape

### Day 2 — Wed Jul 8

- [x] `RoadStrip.cs` — 50m tileable road segment with dashes, SetColor support
- [x] `RoadManager.cs` — spawn/despawn road strips (6 ahead, 4 behind), theme coloring
- [x] `RoadChunk.cs` — replaced flat ground box with 50×50 heightmap terrain
      (SurfaceTool mesh + HeightMapShape3D collision, sine-wave hills)
- [x] `TerrainManager.cs` — spawn/despawn terrain tiles in grid (4 ahead, 2 behind, 1 side)
- [x] Car downforce fixed: base downforce (Mass×5) + speed-scaled (Downforce×fwdSpeed)
- [x] Main.tscn rewired: World→TerrainManager, RoadManager wired to Car
- [x] Commit/push: all 5 files (e5ec94d)
- [x] README + BLUEPRINT revised with terrain/spline roadmap

## To do

### Day 3 — Thu Jul 9

- [ ] ObjectPlacer — trees with collision, box houses, factories along road
- [ ] Option B ground collision — terrain below road level, drive off → fall
- [ ] Spline road — curved road path through terrain (Catmull-Rom control points)
- [ ] Bridge/tunnel feel — terrain above road = tunnel, terrain below = bridge

### Day 4 — Fri Jul 10

- [ ] End-to-end test from clean checkout
- [ ] Fix whatever breaks
- [ ] Record demo video (5-7 min)

### Day 5 — Sat Jul 11

- [ ] README polish
- [ ] Submission form
- [ ] **Ship by 21:00 IST**

## Known bugs

1. **Car launches off terrain** — 120000 N engine force is ~10× stronger than downforce.
   Car can launch off hilltops. Partial fix: base downforce (Mass×5) added. Still happens
   on steep inclines. Wheel colliders (post-hackathon) are the real fix.
2. **Flat road strips on heightmap terrain** — road strips are flat at Y=0 while terrain
   has hills above/below. Road needs to follow terrain surface (spline road, Day 3).
3. **Tree collision missing** — trees are visual only (no physics shape). You drive
   through them. Fix in Day 3 (ObjectPlacer).
4. **No building collision** — same as trees. Box buildings will fix this.

## Technical notes

- **Car physics**: RigidBody3D with box collision. EngineForce=120000 N was empirically
  required (1200 N and 3000 N both failed — Godot 4 solver swallows small per-tick
  forces on resting bodies). Real fix needs wheel colliders.
- **Heightmap**: 33×33 samples per 50×50 tile. Layered sine waves seeded by world
  position so terrain is deterministic and seamless between tiles.
- **Model switching**: This conversation has been on MiniMax-M3 → GLM-5.1 → GLM-5.2
  → DeepSeek V4 Flash. Provider chain is configurable in Hermes (`ai-game` profile).
- **Git commits**: f45594e (blueprint), db2eb92 (backend contract), a615d0b (HTTP
  client), 78c68b3 (car fix), 9321cda (docs re-scope), ad41619 (road chunks),
  e5ec94d (terrain heightmap + downforce).
