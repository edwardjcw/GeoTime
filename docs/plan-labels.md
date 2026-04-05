# Plan: Planet Label System

## Overview

Add a fully **backend-driven** geographic feature registry to GeoTime. The C# backend detects, names, tracks, and evolves every significant feature on the planet across geological time. The frontend is kept minimal — it only fetches label metadata from the API and renders `<div>` overlays on the globe. The feature set is far broader than simple biome regions: it includes tectonic plates, continents, oceans, seas, islands, mountain ranges, river systems, lakes, inland seas, deserts, rainforests, polar ice caps, ocean current systems, atmospheric circulation zones (jet streams, ITCZ, monsoon belts, hurricane corridors), hotspot chains, subduction zones, rift valleys, and impact basins. Every feature tracks its full temporal history so that the planet's geography can be narrated from its earliest formation to the present simulation tick.

All feature detection and naming runs **server-side** in `GeoTime.Core`. The frontend receives a compact JSON payload of `{id, name, type, centerLat, centerLon, zoomLevel}` tuples from a single REST endpoint and renders them as labels.

---

## Phase L1 — Backend: Core Data Models and Name Generator

**Goal**: Establish the data contracts and deterministic naming infrastructure that every subsequent phase depends on.

### 1.1 — Feature Data Models (`backend/GeoTime.Core/Models/FeatureModels.cs`)

Define the complete model hierarchy:

```csharp
public enum FeatureType
{
    TectonicPlate, Continent, Ocean, Sea, Island, IslandChain,
    MountainRange, MountainPeak, Rift, SubductionZone, HotspotChain, ImpactBasin,
    River, RiverDelta, Lake, InlandSea,
    Desert, Rainforest, Savanna, Tundra, PolarIceCap,
    OceanCurrentSystem, JetStream, MonsonBelt, HurricaneCorridor, ITCZ
}

public enum FeatureStatus { Nascent, Active, Waning, Extinct, Submerged, Exposed }

public record FeatureSnapshot(
    long   SimTickCreated,
    long   SimTickExtinct,    // long.MaxValue if still active
    string Name,              // name at this point in history
    float  CenterLat,
    float  CenterLon,
    float  AreaKm2,
    FeatureStatus Status,
    string? ParentFeatureId,  // e.g., river belongs to a continent
    string? MergedIntoId,     // if this feature merged with another
    string? SplitFromId       // if this feature split from another
);

public class DetectedFeature
{
    public string   Id               { get; init; }  // stable GUID, seed-derived
    public FeatureType Type          { get; init; }
    public List<FeatureSnapshot> History { get; init; } = new();
    public FeatureSnapshot Current   => History.Last();
    public List<string> AssociatedPlateIds { get; init; } = new();
    public List<int>    CellIndices   { get; init; } = new(); // grid cells in feature
    public Dictionary<string, float> Metrics { get; init; } = new();
    // e.g., "max_elevation_m", "mean_precip_mm", "river_length_km", "discharge_m3s"
}

public class FeatureRegistry
{
    public Dictionary<string, DetectedFeature> Features { get; init; } = new();
    public long LastUpdatedTick { get; set; }
}
```

Add `FeatureRegistry FeatureRegistry` to `SimulationState`.

### 1.2 — Name Generator (`backend/GeoTime.Core/Services/FeatureNameGenerator.cs`)

Implement a deterministic syllable-assembly engine seeded by `(planetSeed, featureType, featureIndex)`:

- Define phoneme banks with category-specific weights: oceanic names are longer and flowing; continental names are short and consonant-heavy; mountain names use hard stops; river names use liquids (l, r) and fricatives.
- Support compound names: `"{Root} {Descriptor}"` — e.g., "Velundra Sea", "Transon Mountains", "Kolspar Ridge", "River Maran".
- Support **name evolution**: given an existing name and a change reason (`SPLIT`, `MERGE`, `CLIMATE_SHIFT`, `RENAME_BY_AGE`), generate a linguistically plausible evolved form (e.g., "Soreth" → "Greater Soreth" on expansion; "Ulvan–Soreth" on merge; "Ancient Soreth" after extinction).
- Unit tests: determinism, uniqueness across 200 names per type, category-appropriate phoneme patterns.

