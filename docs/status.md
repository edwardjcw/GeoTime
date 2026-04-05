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

### Phase L3 — Hydrological & Atmospheric Feature Detection
- Rivers via D8 flow routing (affects terrain erosion → wire into simulation, not just a label)
- Lakes, inland seas (endorheic basins)
- ITCZ, jet streams, monsoon belts, hurricane corridors
- Tests: D8 flow-routing on synthetic grid, ITCZ band detection
- **Note**: Rivers should affect terrain via enhanced erosion in river channels — integrate with `SurfaceEngine`/`ErosionEngine`

### Phase L4 — Temporal History and Name Evolution
- `FeatureEvolutionTracker.cs` — tick-by-tick diff (SPLIT, MERGE, AREA_SHIFT, SUBMERGENCE, etc.)
- `GET /api/state/features?tick=N` — already structurally supported in L2
- `GET /api/state/features/{id}/history` endpoint
- Tests: continent split scenario, river capture, deep-time re-exposure

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
