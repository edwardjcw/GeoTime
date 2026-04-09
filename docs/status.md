# GeoTime — docs/status.md

## Instructions for Future Agents

This file is the **first thing to read** when picking up work on GeoTime. It provides context, constraints, and a tracker for what has been done and what remains.

### Key Principles
1. **Backend-first**: All simulation logic lives in C# (`backend/GeoTime.Core`). The TypeScript frontend is display-only. New features that affect planet evolution must be implemented as engines or services in `GeoTime.Core`.
2. **Test as you go**: Every backend change needs xUnit tests (`backend/GeoTime.Tests/`); every frontend change needs Vitest unit tests (`tests/`) and Playwright E2E tests (`e2e/`). Pull requests should include screenshots.
3. **Simulation realism**: If a feature affects how the land develops over time (e.g., rivers shaping terrain, ice caps modulating climate), it must be wired into `SimulationOrchestrator.AdvanceSimulationCore` — not left as a passive label.
4. **Minimal changes**: Change only what is needed. Avoid refactoring unrelated code.
5. **Build and test before committing**: Run `dotnet test` (backend) and `npx vitest run` (frontend) after changes. Use `report_progress` to push commits.
6. **Keep Unreal up to date**: An alternative to the frontend is `unreal`. Make sure all features in the frontend are equally represented in `unreal`.
7. **Update documents**: This document should be updated as a change log for future reference. Readme documents should also be kept up to date.
8. **GPU**: Every compute change should be implemented for the GPU if it would improve performance.

### Development Commands
```bash
# Backend
cd backend && dotnet build
cd backend && dotnet test

# Frontend
npm run build
npx vitest run
npx playwright test  # E2E
```

### Plan documents
- `docs/plan-labels.md` — Phase L1–L6: geographic feature detection, naming, labels
- `docs/plan-descriptions.md` — Phase D1-D3: LLM work
- `docs/plan-split.md` — Phase S1–S8: tectonic engine split & performance optimization
- `GeoTime_Implementation_Plan.md` — Original high-level plan

---

## Implementation Tracker

### Phase L1 — Backend: Core Data Models and Name Generator ✅

**Completed** (this session)

- [x] `backend/GeoTime.Core/Models/FeatureModels.cs` — `FeatureType` enum, `FeatureStatus` enum, `FeatureSnapshot` record, `DetectedFeature` class, `FeatureRegistry` class
- [x] `backend/GeoTime.Core/Services/FeatureNameGenerator.cs` — Deterministic syllable-assembly name engine with phoneme banks per feature category; name evolution (Split, Merge, ClimateShift, RenameByAge, Submergence, Exposure)
- [x] `backend/GeoTime.Core/Models/SimulationModels.cs` — Added `FeatureRegistry FeatureRegistry` property to `SimulationState`
- [x] `backend/GeoTime.Tests/FeatureModelTests.cs` — 19 unit tests: determinism, uniqueness (200 names per type), category names, serialisation, name evolution

### Phase L2 — Backend: Primary Feature Detection ✅

**Completed** (this session)

- [x] `backend/GeoTime.Core/Services/FeatureDetectorService.cs` — Detects tectonic plates, continents/oceans/islands/seas (flood-fill on elevation), mountain ranges (>1500 m clusters), subduction zones/rifts (BoundaryClassifier), hotspot chains (proximity grouping), impact basins (event log)
- [x] `backend/GeoTime.Core/SimulationOrchestrator.cs` — `FeatureDetectorService` called after `GeneratePlanet` and at the end of each `AdvanceSimulationCore` tick; `GetFeatureRegistry()` method exposed
- [x] `backend/GeoTime.Api/Program.cs` — `GET /api/state/features` (full registry + optional `?tick=N` historical filter) and `GET /api/state/features/{id}` endpoints
- [x] `backend/GeoTime.Tests/FeatureDetectorTests.cs` — 11 unit tests: land/ocean split, area calculation, centroid accuracy, mountain range detection, rain-shadow flag, tectonic plates, hotspot chain grouping, impact basin registration
- [x] `backend/GeoTime.Tests/ApiIntegrationTests.cs` — 4 new integration tests for the feature API endpoints

**Test count**: 231 (pre-existing) + 34 new = **265 total backend tests passing**

---

## Remaining Phases

### Phase L3 — Hydrological & Atmospheric Feature Detection ✅

**Completed** (this session)