### 1.3 — Phase L1 Backend Tests (`backend/GeoTime.Tests/FeatureModelTests.cs`)

- Verify `FeatureRegistry` serialises/deserialises cleanly with `SimulationState`.
- Verify `FeatureNameGenerator` is deterministic and produces category-appropriate output.

---

## Phase L2 — Backend: Primary Feature Detection

**Goal**: Detect the most important geographic features after planet generation and on each simulation tick, tracking changes over time.

### 2.1 — Feature Detector Service (`backend/GeoTime.Core/Services/FeatureDetectorService.cs`)

The `FeatureDetectorService` runs on the `SimulationState` grid (512 × 512 equirectangular cells) and updates `FeatureRegistry` in place.

**Detection algorithms:**

#### Tectonic Plates
Read directly from `SimulationState.PlateMap`. Classify each plate as `OceanicPlate` or `ContinentalPlate` based on mean cell elevation. Compute centroid and area. Detect **plate boundaries**: for every cell adjacent to a differently-platted cell, classify the boundary as convergent (approaching velocity vectors → subduction zone or collision orogen), divergent (receding → rift zone), or transform (parallel motion).

#### Continents and Oceans
Flood-fill on elevation: `height ≥ -200 m` and connected to other land forms a land component; `height < -200 m` forms an ocean component. Threshold ocean components: ≥ 50 M km² → Ocean, 2–50 M km² → Sea, < 2 M km² → Marginal Sea or Bay. Land components: ≥ 1 M km² → Continent, 10 k–1 M km² → Large Island, < 10 k km² → Island.

#### Mountain Ranges
Land cells with `height > 1500 m`. Cluster using 8-connected flood fill. Compute:
- Bounding-box major axis length → range name uses "Mountains" (long) or "Ridge" (short).
- Mean and maximum elevation.
- Orographic classification: windward vs. leeward side from prevailing wind direction (use zonal wind from `ClimateEngine`).
- **Rain-shadow detection**: compare precipitation on the windward vs. leeward flanks; flag as `RAIN_SHADOW_SOURCE` if delta > 500 mm/yr.
- Fold-belt vs. volcanic-arc origin: volcanic arc if within 200 km of a subduction boundary, fold belt if at a convergent plate boundary without active volcanism.

#### Subduction Zones and Rifts
From the plate-boundary classification computed above:
- Convergent boundaries with one oceanic plate → subduction zone; compute dip direction and trench depth.
- Divergent boundaries → rift valley; compute rift width and spreading rate.
- Convergent boundaries with two continental plates → collision orogen.

#### Hotspot Chains
From `SimulationState` hotspot list (already tracked in Phase 7+): group hotspots by proximity, order by age, fit a direction vector → island-chain feature with length and propagation azimuth.

#### Impact Basins
Read from geological event log. Impact events create a circular `ImpactBasin` feature with a computed diameter (from impactor energy in the event record), a rim-height profile, and a central uplift. Basin fill history tracks subsequent sedimentation.

### 2.2 — API Endpoint (`backend/GeoTime.Api/Program.cs`)

Add:
```
GET /api/state/features
```
Returns the full `FeatureRegistry` as JSON. Optionally accept a `?tick=N` query parameter to return the feature state as of a historical tick (using `FeatureSnapshot.SimTickExtinct`).

```
GET /api/state/features/{id}
```
Returns the full `DetectedFeature` for a single feature including its entire `History` array.

### 2.3 — Phase L2 Tests (`backend/GeoTime.Tests/FeatureDetectorTests.cs`)

