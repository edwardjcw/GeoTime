# Plan: Feature Description Engine

## Overview

When a user clicks the info icon in the cell inspector panel, a Wikipedia-style modal displays a richly written article about the geographic feature at that location (e.g., the mountain range it's part of, the ocean basin, the tectonic context, the climate, and the biome). The description references other named features (plates, oceans, ranges) by their generated names from the Label System plan.

---

## Architecture Decision: Implementation Options

### Option A — Template Engine *(Recommended for a first implementation)*

Build a pure TypeScript **narrative template engine** that assembles prose from structured simulation data using fill-in-the-blank sentence patterns.

**How it works:**

1. When the user clicks the info icon on the inspect panel, gather all relevant context:
   - The cell's `CellInspection` data (rock type, elevation, temp, precip, biomass, soil)
   - The `DetectedFeature` the cell belongs to (from the `FeatureMap` built in the Label System plan)
   - Nearby features within a radius (nearby plates, ocean/sea, mountain range)
   - The plate the cell is on (from `/api/state/plates`)
   - Adjacent plates (for collision context) from `/api/state/plates` + proximity to boundaries
   - Geological events at this location from `/api/state/events`

2. Build a `DescriptionContext` object:

```typescript
interface DescriptionContext {
  feature: DetectedFeature;         // primary feature (e.g., mountain range)
  cell: CellInspection;
  plate: PlateInfo & { name: string };
  nearestOcean?: DetectedFeature;
  collidingPlate?: PlateInfo & { name: string };
  riftingPlate?: PlateInfo & { name: string };
  nearbyRange?: DetectedFeature;
  nearbySea?: DetectedFeature;
  events: GeoLogEntry[];
  biomeType: string;                // computed from temp/precip
}
```

3. Select the correct **article template** based on feature type:
   - `MOUNTAIN_RANGE_TEMPLATE` — leads with formation (subduction/collision), climate, notable facts
   - `OCEAN_TEMPLATE` — leads with basin size, bordering continents, currents
   - `CONTINENT_TEMPLATE` — leads with plate, biomes, topography
   - `DESERT_TEMPLATE` — leads with aridity cause (rain shadow / subtropical), temperature
   - `RAINFOREST_TEMPLATE` — leads with precipitation source, biodiversity context
   - `ISLAND_TEMPLATE` — leads with volcanic/continental origin, isolation
   - `SEA_TEMPLATE` — leads with enclosed/semi-enclosed geography, salinity context
   - `LAKE_TEMPLATE` — leads with tectonic or glacial origin, depth

4. Each template is a function `(ctx: DescriptionContext) => string[]` (array of paragraph strings). Templates use conditional clauses:
   - If `ctx.collidingPlate` exists → use "formed by the subduction of the X Plate under the Y Plate"
   - If `ctx.nearestOcean` exists → reference warm/cold ocean winds, named sea
   - If recent volcanism in events → mention active volcanism
   - If high precipitation → mention moisture source from adjacent ocean

5. Return a `FeatureDescription` object:

```typescript
interface FeatureDescription {
  title: string;               // e.g., "The Transon Mountains"
  subtitle: string;            // e.g., "Mountain Range · Mamoth Plate"
  paragraphs: string[];        // 2–4 paragraphs of prose
  stats: Array<[string, string]>;  // key-value sidebar facts
  relatedFeatures: string[];   // names of linked features
}
```

**Pros**: No external dependencies, runs in browser, fully deterministic, fast.  
**Cons**: Text can feel formulaic; limited variation.

---

### Option B — Grammar-Based Sentence Generation

Use a **Tracery-style context-free grammar** (or a custom recursive grammar) to generate more varied prose. Sentences are defined as grammar rules with multiple alternatives randomly selected per generation.

**Example grammar fragment:**

```
mountain_range_intro:
  "The {name} {range_word} {formation_verb} from {tectonic_cause}."
  | "{age} {name} forms a {adj} barrier across {continent_name}."

tectonic_cause:
  "the collision of the {plate1} and {plate2}"
  | "a deep subduction zone beneath the {plate1}"
  | "ancient rifting of the {plate1}"
```

Each rule maps to an array of templates with variables. The variables are resolved from the `DescriptionContext`. Multiple alternatives per slot (adjectives, verbs, sentence structures) produce varied text across playthroughs.

**Implementation**: Create `src/geo/grammar-engine.ts` with a `Grammar` class that loads rule tables and resolves a start symbol recursively, seeded by the feature ID for reproducibility.

**Pros**: Much more natural variation; feels less formulaic.  
**Cons**: Grammar authoring is labour-intensive; still requires manual rule writing; grammar must be designed carefully to avoid nonsensical combinations.

---

### Option C — LLM-Powered Descriptions

Use a language model to write the prose paragraph given a structured JSON prompt of the simulation data.

#### C1 — External LLM API (OpenAI / Anthropic)

- Add a `/api/describe` backend endpoint that accepts a `DescriptionRequest` JSON.
- Backend assembles a system prompt ("You are a geographer writing encyclopedia articles about a fictional alien planet...") and a user message containing the structured feature data.
- Calls OpenAI API (or Anthropic) with `gpt-4o-mini` or `claude-haiku` (fast, cheap models).
- Returns the generated text to the frontend.
- Add `OPENAI_API_KEY` (or equivalent) as an environment variable / config option in `backend/GeoTime.Api/appsettings.json`.
- **Pros**: Highest quality, best variety, handles all edge cases naturally.
- **Cons**: Requires internet + API key; adds latency (~1–2 s); not free; requires server-side secret management.

#### C2 — Local Small LLM via llama.cpp / Ollama

- Add a side-car process (or use the `backend/GeoTime.Api` process) that loads a small GGUF model (e.g., Phi-3 mini 3.8B, ~2 GB RAM) via `llama.cpp` bindings for .NET (`LLamaSharp` NuGet package).
- Add `/api/describe` endpoint that runs inference locally.
- Use a structured prompt with `<INST>` tags appropriate for the model.
- **Pros**: Fully offline, no API key, still natural prose.
- **Cons**: Requires ~2–4 GB model download; adds startup time; inference is CPU-bound (~5–10 s without GPU).

#### C3 — Hybrid: Template + LLM Rewrite

- Run the template engine (Option A) first to get a factually correct skeleton text.
- Pass the skeleton + data to the LLM with instructions to "rewrite in flowing encyclopedic prose, preserving all facts".
- **Pros**: Guarantees factual accuracy; LLM only polishes; skeleton serves as fallback on LLM failure.
- **Cons**: Two-step process; slightly more complex.

**Recommendation**: Start with Option A (template engine) for an immediately working implementation. Add Option C1 (OpenAI) behind a feature flag / API key toggle so that if a key is configured, the app uses LLM-enhanced descriptions; if not, templates are used.

---

## Implementation Plan (Option A + C1 Hybrid)

### Step 1 — Description Context Builder (`src/geo/description-context-builder.ts`)

Create a `DescriptionContextBuilder` class that:

- Takes the `FeatureMap` (from the Label System plan) and the current `CellInspection`.
- Finds which `DetectedFeature` the cell belongs to (by cell index membership or nearest centroid).
- Fetches adjacent plate data (plate info + boundary type) — use data already fetched for the layer system.
- Identifies "neighboring" features within a geographic radius (e.g., 2000 km) from the feature centroid.
- Identifies recent geological events within 500 km of the cell (from event log).
- Assembles and returns a `DescriptionContext`.

---

### Step 2 — Template Engine (`src/geo/description-templates.ts`)

Implement one template function per `FeatureType`. Each template function receives `DescriptionContext` and returns `FeatureDescription`.

Cover the required feature types: mountain range, ocean, sea, continent, island, lake, desert, rainforest, plate. Each template produces 2–4 prose paragraphs and a sidebar stats table. Templates reference other features by their generated names.

---

### Step 3 — UI: Description Modal (`src/ui/app-shell.ts`)

- Add a small info icon (`ℹ`) button to the inspect panel header (next to the close ✕ button).
- When clicked, show a `#description-modal` overlay with:
  - **Header**: feature name + type badge
  - **Body**: 2–4 paragraph prose, styled like a compact encyclopedia article
  - **Sidebar**: key-value stats table (elevation range, area, temperature, precipitation, rock type, dominant soil, biomass)
  - **Footer**: "Related features" links (clickable names that pan the camera to that feature's centroid)
  - Loading spinner while description is being generated (for LLM mode)
- Close button (✕) and click-outside-to-dismiss behavior.
- Styling consistent with existing dark-theme panels (`rgba(10,10,14,0.95)`, white text, bordered).

---

### Step 4 — Backend: `/api/describe` Endpoint (optional LLM path)

Add to `backend/GeoTime.Api/Program.cs`:

```
POST /api/describe
Request:  DescriptionRequest  { featureType, featureName, plateName?, nearOceanName?,
                                 collidingPlateName?, elevation, temperature,
                                 precipitation, biome, rockType, ... }
Response: DescriptionResponse { title, paragraphs: string[], stats: object }
```

The endpoint:

1. Checks if `OPENAI_API_KEY` is configured in the environment.
2. If yes: calls OpenAI chat completions with a carefully crafted system prompt establishing the fictional geographer persona and instructing it to write a 3-paragraph encyclopedia article using all provided data fields.
3. If no: falls back to template engine logic (or returns an empty response so the frontend uses its own templates).

Add `Microsoft.Extensions.AI` or `Azure.AI.OpenAI` NuGet package for the LLM call. Add `OPENAI_API_KEY` to `appsettings.Development.json` (gitignored) with a comment in `appsettings.json` documenting the key name.

---

### Step 5 — Frontend Wiring (`main.ts`)

- On inspect panel info-icon click: call `descriptionContextBuilder.build(cellInspection)` → call template engine locally → show modal immediately.
- Simultaneously (if LLM enabled): POST to `/api/describe` → when response arrives, update the modal body with enhanced prose (replaces template text).
- If LLM request takes > 3 s, the template text is already visible and the user is not blocked.

---

### Step 6 — Tests

- Unit tests for `DescriptionContextBuilder`: verify correct feature assignment for cells inside a continent, ocean, mountain range.
- Unit tests for each template function: verify output contains feature name, plate name, expected biome words.
- Unit test for `/api/describe` endpoint with mocked OpenAI client (verify fallback to template when key missing).
- Playwright E2E: after inspecting a cell, click info icon, verify modal appears with non-empty title and at least 2 paragraphs.

---

## File Change Summary

| File | Change |
|------|--------|
| `src/geo/description-context-builder.ts` | New file |
| `src/geo/description-templates.ts` | New file (all feature type templates) |
| `src/shared/types.ts` | Add `DescriptionContext`, `FeatureDescription` interfaces |
| `src/ui/app-shell.ts` | Add info icon to inspect panel; add description modal HTML + CSS |
| `main.ts` | Wire info-icon click → context builder → template engine → modal |
| `backend/GeoTime.Api/Program.cs` | Add `POST /api/describe` endpoint |
| `backend/GeoTime.Core/Models/DescriptionModels.cs` | New file: `DescriptionRequest`, `DescriptionResponse` |
| `backend/GeoTime.Api/DescriptionService.cs` | New file: LLM call + fallback logic |
| `backend/GeoTime.Api/appsettings.json` | Document `OPENAI_API_KEY` config key |
| `tests/description-context-builder.test.ts` | New tests |
| `tests/description-templates.test.ts` | New tests |
| `backend/GeoTime.Tests/DescriptionTests.cs` | New xUnit tests |
| `e2e/app-shell.spec.ts` | Add description modal E2E test |

---

## Dependency Check

- **Template-only path**: Zero new dependencies.
- **Grammar path**: Can be implemented from scratch with no added libraries.
- **LLM API path**: Add `Azure.AI.OpenAI` (or `OpenAI`) NuGet package to `GeoTime.Api`. Check advisory database before adding.
- **Local LLM path**: Add `LLamaSharp` NuGet package and a GGUF model file (downloaded separately, not committed to the repository).
