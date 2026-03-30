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

## Phase 4 — Atmosphere, Climate & Weather ✅

**Goal**: Climate drives biomes. Winds, precipitation, and temperature change over geological time.

### AGENT-ATMO: General Circulation
- [x] 3-cell model: Hadley (0–30°), Ferrel (30–60°), Polar (60–90°) per hemisphere (`src/geo/climate-engine.ts`)
- [x] Solar insolation: I = S₀ · cos(θ) · (1 - α) with latitude-based albedo (`src/geo/climate-engine.ts`)
- [x] Differential albedo: ocean (0.06), land (0.30), ice (0.85) (`src/geo/climate-engine.ts`)
- [x] Trade winds, westerlies, polar easterlies from 3-cell formulas (`src/geo/climate-engine.ts`)
- [x] windUMap and windVMap updated each tick (`src/geo/climate-engine.ts`)

### AGENT-ATMO: Ice Ages & Forcing
- [x] Milankovitch eccentricity cycle (100 kyr period) forcing (`src/geo/climate-engine.ts`)
- [x] Greenhouse forcing: ΔT = λ · ln(CO₂/CO₂_ref) / ln(2), λ = 3°C per doubling (`src/geo/climate-engine.ts`)
- [x] Altitude lapse rate correction: 6.5°C per km (`src/geo/climate-engine.ts`)
- [x] Ice-albedo feedback: fraction of ice cells tracked (`src/geo/climate-engine.ts`)
- [x] Snowball Earth threshold: equatorial temp < -10°C detection (`src/geo/climate-engine.ts`)
- [x] ICE_AGE_ONSET / ICE_AGE_END events from mean temperature (`src/geo/atmosphere-engine.ts`)

### AGENT-ATMO: Weather Systems
- [x] Frontal precipitation at polar front (~60°) and ITCZ (<10°) (`src/geo/weather-engine.ts`)
- [x] Stochastic mid-latitude cyclones with precipitation (`src/geo/weather-engine.ts`)
- [x] Tropical cyclone spawning: SST > 26°C, 5-20° latitude (`src/geo/weather-engine.ts`)
- [x] Orographic precipitation: windward multiplier (×2), leeward rain shadow (×0.5) (`src/geo/weather-engine.ts`)
- [x] precipitationMap updated each tick (`src/geo/weather-engine.ts`)

### AGENT-ATMO: Cloud Generation
- [x] Cloud type assignment (7 CloudGenus types used) based on temperature, precipitation, moisture (`src/geo/weather-engine.ts`)
- [x] cloudTypeMap and cloudCoverMap updated each tick (`src/geo/weather-engine.ts`)
- [x] Cloud cover between 0–1 (`src/geo/weather-engine.ts`)

### Atmosphere Engine Orchestrator
- [x] Climate → Weather pipeline orchestration (`src/geo/atmosphere-engine.ts`)
- [x] Sub-tick batching with configurable minimum interval (default 1.0 Ma) (`src/geo/atmosphere-engine.ts`)
- [x] Event emission: CLIMATE_UPDATE, TROPICAL_CYCLONE_FORMED, ICE_AGE_ONSET, ICE_AGE_END, SNOWBALL_EARTH (`src/geo/atmosphere-engine.ts`)
- [x] getClimateEngine() and getWeatherEngine() accessors (`src/geo/atmosphere-engine.ts`)

### AGENT-RENDER: Atmosphere
- [x] Biome color function using Whittaker diagram (temperature × precipitation, 12 biomes) (`src/render/globe-renderer.ts`)
- [x] updateClimateMap() method creates RGBA biome overlay texture (`src/render/globe-renderer.ts`)

### Integration
- [x] AtmosphereEngine wired into main render loop after SurfaceEngine (`src/main.ts`)
- [x] New event types added to shared types: CLIMATE_UPDATE, TROPICAL_CYCLONE_FORMED, SNOWBALL_EARTH (`src/shared/types.ts`)
- [x] Payload interfaces: ClimateUpdatePayload, TropicalCycloneFormedPayload, SnowballEarthPayload (`src/shared/types.ts`)

### Testing
- [x] 37 unit tests across 3 new test files (Vitest)
  - `tests/climate-engine.test.ts` — 14 tests (insolation, winds, greenhouse, Milankovitch, Snowball Earth, determinism)
  - `tests/weather-engine.test.ts` — 11 tests (clouds, precipitation, orographic, cyclones, fronts, determinism)
  - `tests/atmosphere-engine.test.ts` — 12 tests (orchestration, events, sub-tick batching, getters, determinism)