- [x] `backend/GeoTime.Core/Models/SimulationModels.cs` — Added `float[] RiverChannelMap` to `SimulationState` (flow-accumulation values populated by HydroDetectorService each tick; used by ErosionEngine)
- [x] `backend/GeoTime.Core/Services/HydroDetectorService.cs` — D8 flow routing (`ComputeFlowDirection`, `ComputeFlowAccumulation`), river main-stem tracing, lake/inland-sea detection (endorheic basins), ITCZ detection, jet-stream detection (both hemispheres), monsoon belt detection, hurricane corridor detection
- [x] `backend/GeoTime.Core/Engines/ErosionEngine.cs` — Added `RiverChannelErosionBoost` (3×) for cells where `RiverChannelMap[i] ≥ 500`; river channels erode 3× faster than general landscape
- [x] `backend/GeoTime.Core/Services/FeatureDetectorService.cs` — Wires `HydroDetectorService.Detect()` at end of `Detect()`, after primary feature detection
- [x] `backend/GeoTime.Tests/HydroDetectorTests.cs` — 13 unit tests: D8 flow direction (downhill, pit, valley), flow accumulation (outlet highest, source=1), river channel map written to state, ITCZ detected near equator, ITCZ always created, jet streams at mid-latitudes, lake in endorheic basin, hurricane corridor with high SST, river delta type metrics

### Phase L4 — Temporal History and Name Evolution ✅

**Completed** (this session)

- [x] `backend/GeoTime.Core/Services/FeatureEvolutionTracker.cs` — Tick-by-tick feature matching (by ID), change classification (SUBMERGENCE, EXPOSURE, AREA_SHIFT_MAJOR), split detection (new feature overlaps old by ≥30% cells), merge detection (new feature absorbs 2+ old features), name evolution on split (directional prefix) and merge (portmanteau); type-group filtering prevents cross-category false matches
- [x] `backend/GeoTime.Core/SimulationOrchestrator.cs` — `FeatureEvolutionTracker.Track()` called after `FeatureDetectorService.Detect()` each tick to preserve full temporal history
- [x] `backend/GeoTime.Api/Program.cs` — Added `GET /api/state/features/{id}/history` endpoint returning the feature's full `History` list
- [x] `backend/GeoTime.Tests/FeatureEvolutionTests.cs` — 9 unit tests: new feature retained (FEATURE_BORN), extinct feature has closing snapshot (FEATURE_EXTINCT), history accumulated across 3 ticks, AREA_SHIFT_MAJOR adds new snapshot, continent split produces child with SplitFromId, submergence changes status and name, deep-time re-exposure generates fresh name, history endpoint has multiple snapshots, split children have divergent names

**Test count**: 265 (previous) + 13 (L3) + 9 (L4) = **287 total backend tests passing**

### Phase L5 — Frontend: Minimal Label Rendering ✅

**Completed** (this session)

- [x] `src/api/backend-client.ts` — Added `FeatureLabel` interface and `fetchFeatureLabels()` (GET /api/state/features/labels); added `FeaturesUpdated` handler to `SimulationEventHandler`
- [x] `src/render/globe-renderer.ts` — Added `getCamera()` public method for label projection use
- [x] `src/render/label-renderer.ts` — New `LabelRenderer` class: div pool, lat/lon → dot-product back-hemisphere culling, zoom culling, per-frame CSS positioning
- [x] `src/ui/app-shell.ts` — Added `#label-layer` overlay div, `getLabelLayer()` method, and 'labels' button in layer panel
- [x] `src/main.ts` — Wired `fetchFeatureLabels()` → `labelRenderer.setLabels()` on planet generated; per-frame update in render loop; 'labels' layer toggle; `onFeaturesUpdated` refresh; load-state label refresh
- [x] `backend/GeoTime.Api/Program.cs` — Added `GET /api/state/features/labels` endpoint (compact label list with zoomLevel computed from AreaKm2)
- [x] `tests/label-renderer.test.ts` — 11 unit tests: div pool, text content, CSS class, visibility, back-hemisphere culling, zoom culling
- [x] `e2e/app-shell.spec.ts` — Phase L5 test suite: label-layer in DOM, labels toggle button, ≥1 label div after planet generation

### Phase L6 — Integration, Snapshot Persistence, SignalR ✅

**Completed** (this session)

