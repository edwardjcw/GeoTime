# Plan: Feature Description Engine

## Overview

When a user inspects a cell, the backend assembles a **deep, expert-level geological narrative** about every feature at that location: the tectonic setting, rock formation history, erosion and sediment transport, river systems, watershed dynamics, climate drivers, biome cascades, stratigraphic column, and the full temporal biography of the feature from the planet's earliest tick to the present. The frontend is kept minimal: it only sends a `POST /api/describe` request and renders the returned HTML/text in a modal.

All intelligence — feature context assembly, geological synthesis, and prose generation — runs **server-side in C#**. The LLM layer is pluggable: the user configures a preferred provider (Gemini, OpenAI, Anthropic, Ollama, local GGUF via LlamaSharp) in `appsettings.json`. If no provider is configured, the engine falls back to a rich template-based description.

The description integrates **time-dependent history** (e.g., "this cell was once part of the supercontinent Velundra before the Great Rift at tick 1200 separated the Soreth and Ulvan plates") and covers **extraordinary geological events** captured in the stratigraphic column (impact ejecta layers, volcanic soot horizons, gamma-ray burst isotope anomalies, ocean anoxic event marker beds).

---

## Phase D1 — Comprehensive Geological Data Model

**Goal**: Extend `SimulationState` and `CellInspection` to carry the full geological context that a description engine needs. Without richer data, descriptions cannot go beyond surface-level observations.

### 1.1 — Stratigraphic Column Model (`backend/GeoTime.Core/Models/StratigraphyModels.cs`)

Each cell already has a rock-type stack. Extend it with:

```csharp
public enum LayerEventType
{
    Normal,           // standard sedimentation or volcanic deposition
    ImpactEjecta,     // distal ejecta blanket from a bolide impact
    VolcanicAsh,      // tephra horizon from a major eruption or flood basalt
    VolcanicSoot,     // carbon-rich horizon from an extinction-scale eruption
    GammaRayBurst,    // cosmogenic isotope spike (10Be, 26Al) from a near-field GRB
    OceanAnoxicEvent, // black shale, pyrite framboids — ocean oxygen depletion
    SnowballGlacial,  // diamictite horizon from global glaciation
    IronFormation,    // banded iron formation from atmospheric oxygen rise
    MeteoriticIron,   // siderophile-enriched layer from cosmic dust flux anomaly
    MassExtinction,   // composite geochemical anomaly layer
    CarbonIsotopeExcursion  // δ13C shift from carbon cycle perturbation
}

public record StratigraphicLayer(
    long   SimTickDeposited,
    float  ThicknessM,
    RockType RockType,
    LayerEventType EventType,
    string? EventId,       // links to GeoLogEntry that caused this layer
    float  IsotopeAnomaly, // fractional deviation from background (0 = normal)
    float  OrganicCarbonFraction,
    float  SootConcentrationPpm,
    bool   IsGlobal        // true if this horizon is planet-wide (e.g., GRB, mega-impact)
);

public class StratigraphicColumn
{
    public List<StratigraphicLayer> Layers { get; init; } = new();
    // Layers ordered oldest-first; index 0 = basement
    public StratigraphicLayer Surface => Layers.Last();
}
```

Add `StratigraphicColumn[]` to `SimulationState` (one per grid cell, populated by existing engines and new event-deposition logic). Each engine that deposits material appends to the column rather than overwriting.

### 1.2 — Extraordinary Event Deposition (`backend/GeoTime.Core/Engines/EventDepositionEngine.cs`)

A new engine called at the end of each tick's event processing. For each `GeoLogEntry` generated during that tick:

