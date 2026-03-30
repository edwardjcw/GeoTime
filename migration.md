# GeoTime Migration Plan: TypeScript → C# .NET Backend

This document tracks the migration of GeoTime's simulation logic from the browser-based TypeScript frontend to a C# .NET backend. The frontend is refactored to be display-only, fetching computed state from the backend via REST API.

## Migration Status

### ✅ Phase 1: Backend Project Setup
- [x] Created .NET solution with three projects:
  - `GeoTime.Core` — class library with all simulation logic
  - `GeoTime.Api` — ASP.NET Core Web API exposing REST endpoints
  - `GeoTime.Tests` — xUnit test project (75 tests)

### ✅ Phase 2: Core Models & Types
- [x] Ported all enums: `RockType`, `SoilOrder`, `CloudGenus`, `DeformationType`, `BoundaryType`, `VolcanoType`
- [x] Ported all data models: `SimulationState`, `StratigraphicLayer`, `PlateInfo`, `HotspotInfo`, `AtmosphericComposition`, `BoundaryCell`, `EruptionRecord`, `CrossSectionProfile`, `GeoLogEntry`, etc.
- [x] Grid constants (`GRID_SIZE = 512`, cell count = 262,144)

### ✅ Phase 3: Procedural Generation
- [x] `Xoshiro256ss` — Seeded PRNG with SplitMix64 expansion
- [x] `SimplexNoise` — 3D simplex noise with FBM support
- [x] `PlanetGenerator` — Voronoi plates, heightfield, sea-level normalization, crust classification, hotspots

### ✅ Phase 4: Kernel Services
- [x] `EventBus` — Pub/sub event system
- [x] `EventLog` — Geological event recording with time-range queries
- [x] `SimClock` — Simulation clock with adaptive tick rate
- [x] `SnapshotManager` — Keyframe snapshots for time-scrubbing

### ✅ Phase 5: Geological Engines
- [x] `StratigraphyStack` — Per-cell 64-layer geological column
- [x] `BoundaryClassifier` — Convergent/divergent/transform plate boundary detection
- [x] `VolcanismEngine` — Subduction arc, mid-ocean ridge, hotspot volcanism
- [x] `TectonicEngine` — Plate motion, boundary processes, isostasy orchestration
- [x] `ErosionEngine` — D∞ flow routing, stream power law, sediment transport
- [x] `GlacialEngine` — Ice accumulation/ablation, glacial erosion, moraines
- [x] `WeatheringEngine` — Chemical weathering, aeolian erosion, loess deposition
- [x] `PedogenesisEngine` — USDA soil taxonomy (all 12 soil orders)
- [x] `SurfaceEngine` — Erosion→Glacial→Weathering→Pedogenesis pipeline orchestrator

### ✅ Phase 6: Atmosphere & Vegetation
- [x] `ClimateEngine` — Solar insolation, 3-cell circulation, Milankovitch, greenhouse, ice-albedo
- [x] `WeatherEngine` — Frontal systems, tropical cyclones, orographic precipitation, clouds
- [x] `AtmosphereEngine` — Climate→Weather pipeline orchestrator
- [x] `VegetationEngine` — Miami Model NPP, biomass accumulation, forest fires, albedo feedback

### ✅ Phase 7: Cross-Section & Orchestration
- [x] `CrossSectionEngine` — Great-circle interpolation, stratigraphy sampling, deep earth zones
- [x] `SimulationOrchestrator` — Top-level orchestrator owning all engines and state

### ✅ Phase 8: REST API Endpoints
- [x] `POST /api/planet/generate` — Generate new planet with seed
- [x] `POST /api/simulation/advance` — Advance simulation by delta Ma
- [x] `GET /api/simulation/time` — Current simulation time and seed
- [x] `GET /api/state/heightmap` — Height map array
- [x] `GET /api/state/platemap` — Plate assignment map
- [x] `GET /api/state/temperaturemap` — Temperature map
- [x] `GET /api/state/precipitationmap` — Precipitation map
- [x] `GET /api/state/biomassmap` — Biomass map
- [x] `GET /api/state/plates` — Plate info
- [x] `GET /api/state/hotspots` — Hotspot info
- [x] `GET /api/state/atmosphere` — Atmospheric composition
- [x] `GET /api/state/events` — Geological event log
- [x] `GET /api/state/inspect/{cellIndex}` — Cell inspection
- [x] `POST /api/crosssection` — Cross-section profile

### ✅ Phase 9: Frontend Refactoring
- [x] Created `src/api/backend-client.ts` — API client module
- [x] Refactored `src/main.ts` to delegate all simulation to backend
- [x] Frontend retains: Three.js globe rendering, cross-section Canvas 2D rendering, UI shell
- [x] Frontend removed: Direct simulation engine imports, SharedArrayBuffer allocation, local simulation loop

### ✅ Phase 10: Unit Tests (75 tests)
- [x] PRNG tests (5 tests) — determinism, range, seeding
- [x] Simplex noise tests (4 tests) — bounds, determinism, FBM
- [x] Planet generator tests (5 tests) — valid state, plate properties, determinism
- [x] Kernel tests (11 tests) — EventBus, EventLog, SimClock, SnapshotManager
- [x] Stratigraphy tests (6 tests) — push, merge, erode, basement, deformation
- [x] Boundary classifier tests (4 tests) — boundaries, neighbors, velocity
- [x] Pedogenesis tests (9 tests) — all soil order classifications, formation rate
- [x] Weathering tests (5 tests) — products, rates, temperature thresholds
- [x] Vegetation tests (6 tests) — NPP, fire probability, biomass rate
- [x] Cross-section tests (8 tests) — central angle, interpolation, lat/lon conversion
- [x] Simulation orchestrator tests (8 tests) — generate, advance, inspect, cross-section

## Future Work
- [ ] Add WebSocket support for real-time simulation streaming
- [ ] Add binary serialization for large array transfers (MessagePack)
- [ ] Add authentication/rate limiting for multi-user deployments
- [ ] Move snapshot management to backend with delta compression
- [ ] Add integration tests for API endpoints
- [ ] Performance optimization: parallel engine ticks using Task.WhenAll
- [ ] Biomatter engine: migrate simple non-plant biomatter system (microbes, plankton, reef organisms) to backend — ocean chemistry, biogenic sedimentation, atmosphere O₂/CH₄ feedback, petroleum source-rock pipeline (see Phase 7 of implementation plan)
- [ ] Add `biomatterMap` and `organicCarbonMap` state arrays and REST endpoints (`GET /api/state/biomattermap`, `GET /api/state/organiccarbonmap`)
- [ ] Add `BiomatterEngine` to `GeoTime.Core/Engines/` with cyanobacteria, plankton, reef, fungi productivity models
- [ ] Add `SED_OIL_SHALE` to `RockType` enum for petroleum source rocks
- [ ] Expand `CellInspection` model with biomatter density, organic carbon, and reef presence fields