- [x] 3 new Playwright integration tests (`e2e/app-shell.spec.ts`)
  - Atmosphere engine active after unpausing (no crash)
  - Planet generation with atmosphere engine initialized
  - No console errors during atmospheric simulation

## Phase 5 — Cross-Section Viewer ✅

**Goal**: User draws a line on the surface and sees a geologically accurate vertical cross-section from atmosphere to core.

### AGENT-SECTION: Cross-Section Engine
- [x] Great-circle interpolation for polyline path sampling (`src/geo/cross-section-engine.ts`)
- [x] Haversine central angle computation for accurate distance (`src/geo/cross-section-engine.ts`)
- [x] N equally-spaced sample points along great-circle arcs (default N=512) (`src/geo/cross-section-engine.ts`)
- [x] Lat/lon → grid cell index coordinate mapping (`src/geo/cross-section-engine.ts`)
- [x] Multi-segment polyline path support (arbitrary waypoints) (`src/geo/cross-section-engine.ts`)
- [x] Stratigraphy stack retrieval at each sample point (`src/geo/cross-section-engine.ts`)
- [x] Surface elevation, crust thickness, soil type/depth sampling from SharedArrayBuffer (`src/geo/cross-section-engine.ts`)
- [x] Deep earth zone definitions (Lithospheric Mantle through Inner Core, 7 zones) (`src/geo/cross-section-engine.ts`)
- [x] CrossSectionProfile construction with full metadata (`src/geo/cross-section-engine.ts`)
- [x] Event-driven: listens for CROSS_SECTION_PATH, emits CROSS_SECTION_READY (`src/geo/cross-section-engine.ts`)
- [x] Label visibility toggle via LABEL_TOGGLE event (`src/geo/cross-section-engine.ts`)

### AGENT-RENDER: Cross-Section Renderer
- [x] Canvas 2D rendering of cross-section profile (`src/render/cross-section-renderer.ts`)
- [x] Split vertical scale: linear 0–100 km, logarithmic 100–6371 km (`src/render/cross-section-renderer.ts`)
- [x] Rock type colour mapping for all 56 rock types (igneous, sedimentary, metamorphic, deep earth) (`src/render/cross-section-renderer.ts`)
- [x] Rock type name mapping for all types (`src/render/cross-section-renderer.ts`)
- [x] Soil order name mapping for all 12 USDA orders (`src/render/cross-section-renderer.ts`)
- [x] Deep earth zone rendering with zone labels (`src/render/cross-section-renderer.ts`)
- [x] Unconformity markers (hatched red dashed lines) (`src/render/cross-section-renderer.ts`)
- [x] Fault detection and indicator rendering (`src/render/cross-section-renderer.ts`)
- [x] Moho discontinuity visualization per sample column (`src/render/cross-section-renderer.ts`)
- [x] Surface elevation profile line (`src/render/cross-section-renderer.ts`)
- [x] Layer label generation with rock type, age (Ma), and optional soil order (`src/render/cross-section-renderer.ts`)
- [x] Anti-collision label resolution (push overlapping labels apart) (`src/render/cross-section-renderer.ts`)
- [x] Legend panel with colour swatches for visible rock types (`src/render/cross-section-renderer.ts`)
- [x] Y-axis depth labels (linear + logarithmic ticks) (`src/render/cross-section-renderer.ts`)
- [x] X-axis distance labels in km (`src/render/cross-section-renderer.ts`)
- [x] Scale break indicator at 100 km (`src/render/cross-section-renderer.ts`)
- [x] Export cross-section as PNG (`src/render/cross-section-renderer.ts`)

### AGENT-UI: Draw Tool & Cross-Section Panel
- [x] Draw Cross-Section button in sidebar (`src/ui/app-shell.ts`)
- [x] Draw mode toggle with visual feedback (`src/ui/app-shell.ts`)
- [x] Cross-section panel (bottom panel, hidden by default) (`src/ui/app-shell.ts`)
- [x] Panel header with Labels toggle, Export PNG, and Close buttons (`src/ui/app-shell.ts`)
- [x] Cross-section canvas element for 2D rendering (`src/ui/app-shell.ts`)
- [x] Label toggle button with opacity feedback (`src/ui/app-shell.ts`)
- [x] Export PNG button triggers download (`src/ui/app-shell.ts`)
- [x] Panel show/hide API (`src/ui/app-shell.ts`)
- [x] Draw mode resets on new planet generation (`src/ui/app-shell.ts`)