| Event Type | Deposition Rule |
|------------|-----------------|
| Bolide impact | Deposit `ImpactEjecta` layer in all cells within `ejectaRadiusKm`; global thin layer everywhere else. Ejecta thickness follows `1/r²` falloff from impact site. |
| Major volcanic eruption (VEI ≥ 7) | Deposit `VolcanicAsh` layer downwind (use zonal wind direction from `ClimateEngine`). Very large eruptions (flood basalts, VEI ≥ 8) also deposit a `VolcanicSoot` horizon globally. |
| Gamma-ray burst (simulated as rare random event) | Deposit `GammaRayBurst` layer globally with `IsotopeAnomaly` proportional to event intensity. Also apply a temporary spike to UV flux that suppresses surface biota. |
| Ocean anoxic event (triggered when `O₂` in atmosphere drops below threshold) | Deposit `OceanAnoxicEvent` black-shale layer on all submerged cells. |
| Snowball glaciation (global temperature < −20 °C average) | Deposit `SnowballGlacial` diamictite layer on all land cells. |
| Carbon isotope excursion (rapid atmospheric CO₂ change > 50 ppm/tick) | Deposit `CarbonIsotopeExcursion` marker globally. |

### 1.3 — Extended CellInspection (`backend/GeoTime.Core/SimulationOrchestrator.cs`)

Extend `CellInspection` to include:
- `StratigraphicColumn Column` — the full column for the inspected cell.
- `List<string> FeatureIds` — IDs of all `DetectedFeature`s that contain this cell (from `FeatureRegistry`).
- `string? RiverName` — name of the river flowing through this cell (null if none).
- `string? WatershedFeatureId` — feature ID of the watershed basin this cell drains into.
- `float DistanceToPlateMarginKm` — distance to the nearest plate boundary.
- `PlateMarginType NearestMarginType` — type of the nearest boundary (subduction, rift, transform, collision).
- `float EstimatedRockAgeMyears` — modelled age of the surface rock in millions of sim-years, based on deposition tick and tick-to-million-year scale factor.
- `List<GeoLogEntry> LocalEvents` — all events that have affected this cell, ordered by tick.

### 1.4 — Phase D1 Tests (`backend/GeoTime.Tests/StratigraphyTests.cs`)

- Advance the simulation with a triggered bolide impact; verify ejecta layers appear in cells within the expected radius, with correct thickness falloff.
- Trigger a GRB event; verify all cells have a `GammaRayBurst` layer with `IsotopeAnomaly > 0`.
- Verify `CellInspection` returns the correct `FeatureIds` for a cell known to be inside a mountain range.

---

## Phase D2 — Geological Context Assembler

**Goal**: Build the C# service that converts raw simulation data for a cell into a structured, richly annotated `GeologicalContext` object ready for consumption by either the template engine or the LLM.

### 2.1 — GeologicalContext Model (`backend/GeoTime.Core/Models/DescriptionModels.cs`)

```csharp
public record GeologicalContext
{
    // === Location ===
    public float Lat { get; init; }
    public float Lon { get; init; }
    public long  CurrentTick { get; init; }
    public string SimAgeDescription { get; init; }  // e.g., "~2.4 billion years"

    // === Cell-level data ===
    public CellInspection Cell { get; init; }
    public StratigraphicColumn Column { get; init; }

    // === Feature hierarchy ===
    public List<DetectedFeature> ContainingFeatures { get; init; }  // sorted by scale: plate > continent > mountain range > river
    public DetectedFeature? PrimaryLandFeature { get; init; }       // most specific land feature
    public DetectedFeature? PrimaryWaterFeature { get; init; }      // ocean / sea / lake / river

    // === Tectonic context ===
    public PlateInfo CurrentPlate { get; init; }
    public float DistanceToPlateMarginKm { get; init; }
    public PlateMarginType NearestMarginType { get; init; }
    public PlateInfo? CollidingPlate { get; init; }
    public PlateInfo? SubductingPlate { get; init; }
    public float ConvergenceRateCmPerYear { get; init; }

    // === Hydrological context ===
    public string? RiverName { get; init; }
    public float? RiverLengthKm { get; init; }
    public float? CatchmentAreaKm2 { get; init; }
    public string? RiverOutletOcean { get; init; }
    public string? WatershedName { get; init; }
    public bool IsInEndorheicBasin { get; init; }
    public float? DrainageGradient { get; init; }

    // === Mountain / orographic context ===
    public bool IsInMountainRange { get; init; }
    public string? RangeName { get; init; }
    public float? RangeMaxElevationM { get; init; }
    public bool IsOnWindwardSide { get; init; }
    public bool HasRainShadow { get; init; }
    public string? MountainOriginType { get; init; }  // "fold-belt", "volcanic arc", "hotspot shield"

    // === Climate context ===
    public string BiomeType { get; init; }
    public float MeanTempC { get; init; }
    public float MeanPrecipMm { get; init; }
    public bool IsInMonsonZone { get; init; }
    public bool IsInHurricaneCorridor { get; init; }
    public bool IsInJetStreamZone { get; init; }
    public string? NearestOceanCurrentName { get; init; }
    public bool NearestCurrentIsWarm { get; init; }

    // === Extraordinary events in stratigraphic record ===
    public List<StratigraphicLayer> ExtraordinaryLayers { get; init; }
    // Filtered: only layers where EventType != Normal

    // === Full feature history (from FeatureRegistry) ===
    public List<FeatureSnapshot> PrimaryFeatureHistory { get; init; }
    // The full timeline of the primary land feature this cell belongs to

    // === Nearby notable features ===
    public List<(DetectedFeature Feature, float DistanceKm)> NearbyFeatures { get; init; }
    // Up to 6 nearest named features with distance
}
```

