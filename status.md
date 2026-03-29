# GeoTime Implementation Status

## Phase 1 — Foundation ✅

**Goal**: Shared architecture, planet generation, static globe rendering.

### AGENT-KERNEL
- [x] Typed event bus with pub/sub and topic filtering (`src/kernel/event-bus.ts`)
- [x] SharedArrayBuffer allocation and layout for all state maps (`src/shared/types.ts`)
- [x] SimClock implementation with variable rate (`src/kernel/sim-clock.ts`)
- [x] Geological event log with timestamps (`src/kernel/event-log.ts`)
- [x] Keyframe snapshot manager for time scrubbing (`src/kernel/snapshot-manager.ts`)
- [ ] Web Worker pool with message routing (deferred — not needed until multi-agent ticking)
- [ ] Binary snapshot format (deferred — Flatbuffers + Brotli)

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

## Phase 2 — Plate Tectonics & Volcanism ✅

**Goal**: The planet now evolves. Mountains rise, ocean crust subducts, volcanoes grow.

### AGENT-GEO: Tectonics
- [x] Euler rotation vectors per plate, computed from plate angular velocity (`src/geo/boundary-classifier.ts`)
- [x] Boundary classification at every cell edge: convergent, divergent, transform (`src/geo/boundary-classifier.ts`)
- [x] Subduction zone modeling: oceanic plate sinks under continental, forming trench + volcanic arc (`src/geo/tectonic-engine.ts`)
- [x] Continental collision: crustal thickening, fold-and-thrust belt formation (`src/geo/tectonic-engine.ts`)
- [x] Rift valley opening: graben formation, eventual seafloor spreading (`src/geo/tectonic-engine.ts`)
- [x] Isostatic adjustment: crustal thickness drives surface elevation (Airy model) (`src/geo/tectonic-engine.ts`)
- [x] Hotspot tracks: fixed mantle plumes pierce moving plates, creating island chains (`src/geo/volcanism.ts`)

### AGENT-GEO: Volcanism
- [x] Stratovolcano: subduction-arc setting, explosive, steep flanks (`src/geo/volcanism.ts`)
- [x] Shield volcano growth: low silica, effusive, broad profile, hotspot-linked (`src/geo/volcanism.ts`)
- [x] Submarine volcanism at mid-ocean ridges: pillow basalt (`src/geo/volcanism.ts`)
- [x] Each eruption records a new StratigraphicLayer (basalt, andesite, dacite, etc.) (`src/geo/volcanism.ts`)
- [x] Volcanic degassing: CO₂ and SO₂ injected into atmosphere budget (`src/geo/volcanism.ts`)

### AGENT-GEO: Stratigraphy Initialization
- [x] Each plate's initial crust assigned a base stack of Precambrian basement layers (`src/geo/stratigraphy.ts`)
- [x] New layers appended as volcanic events occur (`src/geo/stratigraphy.ts`)
- [x] Dip angle updated when compression or extension deforms a cell (`src/geo/stratigraphy.ts`)
- [x] Erosion support: material removed from the top of the stack (`src/geo/stratigraphy.ts`)
- [x] Layer budget enforcement: oldest layers merged when exceeding max (`src/geo/stratigraphy.ts`)

### AGENT-KERNEL: Temporal System
- [x] Keyframe snapshots at configurable intervals (default every 10 Myr sim-time) (`src/kernel/snapshot-manager.ts`)
- [x] Forward scrubbing: advance clock (`src/kernel/snapshot-manager.ts`)
- [x] Backward scrubbing: load nearest earlier snapshot (`src/kernel/snapshot-manager.ts`)
- [x] Geological event log: eruptions, collisions, rifts recorded with timestamps (`src/kernel/event-log.ts`)

### Integration
- [x] Tectonic engine wired into the main render loop (`src/main.ts`)
- [x] Tectonic simulation runs at throttled rate (100ms intervals)
- [x] Height map GPU texture updates after tectonic changes
- [x] Periodic snapshots taken during simulation

### Testing
- [x] 102 unit tests passing across 13 test files (Vitest)
  - `tests/stratigraphy.test.ts` — 15 tests
  - `tests/event-log.test.ts` — 8 tests
  - `tests/snapshot-manager.test.ts` — 11 tests
  - `tests/boundary-classifier.test.ts` — 12 tests
  - `tests/volcanism.test.ts` — 7 tests
  - `tests/tectonic-engine.test.ts` — 8 tests
  - Plus 41 Phase 1 tests