- Synthetic 32×32 height map with a known continent shape: verify correct continent/ocean split, area calculation, and centroid.
- Synthetic plate map with convergent boundary: verify subduction zone is detected on the correct side.
- Known mountain cluster: verify rain-shadow flag when precipitation gradient exceeds threshold.

---

## Phase L3 — Backend: Hydrological and Atmospheric Feature Detection

**Goal**: Detect river systems, lake networks, and atmospheric circulation features. These require additional passes over the climate data and a flow-routing algorithm.

### 3.1 — River System Detection (`backend/GeoTime.Core/Services/HydroDetector.cs`)

Rivers are the drainage pathways that connect mountain ranges to oceans. Use a **D8 flow-routing** algorithm (standard in computational hydrology) on the elevation grid:

- Compute flow direction for every land cell: water flows to the lowest adjacent neighbour (8-connectivity).
- Compute flow accumulation: propagate counts upstream → downstream. Cells with accumulation above a threshold (tunable, default ≈ 500 uphill cells) form the river network.
- Trace the main stem of each river from source to outlet. Extract:
  - Total length (sum of cell arc lengths using spherical geometry)
  - Discharge proxy: flow accumulation × mean precipitation in the catchment
  - Gradient profile: source elevation, knickpoints, gradient changes
  - Delta type at the outlet: fan delta (steep gradient to sea), bird-foot delta (low gradient, high sediment load), estuarine (tidal zone, low gradient)
  - Tributary network: identify major tributaries ≥ 10% of main-stem length
- Classify rivers: braided (high gradient, coarse sediment), meandering (low gradient, fine sediment), ephemeral (desert, seasonal).
- Assign names; the main stem gets a primary name ("River Maran"), major tributaries get compound names ("North Maran", "Upper Maran").
- River mouths open into named oceans/seas and are cross-referenced to the `DetectedFeature` for that ocean.

### 3.2 — Lake and Inland Sea Detection

Using the river flow network:
- Identify **endorheic basins** (no outlet to sea): flood-fill from flow sinks that are inland. If area ≥ 1000 km² → named lake; ≥ 100 k km² → inland sea.
- Compute salinity proxy: high evaporation / low precipitation ratio → saline lake (like the Dead Sea); low ratio → freshwater.
- Classify tectonic origin: graben lake (at a rift), volcanic caldera lake, glacial kettle lake (if polar region), or fluvial oxbow.

### 3.3 — Atmospheric Circulation Zone Detection

Using `ClimateEngine` temperature and wind output:
- **ITCZ**: identify the latitude band where surface convergence is maximal (highest precipitation, lowest pressure gradient) — typically a narrow band near the equator.
- **Jet Streams**: identify upper-level wind maxima at mid-latitudes using the zonal wind component; track polar and subtropical jet positions.
- **Monsoon Belts**: identify regions with > 500 mm/yr seasonal precipitation asymmetry between summer and winter hemispheres, caused by land–sea thermal contrast.
- **Hurricane Corridors**: identify ocean areas with sea-surface temperature > 26 °C at latitudes 5°–20° where Coriolis is non-zero; these are potential genesis zones. Track corridor area and mean SST.
- **Ocean Gyres and Current Systems**: identify large closed circulation loops in the ocean velocity field; classify as subtropical gyre, subpolar gyre, or boundary current (western intensified or eastern boundary).

### 3.4 — Phase L3 Tests

- D8 flow-routing on a synthetic small grid with a known valley → verify main-stem path, length, and outlet cell.
- Delta type classification with contrasting gradient profiles.
- ITCZ band detection with synthetic zonal precipitation array.

---

## Phase L4 — Backend: Temporal History and Name Evolution

**Goal**: Track feature changes tick-by-tick so that every feature has a complete geological biography, and names evolve appropriately as features form, grow, split, merge, and become extinct.