### 2.2 — Context Assembler Service (`backend/GeoTime.Core/Services/GeologicalContextAssembler.cs`)

`Task<GeologicalContext> AssembleAsync(int cellIndex)`:

1. Fetch `CellInspection` + `StratigraphicColumn` from `SimulationState`.
2. Look up `FeatureRegistry` to find all features containing `cellIndex`; sort by scale.
3. Identify tectonic context: find the owning plate, compute nearest boundary distance and type, find converging/subducting neighbour plate if within 500 km.
4. Identify hydrological context from the `HydroDetector` river network: find which river (if any) this cell is part of, trace to outlet, compute gradient.
5. Identify orographic context: is the cell in a mountain range? Compute windward/leeward classification.
6. Identify climate zone membership from `ClimateEngine` outputs.
7. Filter the stratigraphic column to extract extraordinary layers (EventType ≠ Normal).
8. Retrieve the `PrimaryFeatureHistory` from the feature's `History` list.
9. Find the 6 nearest named features within 3000 km.
10. Return fully populated `GeologicalContext`.

### 2.3 — Phase D2 Tests (`backend/GeoTime.Tests/ContextAssemblerTests.cs`)

- For a cell known to be on a subduction zone, verify `NearestMarginType == Subduction` and `SubductingPlate != null`.
- For a cell with a known impact layer in its column, verify `ExtraordinaryLayers` is non-empty and contains `ImpactEjecta`.
- For a cell inside a known river path, verify `RiverName` matches and `RiverLengthKm > 0`.

---

## Phase D3 — LLM Provider Abstraction

**Goal**: Define a clean `ILlmProvider` interface and implement it for every supported backend. The caller (`DescriptionService`) never knows which provider is active. Provider selection and configuration is entirely in `appsettings.json`.

### 3.1 — Provider Interface (`backend/GeoTime.Api/Llm/ILlmProvider.cs`)

```csharp
public interface ILlmProvider
{
    string Name { get; }
    bool IsAvailable { get; }
    Task<string> GenerateAsync(string systemPrompt, string userPrompt,
                               CancellationToken ct = default);
}
```

### 3.2 — Provider Implementations

#### `GeminiProvider` (primary)
- Uses Google Generative AI REST API (`generativelanguage.googleapis.com`).
- Config keys: `Llm:Gemini:ApiKey`, `Llm:Gemini:Model` (default `gemini-2.0-flash`).
- HTTP client sends `POST /v1beta/models/{model}:generateContent` with `systemInstruction` + `contents[user]`.
- Handles `429` (rate limit) with exponential backoff, max 3 retries.