- [x] `backend/GeoTime.Core/SimulationOrchestrator.cs` — `SerializeState()` appends FeatureRegistry JSON after binary block (4-byte length + JSON bytes); `DeserializeState()` reads it back if present; backward-compatible with old snapshots
- [x] `backend/GeoTime.Api/SimulationHub.cs` — `AdvanceSimulation` broadcasts `FeaturesUpdated` (changed feature labels for current tick) to all clients after each step via `PushFeaturesUpdatedAsync()`
- [x] `src/api/backend-client.ts` — `SimulationEventHandler.onFeaturesUpdated` handler; `FeaturesUpdated` case in WebSocket message switch
- [x] `src/main.ts` — `onFeaturesUpdated` triggers `fetchFeatureLabels()` refresh when labels are visible; load-state also refreshes labels
- [x] `backend/GeoTime.Tests/ApiIntegrationTests.cs` — `GetFeatureLabels_ReturnsCompactList` (L5); `SaveAndRestoreSnapshot_PreservesFeatureNames` (L6)
- [x] `backend/GeoTime.Tests/SignalRIntegrationTests.cs` — `Hub_AdvanceSimulation_ReceivesFeaturesUpdated` (L6)

**Test count**: 287 (previous) + 3 new backend = **290 total backend tests passing**; 368 (previous) + 11 new frontend = **379 total frontend tests passing**

---

## Bug Fixes (this session)

### Bug Fix: GPU Selection (Frontend WebGL Renderer)

- [x] `src/render/globe-renderer.ts` — Added `powerPreference: 'high-performance'` to `THREE.WebGLRenderer` so the browser's OS GPU selection mechanism prefers the dedicated NVIDIA GPU over the integrated Intel GPU.

### Bug Fix: Tick Timing Diagnostic in Event Log

- [x] `backend/GeoTime.Core/SimulationOrchestrator.cs` — After each `AdvanceSimulationCore` tick, records a `TICK_STATS` GeoLogEntry with per-phase millisecond breakdown (Tectonic / Surface / Atmo / Veg / Bio / Total). These entries appear in the event log panel to help diagnose why ticks are slow at early simulation times.

### Bug Fix: Agent Status Shows "running" During Advance

- [x] `src/main.ts` — When a simulation advance HTTP request is dispatched, all agent statuses are immediately set to `'running'` so the status panel shows activity during the computation. SignalR progress events update per-phase status as they arrive; the HTTP response handler sets final `'done'`/`'idle'` states.

---

## Phase D1 — Comprehensive Geological Data Model ✅

**Completed** (this session)

- [x] `backend/GeoTime.Core/Models/StratigraphyModels.cs` — New file: `LayerEventType` enum (Normal, ImpactEjecta, VolcanicAsh, VolcanicSoot, GammaRayBurst, OceanAnoxicEvent, SnowballGlacial, IronFormation, MeteoriticIron, MassExtinction, CarbonIsotopeExcursion); `StratigraphicColumn` class with `Layers`, `Surface`, `TotalThicknessM`, `ExtraordinaryLayers`
- [x] `backend/GeoTime.Core/Models/SimulationModels.cs` — Extended `StratigraphicLayer` with event-horizon fields: `LayerEventType EventType`, `string? EventId`, `float IsotopeAnomaly`, `float OrganicCarbonFraction`, `float SootConcentrationPpm`, `bool IsGlobal`; updated `Clone()` to copy new fields
- [x] `backend/GeoTime.Core/Engines/EventDepositionEngine.cs` — New engine: `Deposit(state, stratigraphy, tickEvents, timeMa)` processes each GeoLogEntry for the tick and deposits appropriate event-horizon layers; rules for IMPACT (1/r² ejecta falloff, global distal layer), VOLCANIC_ERUPTION (ash cone + global soot), GRB (global isotope spike), OCEAN_ANOXIC_EVENT (submerged cells), SNOWBALL_EARTH (land cells), CARBON_ISOTOPE_EXCURSION (global); all deposition in parallel
- [x] `backend/GeoTime.Core/SimulationOrchestrator.cs` — Extended `CellInspection` with D1 fields: `StratigraphicColumn Column`, `List<string> FeatureIds`, `string? RiverName`, `string? WatershedFeatureId`, `float DistanceToPlateMarginKm`, `BoundaryType NearestMarginType`, `float EstimatedRockAgeMyears`, `List<GeoLogEntry> LocalEvents`; updated `InspectCell` to populate all new fields; wired `EventDepositionEngine` in `AdvanceSimulationCore`; captures `logLengthBefore` to pass only new-tick events to the deposition engine
- [x] `backend/GeoTime.Tests/StratigraphyTests.cs` — Extended with 8 D1 tests: event field defaults on StratigraphicLayer, Clone copies event fields, StratigraphicColumn Surface returns top layer, ExtraordinaryLayers filters Normal layers, EventDepositionEngine deposits ImpactEjecta in nearby cells, GRB deposits in all cells with IsotopeAnomaly > 0, ejecta thickness falls off with distance, CellInspection FeatureIds includes feature

