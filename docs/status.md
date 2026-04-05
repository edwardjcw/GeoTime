# GeoTime — docs/status.md

## Instructions for Future Agents

This file is the **first thing to read** when picking up work on GeoTime. It provides context, constraints, and a tracker for what has been done and what remains.

### Key Principles
1. **Backend-first**: All simulation logic lives in C# (`backend/GeoTime.Core`). The TypeScript frontend is display-only. New features that affect planet evolution must be implemented as engines or services in `GeoTime.Core`.
2. **Test as you go**: Every backend change needs xUnit tests (`backend/GeoTime.Tests/`); every frontend change needs Vitest unit tests (`tests/`) and Playwright E2E tests (`e2e/`).
3. **Simulation realism**: If a feature affects how the land develops over time (e.g., rivers shaping terrain, ice caps modulating climate), it must be wired into `SimulationOrchestrator.AdvanceSimulationCore` — not left as a passive label.
4. **Minimal changes**: Change only what is needed. Avoid refactoring unrelated code.
5. **Build and test before committing**: Run `dotnet test` (backend) and `npx vitest run` (frontend) after changes. Use `report_progress` to push commits.

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
- `docs/plan-descriptions.md` — Other future plans
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

### Phase L5 — Frontend: Minimal Label Rendering
- `src/api/backend-client.ts` — `fetchFeatureLabels()` method
- `src/render/label-renderer.ts` — `<div>` overlay pool, lat/lon → screen projection, zoom culling
- `src/ui/app-shell.ts` — "Labels" toggle in layer panel, `#label-layer` container
- `src/main.ts` — Wire `fetchFeatureLabels()` → `labelRenderer.setLabels()` on `PLANET_GENERATED`
- Playwright E2E: after planet generation, label container exists with ≥ 1 visible label
- Vitest unit test: back-hemisphere culling, zoom culling

### Phase L6 — Integration, Snapshot Persistence, SignalR
- `FeatureRegistry` serialised as part of snapshot save/restore
- `SimulationHub.cs` — broadcast `FeaturesUpdated` after each tick
- Frontend merge of delta features on `FeaturesUpdated` event
- Integration test: save/restore preserves feature names; SignalR test for `FeaturesUpdated`