#### `OpenAiProvider`
- Config keys: `Llm:OpenAi:ApiKey`, `Llm:OpenAi:Model` (default `gpt-4o-mini`), `Llm:OpenAi:BaseUrl` (optional, for Azure OpenAI endpoint overriding).
- Uses the standard chat completions endpoint.

#### `AnthropicProvider`
- Config keys: `Llm:Anthropic:ApiKey`, `Llm:Anthropic:Model` (default `claude-haiku-3-5`).
- Uses the Messages API with `system` + `user` turn.

#### `OllamaProvider`
- Connects to a local Ollama instance at `http://localhost:11434`.
- Config keys: `Llm:Ollama:BaseUrl`, `Llm:Ollama:Model` (e.g., `gemma3`, `llama3.2`).
- Uses `/api/chat` endpoint with `stream: false`.
- `IsAvailable`: attempts a `GET /api/tags` health check on startup; if the server is unreachable, returns `false` without throwing.

#### `LlamaSharpProvider`
- Loads a GGUF model file directly in-process using the `LLamaSharp` NuGet package.
- Config keys: `Llm:LlamaSharp:ModelPath` (absolute path to .gguf file), `Llm:LlamaSharp:ContextSize` (default 4096), `Llm:LlamaSharp:GpuLayers` (default 0 for CPU-only).
- `IsAvailable`: returns `false` if `ModelPath` is not set or the file does not exist, so the system silently falls back without error.
- Model is loaded once at DI container startup (singleton scope) to avoid per-request load latency.

#### `TemplateFallbackProvider`
- Always available (`IsAvailable = true`), zero external dependencies.
- `GenerateAsync` ignores the prompts and instead calls the template engine (see Phase D4) to assemble deterministic prose from the structured context embedded in the user prompt as JSON.
- Acts as the ultimate fallback when no other provider is configured or available.

### 3.3 — Provider Selection (`backend/GeoTime.Api/Llm/LlmProviderFactory.cs`)

`LlmProviderFactory` is registered as a singleton. On construction it reads `appsettings.json` and builds an ordered list of providers by a `Llm:PreferenceOrder` config array (default: `["Gemini", "OpenAi", "Anthropic", "Ollama", "LlamaSharp", "Template"]`). `GetProvider()` returns the first `IsAvailable == true` provider in the list.

### 3.4 — Phase D3 Tests (`backend/GeoTime.Tests/LlmProviderTests.cs`)

- `LlmProviderFactory` with no keys configured → returns `TemplateFallbackProvider`.
- `OllamaProvider` with unreachable URL → `IsAvailable == false`.
- `LlamaSharpProvider` with missing model file → `IsAvailable == false`.
- Mock `GeminiProvider` HTTP handler: verify correct request format and retry behaviour on 429.

---

## Phase D4 — Geological Narrative Assembly and Template Engine

**Goal**: Build the prompt composer that converts a `GeologicalContext` into a rich, expert-level structured prompt, and build the template fallback that can produce solid prose without an LLM.

### 4.1 — Prompt Composer (`backend/GeoTime.Core/Services/DescriptionPromptComposer.cs`)

The `ComposeSystemPrompt()` method returns a fixed system message establishing the LLM's persona:

> "You are a senior planetary geologist writing encyclopedia entries for a fictional alien world. Your writing is precise, technical, and vivid — the style of a Nature article combined with a National Geographic narrative. You describe geological formations in the same depth a field geologist would: origin, deformation history, lithology, erosional history, drainage influence, and climate coupling. You reference all features, plates, rivers, and oceans by their proper names as given. You explicitly discuss any extraordinary events visible in the stratigraphic record. You describe how the formation evolved through geological time and how it is changing today."

The `ComposeUserPrompt(GeologicalContext ctx)` method serialises the full context to a compact JSON block followed by explicit instructions:

