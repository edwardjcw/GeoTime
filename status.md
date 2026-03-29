# GeoTime Implementation Status

## Phase 1 — Foundation ✅

**Goal**: Shared architecture, planet generation, static globe rendering.

### AGENT-KERNEL
- [x] Typed event bus with pub/sub and topic filtering (`src/kernel/event-bus.ts`)
- [x] SharedArrayBuffer allocation and layout for all state maps (`src/shared/types.ts`)
- [x] SimClock implementation with variable rate (`src/kernel/sim-clock.ts`)
- [ ] Web Worker pool with message routing (deferred to Phase 2 — not needed until multi-agent ticking)
- [ ] Binary snapshot format (deferred to Phase 2 — Flatbuffers + Brotli)

### AGENT-PROC
- [x] Seeded PRNG — xoshiro256** (`src/proc/prng.ts`)
- [x] Voronoi plate generation with Lloyd relaxation — 10-16 plates, 3 iterations (`src/proc/planet-generator.ts`)
- [x] Simplex noise height field on icosphere — 4 octaves FBM (`src/proc/simplex-noise.ts`)
- [x] Ocean fraction targeting — binary search sea level for ~70% ocean (`src/proc/planet-generator.ts`)
- [x] Mantle plume hotspot seeding — 2-5 hotspots (`src/proc/planet-generator.ts`)
- [x] Baseline atmospheric composition assignment (`src/proc/planet-generator.ts`)

### AGENT-RENDER
- [x] Icosphere mesh generation with subdivision LOD (`src/render/icosphere.ts`)
- [x] Height-map displacement in vertex shader (`src/render/globe-renderer.ts`)
- [x] Basic PBR material pipeline — height-based coloring (ocean/land/mountain/snow) (`src/render/globe-renderer.ts`)
- [x] Free-orbit arcball camera with inertia — Three.js OrbitControls (`src/render/globe-renderer.ts`)
- [x] Placeholder sun directional light (`src/render/globe-renderer.ts`)

### AGENT-UI
- [x] App shell: globe viewport, collapsible sidebar, top HUD (`src/ui/app-shell.ts`)
- [x] New Planet button (triggers PROC) (`src/ui/app-shell.ts`)
- [x] Seed display and copy control (`src/ui/app-shell.ts`)
- [x] FPS and triangle count performance overlay (`src/ui/app-shell.ts`)

### Infrastructure
- [x] Project scaffolding: Vite + TypeScript (strict) + Vitest
- [x] Shared type definitions — single source of truth (`src/shared/types.ts`)
- [x] Unit tests — 41 tests passing across 7 test files

---

## Phase 2 — Plate Tectonics & Volcanism ⬜
Not started.

## Phase 3 — Erosion, Rivers & Surface Processes ⬜
Not started.

## Phase 4 — Atmosphere, Climate & Weather ⬜
Not started.

## Phase 5 — Cross-Section Viewer ⬜
Not started.

## Phase 6 — Vegetation & Polish ⬜
Not started.

---

## Architecture Notes for Future Agents

### Project Structure
```
src/
├── shared/types.ts    — All enums, interfaces, buffer layout (import first)
├── kernel/
│   ├── event-bus.ts   — Typed pub/sub event bus
│   └── sim-clock.ts   — Simulation clock with pause/resume/seekTo
├── proc/
│   ├── prng.ts        — Xoshiro256** seeded PRNG
│   ├── simplex-noise.ts — 3D simplex noise + FBM
│   └── planet-generator.ts — Voronoi plates + heightfield + sea level
├── render/
│   ├── icosphere.ts   — Icosphere mesh generation
│   └── globe-renderer.ts — Three.js renderer with height displacement
├── ui/
│   └── app-shell.ts   — DOM-based UI shell
└── main.ts            — Application entry point, wires everything together
tests/
├── event-bus.test.ts
├── sim-clock.test.ts
├── prng.test.ts
├── simplex-noise.test.ts
├── planet-generator.test.ts
├── icosphere.test.ts
└── shared-types.test.ts
```

### Key Design Decisions
- **Grid Size**: 512×512 for Phase 1 (configurable via `GRID_SIZE` in `shared/types.ts`)
- **Buffer Layout**: ~10.7 MB SharedArrayBuffer with 13 typed array views
- **Event Bus**: Synchronous pub/sub; agents subscribe to typed events
- **SimClock**: Starts at -4500 Ma (4.5 Ga); rate in Ma/second; paused by default
- **PRNG**: Fully deterministic; same seed → same planet
- **Renderer**: Three.js WebGL with custom ShaderMaterial for height displacement
- **No SharedArrayBuffer in tests**: Tests use `ArrayBuffer` cast as SAB fallback

### Phase 2 Prerequisites
- The stratigraphy stack data structure is defined in `shared/types.ts` (`StratigraphicLayer`)
- The event bus supports all events needed for tectonic notifications
- The plate map and height map are populated and ready for tectonic evolution
- Web Worker pool should be implemented for parallel agent ticking