**Test count**: 290 (previous) + 8 new backend = **298 total backend tests passing**

---

## Phase D2 — Geological Context Assembler ✅

**Completed** (this session)

- [x] `backend/GeoTime.Core/Models/DescriptionModels.cs` — New file: `GeologicalContext` record with all context fields: location (Lat, Lon, CurrentTick, SimAgeDescription), cell data (CellInspection, StratigraphicColumn), feature hierarchy (ContainingFeatures sorted by scale, PrimaryLandFeature, PrimaryWaterFeature), tectonic context (CurrentPlate, DistanceToPlateMarginKm, NearestMarginType, CollidingPlate, SubductingPlate, ConvergenceRateCmPerYear), hydrological context (RiverName, RiverLengthKm, CatchmentAreaKm2, RiverOutletOcean, WatershedName, IsInEndorheicBasin, DrainageGradient), mountain/orographic context (IsInMountainRange, RangeName, RangeMaxElevationM, IsOnWindwardSide, HasRainShadow, MountainOriginType), climate context (BiomeType, MeanTempC, MeanPrecipMm, IsInMonsoonZone, IsInHurricaneCorridor, IsInJetStreamZone, NearestOceanCurrentName, NearestCurrentIsWarm), extraordinary layers, primary feature history, nearby features
- [x] `backend/GeoTime.Core/Services/GeologicalContextAssembler.cs` — New service: `AssembleAsync(int cellIndex)` → `GeologicalContext?`; 10-step assembly: (1) CellInspection fetch, (2) feature registry lookup sorted by scale, (3) tectonic context via BoundaryClassifier (converging/subducting plates within 500 km, convergence rate in cm/yr), (4) hydrological context (river metrics, watershed, endorheic basin detection, outlet ocean), (5) orographic context (windward/leeward via precipitation comparison, rain-shadow, mountain origin type), (6) climate zone membership, (7) extraordinary layer extraction, (8) primary feature history, (9) nearby features within 3 000 km (up to 6), (10) GeologicalContext assembly
- [x] `backend/GeoTime.Tests/ContextAssemblerTests.cs` — 18 unit tests: null for out-of-range index, valid cell returns context, lat/lon matches cell index, sim age description, plate ID matches cell, convergent boundary cell has CONVERGENT margin type, subduction zone has SubductingPlate populated, ContainingFeatures includes cell's features, features sorted by scale (plate first), impact ejecta layer in ExtraordinaryLayers, ExtraordinaryLayers excludes Normal layers, river name populated, river length km > 0, mountain range IsInMountainRange + RangeName, non-mountain cell IsInMountainRange false, NearbyFeatures ≤ 6, nearby features don't include self, nearby features ordered by distance

**Test count**: 298 (previous) + 18 new backend = **316 total backend tests passing**

---

## Phase D3 — LLM Provider Abstraction Layer ✅

**Completed** (this session)