### Shared Types
- [x] CrossSectionSample interface (distanceKm, surfaceElevation, crustThicknessKm, soilType, soilDepthM, layers) (`src/shared/types.ts`)
- [x] DeepEarthZone interface (name, topKm, bottomKm, rockType) (`src/shared/types.ts`)
- [x] CrossSectionProfile interface (samples, totalDistanceKm, pathPoints, deepEarthZones) (`src/shared/types.ts`)
- [x] Enhanced CrossSectionReadyPayload with profile data (`src/shared/types.ts`)

### Integration
- [x] CrossSectionEngine instantiated and wired into event bus in main.ts (`src/main.ts`)
- [x] Engine initialized with stateViews and stratigraphy on planet generation (`src/main.ts`)
- [x] CROSS_SECTION_READY event renders profile to panel canvas (`src/main.ts`)
- [x] Label toggle re-renders active profile (`src/main.ts`)
- [x] Export PNG creates downloadable link (`src/main.ts`)
- [x] Engine and UI state cleared on new planet generation (`src/main.ts`)

### Testing
- [x] 91 unit tests across 2 new test files (Vitest)
  - `tests/cross-section-engine.test.ts` — 54 tests (coordinate mapping, central angle, great-circle interpolation, path sampling, path distance, deep earth zones, engine integration, stratigraphy retrieval, profile construction, edge cases)
  - `tests/cross-section-renderer.test.ts` — 37 tests (rock colours, rock names, soil names, vertical scale, label building, render function, export PNG)
- [x] 6 new Playwright integration tests (`e2e/app-shell.spec.ts`)
  - Draw Cross-Section button visibility
  - Draw mode toggle behaviour
  - Cross-section panel hidden by default
  - No crash on multiple draw mode toggles
  - Draw mode reset on new planet generation
  - No console errors with cross-section engine initialized

## Phase 6 — Vegetation & Polish ✅

**Goal**: Optional vegetation module, performance improvements, full integration testing, UI polish.

### AGENT-GEO: Vegetation (feature-flagged)
- [x] Net primary productivity (NPP) from temperature and precipitation: Miami Model approximation (`src/geo/vegetation-engine.ts`)
- [x] Biomass accumulation rate scaled to NPP; biomass written to biomassMap (`src/geo/vegetation-engine.ts`)
- [x] Biomass cleared by glaciation (temp < -10°C) or desertification (precip < 50 mm/yr) (`src/geo/vegetation-engine.ts`)
- [x] Forest fire stochastic model: dry season + high biomass → fire probability (`src/geo/vegetation-engine.ts`)
- [x] Vegetation albedo feedback: forests darker than grassland → warming feedback (`src/geo/vegetation-engine.ts`)
- [x] Grass coverage on slopes where soil depth sufficient and precipitation > 250 mm/yr (`src/geo/vegetation-engine.ts`)
- [x] Feature-flag support: `enabled` config option, defaults to true (`src/geo/vegetation-engine.ts`)

### AGENT-KERNEL: Performance Pass
- [x] Sparse delta snapshots: `computeDelta()` / `applyDelta()` helpers for block-level diffing (`src/kernel/snapshot-manager.ts`)
- [x] Adaptive tick rate: configurable `maxFrameBudget` caps real-time dt to prevent UI jank (`src/kernel/sim-clock.ts`)

### AGENT-UI: Polish
- [x] Click-to-inspect: viewport click → `InspectInfo` panel showing elevation, rock type, soil order, climate, biomass (`src/ui/app-shell.ts`)
- [x] Geological timeline: horizontal strip below globe with event markers and cursor position (`src/ui/app-shell.ts`)
- [x] Layer overlay panel: 6 toggleable layers — plates, temperature, precipitation, soil, clouds, biomass (`src/ui/app-shell.ts`)
- [x] Planet URL sharing: seed encoded in URL fragment for shareable links (`src/main.ts`)

### Shared Types & Buffer Layout
- [x] `biomassMap` added to SharedArrayBuffer layout — 14th typed array view (Float32, 1 048 576 bytes) (`src/shared/types.ts`)
- [x] `VEGETATION_UPDATE` and `FOREST_FIRE` event types with typed payloads (`src/shared/types.ts`)
- [x] Total buffer size: ~11.8 MB (14 typed array views) (`src/shared/types.ts`)

### Testing
- [x] 36 unit tests for VegetationEngine (`tests/vegetation-engine.test.ts`)
  - computeNPP: 7 tests (boundary conditions, monotonicity, ranges)
  - nppToBiomassRate: 3 tests (zero, positive, linearity)
  - computeFireProbability: 5 tests (threshold, dry/wet, biomass, bounds)
  - computeVegetationAlbedo: 4 tests (zero, forest, grassland, transition)
  - VegetationEngine lifecycle: 17 tests (init, tick, disable, underwater, glaciation, desert, soil, cap, events, fire, determinism, batching)