1. Write a 4–6 paragraph encyclopedia entry for the **primary feature** at this location.
2. Paragraph 1: Tectonic origin — how the feature formed, plate kinematics, timescale.
3. Paragraph 2: Lithology and stratigraphy — rock types, deformation, any extraordinary event layers visible in the record (name them: "the {EventId} impact ejecta layer, deposited at simulation year {year}").
4. Paragraph 3: Erosion, drainage, and river systems — how water and ice shaped the feature; name any rivers, their catchment area, and outlet seas.
5. Paragraph 4: Climate coupling — orographic effects, rain shadow, monsoon, hurricane corridor, ocean current influence.
6. Paragraph 5: Biome and ecology cascade — how the feature determines surrounding biome distribution.
7. Paragraph 6 (if history spans > 5 major changes): Historical biography — major changes in the feature's identity (splits, merges, submergences, renames), with simulation-year dates.
8. End with a 2-sentence summary of what makes this feature geologically significant on this planet.

### 4.2 — Template Fallback Engine (`backend/GeoTime.Core/Services/DescriptionTemplateEngine.cs`)

For every `FeatureType`, implement a C# template method that reads `GeologicalContext` and builds the same 4–6 paragraph structure using conditional sentence assembly. Key expert logic encoded in templates:

- **Mountain Range**: Classify origin (subduction arc → "The {RangeName} arc rises above a {dip}° eastward-dipping subduction zone…"; collision orogen → "Formed by the Himalayan-style collision of {plate1} and {plate2}…"; hotspot shield → "The broad shield of {name} reflects mantle plume magmatism…"). Include rain-shadow sentence if `HasRainShadow`. Include orogenic deformation style (fold-and-thrust belt vs. metamorphic core complex) based on convergence rate. Include erosion river-routing paragraph.
- **River**: Describe headwater source (glacial, spring, orographic precipitation), gradient profile, valley morphology (V-shaped = young/steep; U-shaped = glacially carved; wide floodplain = mature/low gradient), delta type, sediment routing to the named ocean, and seasonal variation (perennial vs. monsoon-fed).
- **Ocean Basin**: Describe spreading history, age of oceanic crust, bathymetric provinces (abyssal plain, mid-ocean ridge, trenches), thermohaline connection to other basins, and named boundary current systems.
- **Continent**: Describe cratonisation age, orogenic belts along margins, drainage divide, mean elevation, climate zonation across the landmass.
- **Impact Basin**: Describe impactor energy estimate, ejecta blanket extent, melt sheet, central uplift, and subsequent erosional and sedimentary infill history.
- **Extraordinary layers** (any feature type): For each non-Normal layer in the column, add a dedicated sentence: "The stratigraphic column at this location preserves a {layerThicknessM}-m {EventType} horizon from {eventDescription}, characterised by {isotope/soot/geochemical signature}."

### 4.3 — Phase D4 Tests (`backend/GeoTime.Tests/DescriptionTemplateTests.cs`)

- For a `GeologicalContext` with `NearestMarginType = Subduction`, verify template contains "subduction" and the subducting plate name.
- For a context with an `ImpactEjecta` extraordinary layer, verify template mentions the impact event.
- For a context with `HasRainShadow = true`, verify template mentions "rain shadow" on the leeward side.
- For a river feature, verify template contains the river name, outlet ocean name, and delta type.

---

## Phase D5 — API Endpoint and Frontend Integration

**Goal**: Expose a clean `POST /api/describe` endpoint and wire the minimal frontend modal.

### 5.1 — Description API (`backend/GeoTime.Api/Program.cs`)

```
POST /api/describe
Request:  { "cellIndex": int }
Response: {
  "title": string,
  "subtitle": string,
  "paragraphs": string[],
  "stats": [{ "label": string, "value": string }],
  "stratigraphicSummary": [{ "age": string, "thickness": string, "rockType": string, "eventNote": string }],
  "historyTimeline": [{ "simTick": long, "event": string, "name": string }],
  "providerUsed": string   // "Gemini", "Ollama", "Template", etc.
}
```