### 4.1 — FeatureEvolutionTracker (`backend/GeoTime.Core/Services/FeatureEvolutionTracker.cs`)

Called at the end of every `AdvanceSimulation` tick, after all engines have run. Compares the newly detected `FeatureRegistry` snapshot against the previous one:

**Change events detected:**

| Event | Trigger | Action |
|-------|---------|--------|
| `FEATURE_BORN` | New connected component appears | Create new `DetectedFeature` with initial `FeatureSnapshot` |
| `FEATURE_EXTINCT` | Component disappears | Close current snapshot with `SimTickExtinct = currentTick` |
| `FEATURE_SPLIT` | One component splits into two | Close parent; create two children with `SplitFromId`; evolve names |
| `FEATURE_MERGE` | Two components merge | Close both; create new feature with `MergedIntoId` refs; merge names |
| `AREA_SHIFT_MAJOR` | Area changes > 20% | Add new snapshot with updated area, same name |
| `CLIMATE_RECLASSIFY` | Biome type changes (e.g., desert becomes savanna) | Add snapshot, optionally rename |
| `SUBMERGENCE` | Continental region drops below sea level (isostasy) | Status → `Submerged`; add snapshot |
| `EXPOSURE` | Oceanic region rises above sea level | Status → `Exposed`; split from parent ocean |
| `PLATE_TRANSFER` | Feature crosses a plate boundary over time | Update `AssociatedPlateIds` in new snapshot |

**Name evolution rules:**
- On `SPLIT`: child A keeps the parent name; child B gets a directional prefix ("North", "Lesser", "New") + parent root.
- On `MERGE`: combined name is a portmanteau or compound of both parents, seeded by feature IDs.
- On `SUBMERGENCE` → `EXPOSURE` after long dormancy: new name is generated fresh (ancient features resurface with new identities).
- River rerouting after a capture event: main river name follows the longer post-capture stem; the captured tributary is renamed.
- On age milestones (every 500 simulation ticks): features may acquire an honorific suffix ("Ancient", "Deep", "Great") if they have been continuously active.

### 4.2 — Historical Snapshot API

```
GET /api/state/features?tick=N
```
Already defined in Phase L2; the evolution tracker ensures that every `FeatureSnapshot` has correct `SimTickCreated` / `SimTickExtinct` boundaries, enabling correct time-sliced queries.

```
GET /api/state/features/{id}/history
```
Returns the full ordered `List<FeatureSnapshot>` for a feature, enabling a complete time-lapse biography.

### 4.3 — Phase L4 Tests

- Simulate a continent split: verify two child features created, names diverged, parent closed at correct tick.
- Simulate a river capture: verify main-stem name follows the longer path.
- Simulate deep-time re-exposure: verify the resurfaced feature gets a fresh name.

---

## Phase L5 — Frontend: Minimal Label Rendering

**Goal**: The frontend does the minimum needed to display labels. All intelligence lives in the backend.

### 5.1 — Backend Client Extension (`src/api/backend-client.ts`)

Add a single new method:
```typescript
async fetchFeatureLabels(): Promise<FeatureLabel[]>
// GET /api/state/features returns:
// [{ id, name, type, centerLat, centerLon, zoomLevel, status }]
// zoomLevel is the minimum camera distance at which this label should appear
// (computed by the backend based on feature area)
```

The backend computes `zoomLevel` from feature area so the frontend needs zero logic for this.

### 5.2 — Label Renderer (`src/render/label-renderer.ts`)

A thin `LabelRenderer` class:
- Maintains a pool of `<div>` elements inside `#label-layer`.
- Per-frame: convert `centerLat`/`centerLon` → 3D sphere point → `Vector3.project()` → CSS `left`/`top`. Hide any label where `z > 1` (back hemisphere) or `cameraDistance > label.zoomLevel`.
- Applies CSS class by `type` (e.g., `.label-ocean`, `.label-mountain-range`). All styling is in CSS; the TypeScript is pure positioning logic.
- Exposes `setVisible(visible: boolean)`.