- [x] 9 integration validation tests (`tests/integration-validation.test.ts`)
  - Continent distribution plausibility (flood-fill for ≥2 major continents)
  - Soil type coverage (≥1 USDA order after atmosphere + surface ticks)
  - Stratigraphy consistency (valid layer stacks after tectonic processing)
  - Erosion volume conservation (height ratio stability during surface processing)
  - Vegetation integration (biomass accumulation with favorable climate)
  - Vegetation glaciation clearing
  - Multi-seed smoke tests (3 seeds: 42, 12345, 98765)
- [x] 7 new Playwright integration tests (`e2e/app-shell.spec.ts`)
  - Layer overlay toggles visible in sidebar
  - Layer toggle button interaction
  - Geological timeline strip rendered
  - Seed encoded in URL fragment
  - Seed loaded from URL fragment
  - Vegetation engine runs without errors
  - No console errors after new planet generation with Phase 6 engines

---

## Phase 7 — Biomatter ✅ (Backend)

**Goal**: Simple non-plant biomatter that reshapes ocean chemistry, sedimentation, atmosphere, and produces petroleum source rocks. Feature-flagged, building on vegetation from Phase 6.

### AGENT-GEO: Biomatter Engine
- [x] `BiomatterEngine` in `GeoTime.Core/Engines/` with feature flag (`backend/GeoTime.Core/Engines/BiomatterEngine.cs`)
- [x] Cyanobacteria: oxygenic photosynthesis in shallow marine (> 10°C), OXYGENATION_EVENT at threshold
- [x] Marine plankton (phytoplankton): Gaussian temperature bell curve (optimal 20°C), photic zone depth factor
- [x] Reef organisms: warm shallow marine (18–30°C, < 50m), carbonate reef growth modifying heightMap
- [x] Benthic organisms: deep marine biomatter with bioturbation
- [x] Fungi & decomposers: terrestrial cells with soil > 0.1m, proportional to vegetation biomass (0.2×)
- [x] Microbial mats / stromatolites: shallow tidal carbonate deposition

### AGENT-GEO: Petroleum Source Rocks
- [x] Organic carbon burial: marine plankton die in anoxic basins → `OrganicCarbonMap` accumulation
- [x] Kerogen formation: buried organic carbon at depth > 2km, temp 60–120°C → `SED_OIL_SHALE` stratigraphy
- [x] PETROLEUM_DEPOSIT event logged on first conversion per tick

### AGENT-GEO: Biogenic Sedimentation
- [x] Coccolith ooze → SED_CHALK (deep marine, high plankton productivity)
- [x] Radiolarian/diatom ooze → SED_CHERT / SED_DIATOMITE (cold nutrient-rich zones)
- [x] Reef limestone → SED_LIMESTONE (warm shallow marine with reef)
- [x] Stromatolite carbonate → SED_LIMESTONE (shallow tidal, microbial mats)
- [x] Phosphorite → SED_PHOSPHORITE (high organic concentration, upwelling)
- [x] Banded iron formation → SED_IRONSTONE (low O₂, cyanobacteria active)

### AGENT-ATMO: Atmosphere Feedback
- [x] O₂ production from cyanobacteria + phytoplankton photosynthesis
- [x] CH₄ production from anaerobic microbes in deep anoxic ocean
- [x] CO₂ drawdown via biological pump (plankton fix CO₂, organic matter sinks)
- [x] CH₄ oxidation when O₂ present (natural decay)
- [x] O₂ gating rules: < 0.1% anaerobic only; > 0.1% aerobic marine; > 2% terrestrial
- [x] OXYGENATION_EVENT fired once when atmospheric O₂ crosses 2% threshold

### Models & State
- [x] `SED_OIL_SHALE` added to `RockType` enum (`backend/GeoTime.Core/Models/Enums.cs`)
- [x] `CH4` field added to `AtmosphericComposition` (`backend/GeoTime.Core/Models/SimulationModels.cs`)
- [x] `BiomatterMap` (float[]) added to `SimulationState` — non-plant biomatter kg C/m²
- [x] `OrganicCarbonMap` (float[]) added to `SimulationState` — buried organic carbon kg C/m²
- [x] `CellInspection` expanded: `BiomatterDensity`, `OrganicCarbon`, `ReefPresent` fields

### API Endpoints
- [x] `GET /api/state/biomattermap` — JSON array of biomatter density
- [x] `GET /api/state/organiccarbonmap` — JSON array of organic carbon
- [x] `GET /api/state/biomattermap/binary` — MessagePack binary
- [x] `GET /api/state/organiccarbonmap/binary` — MessagePack binary
- [x] Cell inspection (`GET /api/state/inspect/:cellIndex`) returns biomatter fields