The endpoint:
1. Calls `GeologicalContextAssembler.AssembleAsync(cellIndex)` to build the full context.
2. Builds stats array and stratigraphic summary from the context directly (no LLM needed for these).
3. Builds history timeline from `PrimaryFeatureHistory`.
4. Calls `LlmProviderFactory.GetProvider().GenerateAsync(systemPrompt, userPrompt)` for the prose paragraphs.
5. Parses the LLM response — if it returns well-formed JSON with a `paragraphs` array, deserialise it; otherwise treat the whole response as a single paragraph block.
6. Returns the composite response.

Add `POST /api/describe/stream` variant that uses SSE (Server-Sent Events) to stream the LLM response token-by-token if the provider supports streaming (Gemini, OpenAI, Anthropic, Ollama all support streaming).

### 5.2 — Frontend: Minimal Description Modal (`src/ui/app-shell.ts`, `src/main.ts`)

- Add a small `ℹ` button to the inspect panel header.
- On click: `POST /api/describe` with the current `cellIndex`. While awaiting, show a spinner inside the modal.
- When the response arrives, populate:
  - Title + subtitle text.
  - Paragraphs as `<p>` elements.
  - Stats as a two-column `<table>`.
  - Stratigraphic summary as a visual column strip (colour-coded by rock type using existing rock colour map) with event annotations.
  - History timeline as a simple `<ol>` list.
- For streaming (`/api/describe/stream`): open an `EventSource`, append tokens to the last paragraph element in real time.
- Close button and click-outside-to-dismiss.

The frontend adds zero geological logic — it is a pure rendering consumer of the backend response.

### 5.3 — Configuration Reference (`backend/GeoTime.Api/appsettings.json`)

Document all provider configuration keys:

```json
{
  "Llm": {
    "PreferenceOrder": ["Gemini", "OpenAi", "Anthropic", "Ollama", "LlamaSharp", "Template"],
    "Gemini":     { "ApiKey": "",  "Model": "gemini-2.0-flash" },
    "OpenAi":     { "ApiKey": "",  "Model": "gpt-4o-mini", "BaseUrl": "" },
    "Anthropic":  { "ApiKey": "",  "Model": "claude-haiku-3-5" },
    "Ollama":     { "BaseUrl": "http://localhost:11434", "Model": "gemma3" },
    "LlamaSharp": { "ModelPath": "", "ContextSize": 4096, "GpuLayers": 0 }
  }
}
```

### 5.4 — Phase D5 Tests (`backend/GeoTime.Tests/DescriptionApiTests.cs`)

- Integration test: `POST /api/describe` with no LLM configured → returns 200 with non-empty paragraphs from the template engine.
- Integration test: stratigraphic summary array has correct count of layers for a known cell.
- Integration test: history timeline is ordered by tick ascending.
- Playwright E2E: click ℹ on inspect panel after planet generation, verify modal opens with title and ≥ 2 paragraphs.
- Playwright E2E: stratigraphic strip element is present in the modal.

---

## Phase D6 — Geological Layers Visualisation (Layer Toggle Integration)

**Goal**: Expose the extraordinary stratigraphic events as a toggleable globe overlay so users can see where impact ejecta, volcanic ash, GRB anomalies, and other global events are preserved in the rock record.

### 6.1 — Backend: Event Layer Map Endpoint

```
GET /api/state/eventlayermap?eventType=ImpactEjecta&tick=N
```
Returns a flat `float[]` array (one value per grid cell, 512×512 = 262 144 values) representing the **thickness of layers of the specified type** deposited up to tick N. This is a binary scalar field suitable for rendering as a heatmap texture.

```
GET /api/state/eventlayermap/types
```
Returns the list of `LayerEventType` values that have at least one layer anywhere on the planet. Used to populate the layer toggle dropdown.

### 6.2 — Frontend: Event Layer Overlay

- Add an "Event Layers" toggle and a sub-dropdown (populated from `/api/state/eventlayermap/types`) in the layer panel.
- When an event type is selected, fetch the scalar field and upload it as a new `DataTexture` to the `GlobeRenderer`. Apply it as a colour tint overlay: colour palette varies by event type (orange-red for impact ejecta, grey for volcanic soot, cyan for GRB anomaly, black for anoxic event).
- Layer blends on top of the base terrain colour using additive blending weighted by the layer thickness value.