### 5.3 — Layer Toggle (`src/ui/app-shell.ts`, `src/main.ts`)

- Add a "Labels" toggle checkbox to the existing layer panel.
- On `PLANET_GENERATED`: call `fetchFeatureLabels()` and pass result to `labelRenderer.setLabels(labels)`.
- On layer toggle: call `labelRenderer.setVisible()`.

### 5.4 — Phase L5 Tests

- Playwright E2E: after planet generation, labels container exists in DOM with ≥ 1 visible label.
- Unit test for `LabelRenderer`: back-hemisphere labels hidden, zoom culling works with mocked camera distance.

---

## Phase L6 — Integration, Snapshot Persistence, and Polish

**Goal**: Ensure feature registry survives save/load, integrates cleanly with the snapshot system, and is wired into SignalR tick notifications.

### 6.1 — Snapshot Integration

- Serialize `FeatureRegistry` as part of `/api/snapshots/save` (JSON or MessagePack).
- Restore on `/api/snapshots/load`: deserialize and set `SimulationState.FeatureRegistry`.
- On load, re-emit `FeatureRegistry` to connected SignalR clients so label overlays refresh.

### 6.2 — SignalR Tick Notification

- In `SimulationHub.cs`, after each `AdvanceSimulation`, broadcast a `FeaturesUpdated` message with only the **changed** features (new snapshots added this tick). This avoids sending the full registry on every tick.
- Frontend subscribes: on `FeaturesUpdated`, merge changes into local label cache and refresh rendered labels.

### 6.3 — Phase L6 Tests

- Integration test: save state, restore state, verify feature names are preserved.
- SignalR test: advance one tick, verify `FeaturesUpdated` message contains correct changed feature IDs.

---

## File Change Summary

| File | Change |
|------|--------|
| `backend/GeoTime.Core/Models/FeatureModels.cs` | New — all data model types |
| `backend/GeoTime.Core/Services/FeatureNameGenerator.cs` | New — syllable-assembly name engine |
| `backend/GeoTime.Core/Services/FeatureDetectorService.cs` | New — primary feature detection |
| `backend/GeoTime.Core/Services/HydroDetector.cs` | New — D8 river routing, lake, ITCZ |
| `backend/GeoTime.Core/Services/FeatureEvolutionTracker.cs` | New — tick-by-tick history tracking |
| `backend/GeoTime.Core/Models/SimulationModels.cs` | Add `FeatureRegistry` to `SimulationState` |
| `backend/GeoTime.Core/SimulationOrchestrator.cs` | Call `FeatureDetectorService` + `FeatureEvolutionTracker` after each tick |
| `backend/GeoTime.Api/Program.cs` | Add `GET /api/state/features`, `GET /api/state/features/{id}`, `GET /api/state/features/{id}/history` |
| `backend/GeoTime.Api/SimulationHub.cs` | Broadcast `FeaturesUpdated` after tick |
| `backend/GeoTime.Tests/FeatureModelTests.cs` | New — model serialisation tests |
| `backend/GeoTime.Tests/FeatureDetectorTests.cs` | New — detection algorithm tests |
| `backend/GeoTime.Tests/HydroDetectorTests.cs` | New — D8 flow routing tests |
| `backend/GeoTime.Tests/FeatureEvolutionTests.cs` | New — split/merge/rename tests |
| `src/api/backend-client.ts` | Add `fetchFeatureLabels()` |
| `src/render/label-renderer.ts` | New — minimal `<div>` label overlay |
| `src/ui/app-shell.ts` | Add "Labels" toggle + `#label-layer` container |
| `src/main.ts` | Wire `fetchFeatureLabels()` → `labelRenderer.setLabels()` on planet generation |
| `e2e/app-shell.spec.ts` | Add label visibility E2E test |