### Orchestration & Serialization
- [x] `BiomatterEngine` integrated into `SimulationOrchestrator` (parallel tick with vegetation)
- [x] State serialization updated: `BiomatterMap` + `OrganicCarbonMap` included in snapshot bytes
- [x] Frontend `backend-client.ts` updated with new endpoints, types, `CellInspection` fields

### Testing
- [x] 30 BiomatterEngine unit tests (`backend/GeoTime.Tests/BiomatterTests.cs`)
  - Temperature factor: 3 tests (at optimal, far from optimal, always positive)
  - Light factor: 4 tests (land=0, shallow=1, deep<1, very deep→0)
  - Reef factor: 5 tests (land, too deep, too cold, too hot, ideal)
  - Marine productivity: 4 tests (land, low O₂, good conditions, optimal vs cold)
  - Cyanobacteria productivity: 4 tests (land, deep, cold, warm shallow)
  - Fungi productivity: 4 tests (low O₂, cold, no soil, proportional to vegetation)
  - Engine lifecycle: 10 tests (disabled, uninitialized, zero delta, marine, reef, terrestrial, O₂ feedback, oxygenation event, organic carbon, biogenic sedimentation, CH₄, caps, BIF)
- [x] 12 API integration tests (`backend/GeoTime.Tests/ApiIntegrationTests.cs`)
  - BiomatterMap JSON + binary endpoints
  - OrganicCarbonMap JSON + binary endpoints
  - Cell inspection with biomatter fields
- [x] **Total: 156 backend tests** (up from 114)

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
│   ├── surface-engine.ts — Surface process orchestrator
│   ├── cross-section-engine.ts — Cross-section path sampling & profile builder
│   └── vegetation-engine.ts — Vegetation NPP, biomass, fire, albedo feedback
├── render/
│   ├── icosphere.ts   — Icosphere mesh generation
│   ├── globe-renderer.ts — Three.js renderer with height displacement
│   └── cross-section-renderer.ts — Canvas 2D cross-section renderer
├── ui/
│   └── app-shell.ts   — DOM-based UI shell
└── main.ts            — Application entry point, wires everything together
tests/                 — 25 Vitest test files (351 tests)
e2e/                   — Playwright integration tests (29 tests)
```

### Key Design Decisions
- **Grid Size**: 512×512 (configurable via `GRID_SIZE` in `shared/types.ts`)
- **Buffer Layout**: ~11.8 MB SharedArrayBuffer with 14 typed array views
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
- **Cross-Section Engine**: Great-circle path sampling with Haversine distance, 512 sample points default
- **Cross-Section Renderer**: Canvas 2D with split vertical scale (linear 0-100 km, log 100-6371 km)
- **Deep Earth Zones**: 7 zones from Lithospheric Mantle (30 km) to Inner Core (6371 km)
- **Vegetation Engine**: Miami Model NPP, feature-flagged, stochastic fire model
- **Biomass Map**: Per-cell biomass in kg/m², max 40 kg/m², cleared by glaciation/desertification
- **Adaptive Tick Rate**: Configurable maxFrameBudget caps real-time dt (50 ms default in main.ts)
- **Sparse Delta Snapshots**: 256-byte block-level diffing for efficient snapshot storage
- **URL Seed Sharing**: Planet seed encoded in URL fragment (#seed=N) for shareable links
- **UI Inspect Panel**: Floating panel showing per-cell location report on viewport click
- **Layer Overlays**: 6 toggleable layer views — plates, temperature, precipitation, soil, clouds, biomass
- **Timeline Strip**: Geological timeline with event markers and cursor position

### Phase 4 Prerequisites
- The surface process engines are fully functional and tested
- Erosion, glacial, weathering, and pedogenesis systems all update the shared height map and stratigraphy
- Temperature, precipitation, and wind maps in SharedArrayBuffer are ready for atmospheric simulation
- The event system supports EROSION_CYCLE, GLACIATION_ADVANCE/RETREAT events
- Glacial engine tracks ice extent and ELA for climate feedback integration

### Phase 5 Prerequisites
- All Phase 1-4 systems are complete and stable (215 unit tests across 21 files, 16 e2e tests)
- StratigraphyStack provides per-cell layer stacks with getLayers() API
- heightMap, crustThicknessMap, soilTypeMap, soilDepthMap available in SharedArrayBuffer
- Event system supports CROSS_SECTION_PATH and CROSS_SECTION_READY events
- All rock types, soil orders, and deformation types defined in shared types
