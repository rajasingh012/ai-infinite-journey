# Assets

Free CC0 assets from Kenney.nl (https://kenney.nl/assets).

All assets in this directory are CC0 (public domain) — no attribution
required, but credited here as a courtesy.

## Packs used

- **Car Kit** (3D) — `Models/Cars/sedan.glb` + `Textures/car-colormap.png`
  Source: https://kenney.nl/assets/car-kit
- **Modular Buildings** (3D) — `Models/Buildings/house-a.glb` + `Textures/building-colormap.png`
  Source: https://kenney.nl/assets/modular-buildings
- **Nature Kit** (3D) — `Models/Trees/tree_default.fbx`, `tree_fat.fbx`, `tree_cone.fbx`
  Source: https://kenney.nl/assets/nature-kit
  (FBX chosen because the kit doesn't ship a GLB variant; no external textures required.)

## Source zips (for re-import / re-licensing)

The original Kenney zips are kept under `../../Assets/Source/` for reference
and are NOT tracked in git. They are public-domain downloads.

## File layout

    Assets/
      Models/
        Cars/        GLB vehicles
        Trees/       FBX foliage
        Buildings/   GLB building blocks
      Textures/      PNG colormaps shipped with the GLB packs
      Source/        Original Kenney zips (untracked, kept for reference)

## Phase 2 status

- Player car: replaced by Kenney sedan GLB with colormap
- Tree scatter: 60 random placements along road sides (3 variants)
- Building prefabs: imported but not yet scattered (Phase 2 stretch)