- [x] 12 Playwright integration tests (`e2e/app-shell.spec.ts`)
  - App boot and WebGL canvas rendering
  - HUD bar display (FPS, Tris, Time)
  - UI controls (New Planet, Pause, Rate slider)
  - Multi-planet generation stability
  - Console error verification

---

## Phase 3 — Erosion, Rivers & Surface Processes ✅

**Goal**: Tectonics builds terrain; erosion carves it. Rivers, glaciers, and weathering give the surface its character.

### AGENT-GEO: Fluvial Erosion
- [x] D∞ flow routing for drainage network computation (`src/geo/erosion-engine.ts`)
- [x] Stream power law for river incision: E = K · A^m · S^n (`src/geo/erosion-engine.ts`)
- [x] Sediment transport: eroded material tracked downstream (`src/geo/erosion-engine.ts`)
- [x] Alluvial fan and delta deposition at mountain fronts and river mouths (`src/geo/erosion-engine.ts`)
- [x] River cell identification via drainage area threshold (`src/geo/erosion-engine.ts`)
- [x] Sedimentary layer recording (sandstone, mudstone) in stratigraphy stacks (`src/geo/erosion-engine.ts`)

### AGENT-GEO: Glacial Erosion
- [x] Equilibrium line altitude (ELA) computation from polar temperature field (`src/geo/glacial-engine.ts`)
- [x] Ice accumulation above ELA in cold regions (`src/geo/glacial-engine.ts`)
- [x] Ice ablation when temperatures rise above threshold (`src/geo/glacial-engine.ts`)
- [x] Glacial erosion: quarrying + abrasion proportional to ice thickness and slope (`src/geo/glacial-engine.ts`)
- [x] Moraine deposition (tillite) at glacier margins with glacial unconformity (`src/geo/glacial-engine.ts`)
- [x] Per-cell ice thickness tracking with clear/reset support (`src/geo/glacial-engine.ts`)

### AGENT-GEO: Aeolian & Chemical Weathering
- [x] Chemical weathering rate: Arrhenius-inspired function of temperature and moisture (`src/geo/weathering-engine.ts`)
- [x] Karst formation: carbonate rock dissolution (limestone, dolostone, chalk) → regolith (`src/geo/weathering-engine.ts`)
- [x] Laterite formation: deep chemical weathering in tropical humid zones (`src/geo/weathering-engine.ts`)
- [x] Caliche formation in arid zones (`src/geo/weathering-engine.ts`)
- [x] Aeolian erosion in arid windy areas (wind speed threshold) (`src/geo/weathering-engine.ts`)
- [x] Loess deposition downwind of aeolian erosion (`src/geo/weathering-engine.ts`)
- [x] Weathered products appended as new StratigraphicLayer (regolith, laterite, caliche, loess) (`src/geo/weathering-engine.ts`)

### AGENT-GEO: Pedogenesis (Soil Formation)
- [x] Soil formation activated where surface rock exposed, temp > -20°C, some moisture (`src/geo/pedogenesis.ts`)
- [x] Formation rate depends on CLORPT: climate, parent rock type, time (`src/geo/pedogenesis.ts`)
- [x] All 12 USDA soil orders classified with specific formation criteria (`src/geo/pedogenesis.ts`)
- [x] soilTypeMap and soilDepthMap updated each pedogenesis tick (`src/geo/pedogenesis.ts`)
- [x] Soil order written into the topmost StratigraphicLayer for cross-section labeling (`src/geo/pedogenesis.ts`)
- [x] Maximum soil depth enforcement (5 m cap) (`src/geo/pedogenesis.ts`)

### Surface Engine Orchestrator
- [x] Combined surface process engine: erosion → glacial → weathering → pedogenesis (`src/geo/surface-engine.ts`)
- [x] Sub-tick batching with configurable minimum interval (default 0.5 Ma) (`src/geo/surface-engine.ts`)
- [x] Event emission: EROSION_CYCLE, GLACIATION_ADVANCE, GLACIATION_RETREAT, MAJOR_RIVER_FORMED (`src/geo/surface-engine.ts`)
- [x] Glaciation advance/retreat detection and event logging (`src/geo/surface-engine.ts`)

### Integration
- [x] Surface engine wired into the main render loop after tectonic engine (`src/main.ts`)
- [x] New event types added to shared types (`src/shared/types.ts`)
- [x] Height map GPU texture updates include surface process changes