### 6.3 — Phase D6 Tests

- Unit test for the event layer map endpoint: given a known impact at cell X, verify cells within ejecta radius have thickness > 0 and cells outside have thickness 0.
- Playwright E2E: trigger event layer overlay for "VolcanicAsh", verify the globe texture changes.

---

## File Change Summary

| File | Change |
|------|--------|
| `backend/GeoTime.Core/Models/StratigraphyModels.cs` | New — stratigraphic column and event layer types |
| `backend/GeoTime.Core/Models/DescriptionModels.cs` | New — `GeologicalContext`, `DescriptionRequest`, `DescriptionResponse` |
| `backend/GeoTime.Core/Models/SimulationModels.cs` | Add `StratigraphicColumn[]` and extended `CellInspection` fields |
| `backend/GeoTime.Core/Engines/EventDepositionEngine.cs` | New — event → stratigraphic layer deposition |
| `backend/GeoTime.Core/Services/GeologicalContextAssembler.cs` | New — assembles full geological context for a cell |
| `backend/GeoTime.Core/Services/DescriptionPromptComposer.cs` | New — system + user prompt builder |
| `backend/GeoTime.Core/Services/DescriptionTemplateEngine.cs` | New — template fallback prose engine |
| `backend/GeoTime.Core/SimulationOrchestrator.cs` | Call `EventDepositionEngine` after tick; extend `InspectCell` to include new fields |
| `backend/GeoTime.Api/Llm/ILlmProvider.cs` | New — provider interface |
| `backend/GeoTime.Api/Llm/GeminiProvider.cs` | New |
| `backend/GeoTime.Api/Llm/OpenAiProvider.cs` | New |
| `backend/GeoTime.Api/Llm/AnthropicProvider.cs` | New |
| `backend/GeoTime.Api/Llm/OllamaProvider.cs` | New |
| `backend/GeoTime.Api/Llm/LlamaSharpProvider.cs` | New |
| `backend/GeoTime.Api/Llm/TemplateFallbackProvider.cs` | New |
| `backend/GeoTime.Api/Llm/LlmProviderFactory.cs` | New — provider selection factory |
| `backend/GeoTime.Api/Program.cs` | Add `POST /api/describe`, `POST /api/describe/stream`, `GET /api/state/eventlayermap` |
| `backend/GeoTime.Api/appsettings.json` | Document all LLM provider config keys |
| `backend/GeoTime.Tests/StratigraphyTests.cs` | New |
| `backend/GeoTime.Tests/ContextAssemblerTests.cs` | New |
| `backend/GeoTime.Tests/LlmProviderTests.cs` | New |
| `backend/GeoTime.Tests/DescriptionTemplateTests.cs` | New |
| `backend/GeoTime.Tests/DescriptionApiTests.cs` | New |
| `src/api/backend-client.ts` | Add `describeCell()` and `fetchEventLayerMap()` |
| `src/ui/app-shell.ts` | Add ℹ button on inspect panel; add description modal HTML; add event layer dropdown |
| `src/main.ts` | Wire modal open/close; wire event layer toggle |
| `src/render/globe-renderer.ts` | Add event layer overlay texture + blend |
| `e2e/app-shell.spec.ts` | Add description modal and event layer overlay E2E tests |

---

## Dependency Check

| Dependency | Purpose | Advisory check required? |
|------------|---------|--------------------------|
| `LLamaSharp` NuGet (+ `LLamaSharp.Backend.Cpu` or `.Cuda12`) | Local GGUF inference | Yes — check NuGet advisory DB before adding |
| No new NuGet needed for Gemini / OpenAI / Anthropic / Ollama | All use standard `HttpClient` | No |
| No new npm packages | All frontend changes use existing Three.js + fetch | No |

The `LlamaSharpProvider` is entirely optional; the system works without it. Only add the NuGet package in a dedicated phase when a local LLM is needed.
