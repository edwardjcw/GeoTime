# Plan: Planet Label System

## Overview

Add procedurally-generated, seed-reproducible geographic labels to the globe for: tectonic plates, oceans, seas, continents, islands, mountain ranges, lakes, deserts, rainforests, and other notable features.

---

## Step 1 — Name Generator (`src/proc/name-generator.ts`)

Create a standalone procedural name generator seeded by the existing planet PRNG (`src/proc/prng.ts`).

- Implement a **syllable-assembly engine**: define a phoneme bank of onset consonants, vowels, and codas, plus rules for combining them into 1–4 syllable words. Examples: `Transon`, `Orle`, `Mamoth`, `Grander`.
- Implement named-category generators with distinct phoneme weights per category type:
  - `oceanName()` — longer, flowing names (e.g., "Santa Ocean", "Velundra Sea")
  - `continentName()` — short, solid (e.g., "Soreth", "Ulvan")
  - `rangeName()` — compound of a name + descriptor ("Transon Mountains", "Kolspar Ridge")
  - `plateName()` — proper-noun style (e.g., "Mamoth Plate", "Orle Plate")
  - `desertName()`, `lakeName()`, `islandName()`, `rainforestName()`
- All names generated deterministically from `seed + featureType + featureIndex`.
- Export a `NameGenerator` class that wraps the PRNG and exposes typed generation methods.
- Unit tests: verify determinism (same seed → same name), uniqueness within a run (no duplicate names in first 100), and that names are pronounceable (simple heuristic checks).

---

## Step 2 — Feature Detection Engine (`src/geo/feature-detector.ts`)

Create a client-side geographic feature detector that runs once after a planet is generated (after the `PLANET_GENERATED` event fires). It consumes the height map, plate map, temperature map, and precipitation map.

**Algorithm per feature type:**

- **Plates**: Read directly from `/api/state/plates` (already has center lat/lon + `isOceanic`). Split into ocean plates vs. land plates.
- **Continents & Oceans**: Run a **connected-component flood fill** on the 512×512 height map. Cells with `height >= 0` form land components; cells with `height < 0` form ocean components. Filter by area:
  - Land area ≥ 500,000 km² equivalent → continent
  - Land area < 500,000 km² → island
  - Ocean area ≥ 10M km² equivalent → ocean
  - Ocean area < 10M km² (and touching a larger ocean) → sea
- **Mountain Ranges**: On land cells, identify clusters where `height > 2000 m` using connected-component with a low connectivity threshold. Compute bounding-box centroid and longest axis for label placement.
- **Lakes**: Identify enclosed water bodies (ocean flood-fill does not reach them) above elevation 0 — these are inland water. Keep components ≥ 5 cells.
- **Deserts**: Identify contiguous land cells where `precipitation < 250 mm/yr` and `temperature > 15 °C`. Merge nearby clusters into named regions.
- **Rainforests**: Contiguous land cells where `precipitation > 1500 mm/yr` and `temperature > 20 °C`.

**Output**: A `FeatureMap` object:

```typescript
interface DetectedFeature {
  id: string;                  // stable, seed-derived
  type: FeatureType;           // enum: CONTINENT, OCEAN, SEA, ISLAND, ...
  name: string;                // generated name
  centerLat: number;
  centerLon: number;
  areaCells: number;
  associatedPlateIds: number[]; // which plates overlap this feature
}
```

Store the `FeatureMap` on the frontend (in memory, re-computed on new planet). Emit a new event `FEATURES_DETECTED` on the event bus with the full `FeatureMap` payload. Add the `FEATURES_DETECTED` event type and payload to `src/shared/types.ts`.

---

## Step 3 — Label Rendering System (`src/render/label-renderer.ts`)

Create a `LabelRenderer` class that manages HTML div overlays positioned over the Three.js canvas.

**Approach**: Use HTML `<div>` overlays projected onto the globe using Three.js's `Vector3.project()` to convert 3D world coordinates to 2D screen coordinates. This is simpler and more performant than 3D text geometry, and enables CSS styling.