- [x] `backend/GeoTime.Api/Llm/ILlmProvider.cs` — `ILlmProvider` interface with `Name`, `IsAvailable`, `GetStatusAsync`, `GenerateAsync`, `StreamAsync`; `LlmProviderStatus` record
- [x] `backend/GeoTime.Api/Llm/LlmSettingsService.cs` — Runtime-mutable settings singleton; seeds from `appsettings.json` Llm section; persists user changes to `~/.config/GeoTime/llm-settings.json`; per-provider `ProviderSettings` record
- [x] `backend/GeoTime.Api/Llm/LlmProviderFactory.cs` — Resolves active provider at runtime; falls back down preference order; always falls back to Template
- [x] `backend/GeoTime.Api/Llm/GeminiProvider.cs` — Google Generative AI REST API; exponential back-off on 429; SSE streaming
- [x] `backend/GeoTime.Api/Llm/OpenAiProvider.cs` — OpenAI Chat Completions API; configurable BaseUrl for Azure-compatible endpoints; SSE streaming
- [x] `backend/GeoTime.Api/Llm/AnthropicProvider.cs` — Anthropic Messages API; SSE streaming
- [x] `backend/GeoTime.Api/Llm/OllamaProvider.cs` — Local Ollama instance; `/api/chat` batch + streaming; model pull detection
- [x] `backend/GeoTime.Api/Llm/LlamaSharpProvider.cs` — In-process GGUF loading stub; GGUF magic-byte validation; `NotifyModelReady()` for setup flow
- [x] `backend/GeoTime.Api/Llm/TemplateFallbackProvider.cs` — Always-available fallback; returns structured placeholder prose
- [x] `backend/GeoTime.Api/Llm/LocalLlmSetupService.cs` — Guided setup flows for Ollama (install + pull) and LlamaSharp (GGUF download + validate); `Channel<LlmSetupProgress>` for SSE streaming
- [x] `backend/GeoTime.Api/Program.cs` — 5 new LLM endpoints: `GET /api/llm/providers`, `GET /api/llm/active`, `PUT /api/llm/active`, `POST /api/llm/setup/{provider}`, `GET /api/llm/setup/{provider}/progress` (SSE)
- [x] `backend/GeoTime.Api/appsettings.json` — Documented all Llm config keys
- [x] `backend/GeoTime.Tests/LlmProviderTests.cs` — 21 unit + integration tests (all pass)
- [x] `backend/GeoTime.Tests/LocalLlmSetupTests.cs` — 8 setup-flow tests (all pass)
- [x] `src/api/backend-client.ts` — Added `getLlmProviders`, `getLlmActive`, `setLlmActive`, `startLlmSetup`, `openLlmSetupProgress` + type interfaces
- [x] `src/ui/app-shell.ts` — ⚙ LLM button in HUD; `#llm-settings-panel` with provider list, radio buttons, API key fields, Setup ▶ button, setup progress sub-panel; `setLlmProviders`, `showLlmSetupProgress`, `onLlmSettingsChanged`, `onLlmSetup` public API
- [x] `src/main.ts` — LLM panel wiring: `refreshLlmPanel()`, `onLlmSettingsChanged`, `onLlmSetup`, startup fetch
- [x] `e2e/app-shell.spec.ts` — 7 E2E tests for the LLM settings panel (button visibility, open/close, provider list, title, radio buttons, API interaction)

**Test count**: 316 (previous) + 21 new backend = **337 total backend tests passing**; 379 frontend Vitest tests still pass

---

## Bug Fixes (Session after Phase D3)

### Fixes Applied
- [x] **Labels off by default** — `LabelRenderer._visible` now starts `false`; container `display:none`; geographic labels only appear when the "labels" layer toggle is activated.
- [x] **Event Layers dropdown** — Select element is hidden by default and only shown when the "event layers" toggle is active.
- [x] **Cell info panel stable** — `showInspectPanel()` now updates DOM values in-place (Map cache of span elements) instead of rebuilding `innerHTML` on every refresh.
- [x] **Log/LLM icons stable** — Added `marginLeft: auto` and `flexShrink: 0` to the log and LLM HUD buttons so they are always anchored to the right edge regardless of other HUD content changes.
- [x] **GPU info (both)** — `GlobeRenderer.getWebGLRendererInfo()` queries `WEBGL_debug_renderer_info`; `setComputeMode()` tooltip now shows both Backend and Frontend GPU.
- [x] **Log shows tick count** — `SimulationOrchestrator.TickCount` added, reset on `GeneratePlanet`, incremented each `AdvanceSimulationCore` call. Returned in `/api/simulation/advance` response. Log panel title shows total ticks.
- [x] **Simulation runs when UI hidden** — Simulation advance loop moved to `setInterval(simTick, 200)` independent of `requestAnimationFrame`. The rAF loop is now rendering-only.
- [x] **Topo layer fix** — Replaced dynamic per-run min/max normalization (which caused all-white when heights clustered) with fixed physical reference ranges: ocean [-6000, -100]→topoColor, land heights used directly (clamped to 4000m max).

### Known Limitations / Not Yet Fixed
- ~~**Plate drift / land deformation**~~ — **FIXED** (this session): True plate advection now implemented via Rodrigues' rotation formula. See "Plate Advection" section below.

---

## Plate Advection — True Plate Drift ✅

**Completed** (this session)