### Testing
- [x] 75 unit tests passing across 5 new test files (Vitest)
  - `tests/erosion-engine.test.ts` — 11 tests (flow graph, drainage area, erosion, deposition, determinism)
  - `tests/glacial-engine.test.ts` — 11 tests (ELA, glaciation, ablation, moraine, tillite)
  - `tests/weathering-engine.test.ts` — 16 tests (chemical, aeolian, loess, laterite, karst)
  - `tests/pedogenesis.test.ts` — 25 tests (12 soil orders, formation rate, tick, depth cap)
  - `tests/surface-engine.test.ts` — 12 tests (orchestration, events, determinism)
- [x] 4 new Playwright integration tests (`e2e/app-shell.spec.ts`)
  - Surface engine active after unpausing
  - Planet generation with surface engine initialized
  - No console errors during surface simulation
  - Simulation time display while surface processes run

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
│   ├── sim-clock.ts   — Simulation clock with pause/resume/seekTo
│   ├── event-log.ts   — Geological event log with timestamps
│   └── snapshot-manager.ts — Keyframe snapshots for time scrubbing
├── proc/
│   ├── prng.ts        — Xoshiro256** seeded PRNG
│   ├── simplex-noise.ts — 3D simplex noise + FBM
│   └── planet-generator.ts — Voronoi plates + heightfield + sea level
├── geo/
│   ├── tectonic-engine.ts — Main tectonic simulation orchestrator
│   ├── boundary-classifier.ts — Plate boundary detection and classification
│   ├── volcanism.ts   — Volcanic eruption system
│   ├── stratigraphy.ts — Per-cell stratigraphic layer stacks
│   ├── erosion-engine.ts — Fluvial erosion (D∞ flow routing, stream power law)
│   ├── glacial-engine.ts — Glacial erosion (ELA, ice, moraine deposition)
│   ├── weathering-engine.ts — Aeolian & chemical weathering
│   ├── pedogenesis.ts — Soil formation (CLORPT, USDA soil orders)
│   └── surface-engine.ts — Surface process orchestrator
├── render/
│   ├── icosphere.ts   — Icosphere mesh generation
│   └── globe-renderer.ts — Three.js renderer with height displacement
├── ui/
│   └── app-shell.ts   — DOM-based UI shell
└── main.ts            — Application entry point, wires everything together
tests/                 — 18 Vitest test files (177 tests)
e2e/                   — Playwright integration tests (16 tests)
```

### Key Design Decisions
- **Grid Size**: 512×512 (configurable via `GRID_SIZE` in `shared/types.ts`)
- **Buffer Layout**: ~10.7 MB SharedArrayBuffer with 13 typed array views
- **Event Bus**: Synchronous pub/sub; agents subscribe to typed events
- **SimClock**: Starts at -4500 Ma (4.5 Ga); rate in Ma/second; paused by default
- **PRNG**: Fully deterministic; same seed → same planet
- **Renderer**: Three.js WebGL with custom ShaderMaterial for height displacement
- **No SharedArrayBuffer in tests**: Tests use `ArrayBuffer` cast as SAB fallback
- **Tectonic Engine**: Sub-tick batching with configurable minimum interval
- **Boundary Classification**: 4-connected neighbors, velocity-based convergent/divergent/transform
- **Isostasy**: Airy model with exponential relaxation
- **Stratigraphy**: Max 64 layers per cell with oldest-merge budget enforcement
- **Snapshots**: Configurable interval (default 10 Ma), max 500 snapshots
- **Erosion**: D∞ steepest-descent flow routing, stream power law E = K·A^m·S^n
- **Glaciation**: ELA-based ice accumulation/ablation, moraine deposition as tillite
- **Weathering**: Arrhenius chemical rates, aeolian threshold model, loess deposition
- **Pedogenesis**: Simplified CLORPT model, all 12 USDA soil orders, max 5 m depth
- **Surface Engine**: Orchestrates erosion → glacial → weathering → pedogenesis each tick

### Phase 4 Prerequisites
- The surface process engines are fully functional and tested
- Erosion, glacial, weathering, and pedogenesis systems all update the shared height map and stratigraphy
- Temperature, precipitation, and wind maps in SharedArrayBuffer are ready for atmospheric simulation
- The event system supports EROSION_CYCLE, GLACIATION_ADVANCE/RETREAT events
- Glacial engine tracks ice extent and ELA for climate feedback integration
