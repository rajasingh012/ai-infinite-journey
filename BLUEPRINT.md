# AI Infinite Journey — Hackathon MVP Blueprint (Rev 3)

**"AI builds this 3D world while you play."**

**Deadline:** 2026-07-11 21:00 IST
**Repository:** https://github.com/rajasingh012/ai-infinite-journey

---

## 1. Vision

Press Enter. An LLM generates a world. Terrain rises into hills, a winding road snakes through with spline curves, bridges span valleys, tunnels cut through ridges. Trees and buildings line the route. You drive through it once — unique every run.

## 2. Scope decisions (hackathon)

| Question | Choice | Why |
|----------|--------|-----|
| Terrain | **Heightmap-based** (noise → hills/valleys, chunks) | 3D feel without external mesh assets |
| Road shape | **Spline** (control points → curved road mesh) | Winding drive ≠ straight line |
| Bridges | **Procedural** (terrain drops → road exposed) | Terrain below road level = bridge feel |
| Tunnels | **Procedural** (terrain rises above road) | Terrain above road level = tunnel feel |
| Buildings | **Box primitives** with collision (houses, factories) | Physical obstacles you crash into |
| Trees | **Existing FBX** + added collision | Physical obstacles |
| HUD / UI | **Removed** | No time for this sprint |
| Cargo / payout | **Removed** | Not needed for the pitch |
| Save / progression | **None** | Fresh run each launch |

## 3. What the LLM outputs (`JourneyPlan`)

```json
{
  "theme": "Mountain",
  "description": "A winding road through a mountain pass.",
  "landmarks": ["Forest", "Lake", "Tunnel", "Village"],
  "hazards": ["Rockfalls"],
  "weather": "Stormy",
  "time_of_day": "Night",
  "recommended_chunks": 0,
  "contract": null
}
```

`theme` influences terrain params (mountain = steep, countryside = gentle). `weather` and `time_of_day` drive visual dressing (post-hackathon).

## 4. Technical design

### 4.1 Terrain generation

**TerrainChunk.cs** — a 50×50m terrain tile with:
- Heightmap: 2D float array sampled from layered sine waves + noise
- Mesh: `ArrayMesh` built from heightmap (triangle strip)
- Collision: `HeightMapShape3D` or triangle collision shape

**TerrainManager.cs** — spawns/despawns chunks in a sliding window around the car, same pattern as the existing RoadManager.

Height function per chunk:
```
h(x, z) = A0 * sin(f0x * x + f0z * z + phase0)
        + A1 * sin(f1x * x + f1z * z + phase1)
        + A2 * gaussian(x, z, cx, cz, sigma)
```

Amplitudes/seeds derived from the journey theme (mountain = high A, gentle = low A).

### 4.2 Spline road

**SplineRoad.cs** — defines a 3D curve through terrain:
- Control points: `Vector3[]` with X and Z positions, Y derived from terrain height + offset
- Road follows the spline as a ribbon mesh

**RoadSegment.cs** — a road strip that follows a subsection of the spline:
- Road mesh: flat ribbon (triangle strip following the spline)
- Road collision: `ConcavePolygonShape3D` matching the mesh
- Dashed center line follows the curve

**SplineManager.cs** — spawns/despawns RoadSegments, aligns them to the terrain surface.

Bridge detection: where terrain height < (spline Y - 0.5m) → road is visible as a bridge.  
Tunnel detection: where terrain height > (spline Y + car_height) → road is inside a tunnel (hide sky, darken).

### 4.3 Objects

**ObjectPlacer.cs** — places trees, houses, factories along the road:
- Trees from existing FBX (already have tree_fat, tree_cone, tree_default) with collision
- Houses: box + triangular roof (procedural mesh)
- Factories: taller boxes with chimneys
- Keep-out zone around road (6m from centerline)
- Collision shapes on all objects so the car crashes into them

### 4.4 Ground collision (Option B)

The ground collision plane is at Y=-0.3 (below the road surface at Y=0). The road collision is at Y=0. Driving off the road drops the car 0.3 units onto the terrain. The car can climb back onto the road if the terrain is close enough to road height. In deep valleys, the car falls further and must stay on the road.

## 5. Sprint plan (revised)

| Day | Deliverable |
|-----|-------------|
| Day 1 (Tue 7) | Backend live, HTTP client, car moves — **DONE** |
| Day 2 (Wed 8) | Terrain heightmap chunks + spline road system |
| Day 3 (Thu 9) | Object placement (trees, houses, factories) with collision, ground vs road collision separation |
| Day 4 (Fri 10) | Polish, end-to-end from clean checkout, demo video |
| Day 5 (Sat 11) | README, submission, **ship by 21:00 IST** |

## 6. Post-hackathon

- Flavor HUD showing LLM description
- Re-add delivery gameplay
- Multi-cargo, progression, save
- Gemma 4 31B as production LLM