- [x] `backend/GeoTime.Core/Engines/TectonicEngine.cs` — Added `AdvectPlates()` method: applies Rodrigues' rotation formula to every cell based on its plate's Euler pole (`AngularVelocity.Lat/Lon/Rate`); scatter-based advection to new grid positions; continental–continental collisions produce mountain building and crustal thickening; oceanic–continental collisions result in subduction; gap cells (divergent rifts) filled with fresh oceanic basalt crust at mid-ocean ridge depth (−4000 m); `FindNearestPlate()` assigns gap cells to nearest plate. Added `UpdatePlateCenters()` to recalculate plate centroids and areas after advection. Advection runs once per `Tick()` with full `deltaMa` for proper grid-scale movement; sub-tick boundary processes run afterward on the updated plate configuration.
- [x] `backend/GeoTime.Core/Engines/StratigraphyStack.cs` — Added `RemapColumns()` method: moves stratigraphic columns to new cell indices during advection, merges colliding columns (with layer budget enforcement), fills gap cells with fresh oceanic basement (gabbro + pillow basalt).
- [x] `backend/GeoTime.Tests/PlateAdvectionTests.cs` — 10 unit tests: plate map changes after advection, height map modified, gap cells filled with oceanic crust, zero rotation rate preserves state, stratigraphy remaps with cells, plate centers update, collision builds mountains, integration with SimulationOrchestrator, RemapColumns moves stratigraphy, RemapColumns fills gaps.

**Test count**: 337 (previous) + 10 new backend = **347 total backend tests passing**

---

## GPU Performance — ILGPU Kernel Offloading ✅

**Completed** (this session)

### Tectonic Engine GPU Kernels (3 new kernels)
- [x] `backend/GeoTime.Core/Compute/GpuComputeService.cs` — `AdvectScatterKernel`: GPU-side Rodrigues' rotation (sin/cos/asin/atan2 × 262K cells) computes destination cell index for plate advection. Hybrid approach: GPU handles expensive trig, CPU handles irregular scatter + collision resolution via precomputed `destMap[]`.
- [x] `backend/GeoTime.Core/Compute/GpuComputeService.cs` — `GapFillKernel`: Per-cell GPU kernel fills empty cells (hitCount == 0) with fresh oceanic crust properties (-4000m height, 7km crust, basalt, current age).
- [x] `backend/GeoTime.Core/Compute/GpuComputeService.cs` — `UpdatePlateCentersKernel`: Per-cell atomic accumulation of Cartesian coordinates (using `Atomic.Add` on double buffers) for plate center-of-mass computation.
- [x] `backend/GeoTime.Core/Engines/TectonicEngine.cs` — Wired GPU path with CPU fallback for `AdvectPlates()` (Phase 1: GPU destMap, Phase 2: CPU scatter, Phase 3: GPU gap fill) and `UpdatePlateCenters()`.

### Climate Engine GPU Kernels (2 new kernels)
- [x] `backend/GeoTime.Core/Compute/GpuComputeService.cs` — `ClimateTemperatureKernel`: Per-cell temperature update on GPU (latitude-based T_base + lapse rate adjustment + GHG/Milankovitch forcing + alpha blending).
- [x] `backend/GeoTime.Core/Compute/GpuComputeService.cs` — `ComputeWindsKernel`: Per-cell 3-band Hadley/Ferrel/polar wind U/V computation on GPU.
- [x] `backend/GeoTime.Core/Engines/ClimateEngine.cs` — Wired GPU temperature + wind kernels with CPU fallback; aggregates (tempSum, iceCells) remain on CPU.

### Glacial Engine GPU Kernel (1 new kernel)
- [x] `backend/GeoTime.Core/Compute/GpuComputeService.cs` — `IceThicknessKernel`: Per-cell ice accumulation/ablation on GPU (accumulation above ELA when temp < glaciation threshold; ablation below).
- [x] `backend/GeoTime.Core/Engines/GlacialEngine.cs` — Wired GPU ice kernel for Phase 1; erosion/moraine (neighbor-dependent) stays on CPU.
- [x] `backend/GeoTime.Core/Engines/SurfaceEngine.cs` — Accepts optional `GpuComputeService?`, passes to `GlacialEngine`.
- [x] `backend/GeoTime.Core/SimulationOrchestrator.cs` — Passes `_gpu` to `SurfaceEngine` constructor.

### ILGPU Dependency
- [x] `backend/GeoTime.Core/GeoTime.Core.csproj` — Added `ILGPU.Algorithms 1.5.3` for `XMath` trig functions.

**Total new GPU kernels**: 8 (IsostasyKernel + DiffuseKernel already existed = 10 total)

---

## Advanced Log View — Performance Graphs ✅

**Completed** (this session)