**Implementation details:**

- On `FEATURES_DETECTED`, create a pool of `<div>` elements (one per feature) appended to a dedicated `#label-layer` container div that sits absolutely positioned over the canvas.
- Per-frame update loop (called from `GlobeRenderer.update()`): convert each feature's lat/lon → 3D sphere point → project to NDC → CSS `left`/`top` position. Hide labels whose projected `z > 1` (behind the globe).
- **Visibility culling by zoom**:
  - Camera distance > 3.0 → show only oceans, continents, plates
  - Distance 1.5–3.0 → also show seas, mountain ranges, deserts, rainforests
  - Distance 1.1–1.5 → also show islands, lakes
  - Distance < 1.1 (first-person) → hide all labels (too close)
- **Font size scaling**: scale CSS `font-size` inversely with camera distance, clamped between 8 px and 18 px.
- **Overlap prevention**: simple greedy suppression — when two labels overlap (compare bounding rects), hide the smaller feature's label.
- **Styling**: ocean/sea labels in italic blue-white, continents in white bold, mountain ranges in light grey, deserts in sandy yellow, rainforests in green, plates in translucent italic.
- **Occlusion**: labels on the back hemisphere are hidden (already handled by `z > 1` check).
- Wire `LabelRenderer` into `GlobeRenderer`: add a `setLabelContainerElement(el)` method; update positions in the existing `render()` call.
- Add a `setLabelsVisible(visible: boolean)` method for the layer toggle.

---

## Step 4 — Layer Toggle Integration (`main.ts`, `app-shell.ts`)

- Add a **"Labels" toggle** to the existing layer toggles list in `app-shell.ts`. Use the `LABEL_TOGGLE` event type (already defined in the event bus types).
- In `main.ts`, listen to the layer toggle for `'labels'` and call `renderer.setLabelsVisible()`.
- On `PLANET_GENERATED`: run `featureDetector.detect(heightMap, plateMap, tempMap, precipMap)` → emit `FEATURES_DETECTED` → `labelRenderer.setFeatures(features)`.
- Labels default to **visible** when a planet is first generated.

---

## Step 5 — Backend: Persist Feature Names (optional enhancement)

If the feature map needs to survive page refresh (same planet, same names):

- Add a `FeatureRegistry` class to `GeoTime.Core` that stores `DetectedFeature[]` in `SimulationState`.
- Add a `/api/state/features` GET endpoint returning the feature list as JSON.
- Add a POST `/api/state/features` or run detection server-side in C# after `GeneratePlanet` (using the same deterministic seed).
- This is optional for a first implementation; client-side detection is sufficient initially.

---

## Step 6 — Tests

- Unit tests for `NameGenerator`: determinism, uniqueness, category-specific patterns.
- Unit tests for `FeatureDetector`: on a small synthetic height map, verify continents, oceans, and mountain ranges are detected with correct counts and centroids.
- Unit tests for `LabelRenderer`: verify `project()` hides back-hemisphere labels and that zoom-level culling returns the correct subset of features.
- Playwright E2E: after generating a planet, verify the labels container exists in DOM and has at least one visible label.

---

## File Change Summary

| File | Change |
|------|--------|
| `src/proc/name-generator.ts` | New file |
| `src/geo/feature-detector.ts` | New file |
| `src/render/label-renderer.ts` | New file |
| `src/shared/types.ts` | Add `FeatureType`, `DetectedFeature`, `FEATURES_DETECTED` event |
| `src/render/globe-renderer.ts` | Wire `LabelRenderer`, add `setLabelsVisible()` |
| `src/ui/app-shell.ts` | Add "Labels" layer toggle button and `#label-layer` container div |
| `main.ts` | Wire feature detection pipeline, layer toggle listener |
| `tests/name-generator.test.ts` | New tests |
| `tests/feature-detector.test.ts` | New tests |
| `tests/label-renderer.test.ts` | New tests |
| `e2e/app-shell.spec.ts` | Add label visibility E2E test |