- [x] `src/ui/app-shell.ts` — Expandable "▶ Advanced View" section in the log panel:
  - Tick history ring buffer (last 50 ticks of per-agent timing stats)
  - Stacked area chart: per-agent timing (Tectonic/Surface/Atmosphere/Vegetation/Biomatter) with color-coded legend
  - Line chart: total ms per tick with 50ms/100ms threshold zones and color-coded current value
  - Real-time processing status row showing each agent's current state (idle ○ / running ⟳ / done ✓)
  - `pushTickHistory()` and `setAdvancedProcessingStatus()` public API
- [x] `src/main.ts` — Wired `pushTickHistory()` after each advance response; `setAdvancedProcessingStatus()` called on every agent status update

---

## Layer Verification Tests ✅

**Completed** (this session)

- [x] `tests/layer-verification.test.ts` — 28 Vitest tests verifying all layer selections:
  - Temperature color mapping: extreme cold (blue), freezing (white), hot (red), gradient in cold range
  - Precipitation color mapping: arid (tan), wet (blue-green), mid-range gradient
  - Cloud cover color mapping: dry (dark grey), wet (white)
  - Biomass color mapping: barren (dark), lush (vivid green)
  - Topography color mapping: deep ocean (blue), shallow ocean, lowland (green), highland (yellow), mountains (brown), peaks (white)
  - Soil order color mapping: None (grey), all 12 USDA orders distinct, unknown order (grey)
  - Random point verification: temperature at 5 lat/lon points, precipitation at 4 points, topography at 10 points, biomass at 5 points, plate IDs at 5 points, soil orders at 3 points, cloud cover at 3 points
  - Cross-layer consistency: single cell checked across all layers for valid values

**Test count**: 379 (previous frontend) + 28 new layer verification = **407 total frontend Vitest tests passing**

---

## Historical Label Tracking — Dynamic Surface Name Lineage ✅

**Completed** (this session)

- [x] `backend/GeoTime.Core/Models/FeatureModels.cs` — Added `List<string> FormerNames` property to `DetectedFeature`. Accumulates all former names from the feature's own history and inherited ancestor names during splits, merges, submergence, exposure, and age-evolution events.
- [x] `backend/GeoTime.Core/Services/FeatureEvolutionTracker.cs` — Updated split handling (Pass 2 and Pass 3) to call `InheritFormerNames()` on child features, propagating the parent's name lineage. Updated merge handling (Pass 3) to collect all absorbed parents' names into the merged feature. Updated `MergeHistory()` to record old name in `FormerNames` before name evolution (submergence, exposure, age milestone). Updated `CloseFeature()` to copy `FormerNames`. Added `InheritFormerNames()` helper method.
- [x] `backend/GeoTime.Api/Program.cs` — Updated `GET /api/state/features/labels` endpoint to include `formerNames` in the compact label response.
- [x] `src/api/backend-client.ts` — Added `formerNames: string[]` to `FeatureLabel` interface.
- [x] `src/render/label-renderer.ts` — Updated `_syncPool()` to display former names as a subtitle (most recent former name shown as "(formerly X)") and set a tooltip with the full lineage chain ("Former names: A → B → C").
- [x] `backend/GeoTime.Tests/FeatureEvolutionTests.cs` — 4 new tests: split children inherit parent FormerNames, submergence records old name in FormerNames, merge collects absorbed parent names, FormerNames preserved across ticks.
- [x] `tests/label-renderer.test.ts` — 4 new tests: former name subtitle display, most recent former name shown, full lineage tooltip, no former names when list empty.

**Test count**: 347 (previous backend) + 4 new = **351 total backend tests**; 407 (previous frontend) + 4 new = **411 total frontend Vitest tests**

---

## Phase S1 — Boundary Classifier Caching ✅

**Completed** (this session)

- [x] `backend/GeoTime.Core/Engines/TectonicEngine.cs` — Added `_cachedBoundaries` field. `Tick()` and `TickAsync()` classify boundaries once after `AdvectPlates()` and reuse the cached list for all sub-ticks. Cache cleared after the sub-tick loop. `ProcessTick()` uses `_cachedBoundaries ?? BoundaryClassifier.Classify(...)` for safety.
- [x] `backend/GeoTime.Tests/SplitPhaseTests.cs` — 2 tests: `S1_BoundaryCacheReusedAcrossSubTicks`, `S1_SyncTickStillWorksWithCache`

## Phase S2 — Async Pipeline ✅

**Completed** (this session)

- [x] `backend/GeoTime.Core/Engines/TectonicEngine.cs` — Added `TickAsync()` with `Func<string, Task>` callback for sub-phase progress (`tectonic:advection`, `tectonic:collision`, `tectonic:boundaries`, `tectonic:dynamics`, `tectonic:volcanism`)
- [x] `backend/GeoTime.Core/SimulationOrchestrator.cs` — Extended `SimulationTickStats` with sub-phase timing fields (`TectonicAdvectionMs`, `TectonicCollisionMs`, `TectonicBoundaryMs`, `TectonicDynamicsMs`, `TectonicVolcanismMs`, `TectonicTotalMs`). Added `AdvanceSimulationAsync()` and `AdvanceSimulationCoreAsync()` methods that use `TickAsync` and flush progress via async callbacks.
- [x] `backend/GeoTime.Api/SimulationHub.cs` — Converted `AdvanceSimulation` to use `AdvanceSimulationAsync` so SignalR messages flush between sub-phases
- [x] `backend/GeoTime.Api/Program.cs` — REST `/api/simulation/advance` uses `AdvanceSimulationAsync`; both advance and stats endpoints include sub-phase timing
- [x] `src/api/backend-client.ts` — Added optional sub-phase timing fields to `TickStats` interface
- [x] `src/main.ts` — Updated `agentStatuses` to include 5 tectonic sub-phases; updated `updateAgentStatuses`, `updateAgentStatusesFromStats`, `PHASE_LABELS` for sub-phase display
- [x] `src/ui/app-shell.ts` — Updated `updateLogPanel` to show sub-phase timing breakdown when available; updated `pushTickHistory` and `_tickHistory` types
- [x] `backend/GeoTime.Tests/SplitPhaseTests.cs` — 2 async tests: `S2_AsyncAdvance_ProducesSubPhaseProgress`, `S2_AsyncAdvance_PopulatesSubPhaseTiming`

**Test count**: 351 (previous backend) + 4 new = **355 total backend tests**

## Phase S3 — GPU Boundary Classification Kernel ✅

**Completed** (this session)

- [x] `backend/GeoTime.Core/Compute/GpuComputeService.cs` — Added `BoundaryClassifyKernel` ILGPU kernel: per-cell boundary classification checking 4 neighbors, computing relative plate velocity via Euler pole, and classifying as CONVERGENT/DIVERGENT/TRANSFORM. Added `ClassifyBoundariesGpu()` public method that runs the kernel and compacts non-NONE results into `List<BoundaryCell>`.
- [x] `backend/GeoTime.Core/Engines/TectonicEngine.cs` — Added `ClassifyBoundaries()` helper that uses GPU classification when available, falling back to CPU-based `BoundaryClassifier.Classify()`. Both `Tick()` and `TickAsync()` now use the GPU path.
- [x] `backend/GeoTime.Core/Engines/BoundaryClassifier.cs` — Retained as CPU fallback (no changes needed).
- [x] `backend/GeoTime.Tests/SplitPhaseTests.cs` — 4 new tests: `S3_GpuBoundaryClassification_MatchesCpuResult` (validates GPU matches CPU output), `S3_GpuBoundaryClassification_NoPlates_NoBoundaries`, `S3_GpuBoundaryClassification_MultiplePlates`, `S3_TectonicEngine_UsesGpuClassification`

## Phase S4 — StratigraphyStack Lock Optimization ✅

**Completed** (this session)

- [x] `backend/GeoTime.Core/Engines/StratigraphyStack.cs` — Replaced single global `Lock` with 256 striped locks (`Lock[] _stripeLocks`) keyed by `cellIndex & 0xFF`. All per-cell methods (`GetLayers`, `GetTopLayer`, `GetTotalThickness`, `PushLayer`, `InitializeBasement`, `ApplyDeformation`, `ErodeTop`) use stripe locks. Backing store changed from `Dictionary` to `ConcurrentDictionary` for thread-safe collection access. `RemapColumns()` uses write-lock pattern: builds new dictionary from stripe-locked reads, then swaps atomically. `Size` and `Clear` use `ConcurrentDictionary` directly.
- [x] `backend/GeoTime.Tests/SplitPhaseTests.cs` — 5 new tests: `S4_ConcurrentPushLayer_DifferentCells`, `S4_ConcurrentPushLayer_SameStripe`, `S4_ConcurrentErodeAndPush`, `S4_ConcurrentApplyDeformation`, `S4_RemapColumns_AtomicSwap`, `S4_RemapColumns_GapFill`

**Test count**: 355 (previous) + 10 new = **365 total backend tests**
