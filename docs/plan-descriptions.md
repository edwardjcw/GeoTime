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

## Phase D3 — LLM Provider Abstraction, Runtime Settings UI, and Local LLM Setup

**Goal**: Define a clean `ILlmProvider` interface and implement it for every supported backend. The caller (`DescriptionService`) never knows which provider is active. The active provider and its key/model settings are **changeable at runtime** through a settings panel in the UI — no server restart required. For local providers (Ollama and LlamaSharp), the UI offers a guided **Setup** flow that downloads and configures the provider automatically, with a live progress bar showing each step.

### 3.1 — Provider Interface (`backend/GeoTime.Api/Llm/ILlmProvider.cs`)

```csharp
public interface ILlmProvider
{
    string Name { get; }
    bool IsAvailable { get; }

    /// <summary>Async check that actively probes the provider (e.g., HTTP ping or file check).</summary>
    Task<LlmProviderStatus> GetStatusAsync(CancellationToken ct = default);

    Task<string> GenerateAsync(string systemPrompt, string userPrompt,
                               CancellationToken ct = default);

    /// <summary>Token-streaming variant. Yields tokens as they arrive.</summary>
    IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userPrompt,
                                         CancellationToken ct = default);
}

public record LlmProviderStatus(
    bool   IsReady,
    string StatusMessage,    // e.g., "Connected", "API key missing", "Model not downloaded"
    bool   NeedsSetup        // true for local providers that haven't been configured yet
);
```

### 3.2 — Provider Implementations

#### `GeminiProvider` (primary)
- Uses Google Generative AI REST API (`generativelanguage.googleapis.com`).
- Config keys: `Llm:Gemini:ApiKey`, `Llm:Gemini:Model` (default `gemini-2.0-flash`).
- HTTP client sends `POST /v1beta/models/{model}:generateContent` with `systemInstruction` + `contents[user]`.
- Streaming: uses the `?alt=sse` variant of the same endpoint.
- Handles `429` (rate limit) with exponential backoff, max 3 retries.
- `NeedsSetup = false` (cloud provider, no local installation required).

#### `OpenAiProvider`
- Config keys: `Llm:OpenAi:ApiKey`, `Llm:OpenAi:Model` (default `gpt-4o-mini`), `Llm:OpenAi:BaseUrl` (optional, for Azure OpenAI endpoint overriding).
- Uses the standard chat completions endpoint with SSE streaming.
- `NeedsSetup = false`.

#### `AnthropicProvider`
- Config keys: `Llm:Anthropic:ApiKey`, `Llm:Anthropic:Model` (default `claude-haiku-3-5`).
- Uses the Messages API with `system` + `user` turn; streaming via `"stream": true`.
- `NeedsSetup = false`.

#### `OllamaProvider`
- Connects to a local Ollama instance.
- Config keys: `Llm:Ollama:BaseUrl` (default `http://localhost:11434`), `Llm:Ollama:Model` (e.g., `gemma3`, `llama3.2`).
- Uses `/api/chat` with `"stream": false` for batch, `/api/chat` with `"stream": true` for streaming.
- `IsAvailable` / `GetStatusAsync`: attempts a `GET /api/tags` health check; if the server is unreachable → `NeedsSetup = true, StatusMessage = "Ollama not running"`.
- `NeedsSetup = true` when Ollama binary is not installed or the configured model has not been pulled.

#### `LlamaSharpProvider`
- Loads a GGUF model file directly in-process using the `LLamaSharp` NuGet package.
- Config keys: `Llm:LlamaSharp:ModelPath`, `Llm:LlamaSharp:ModelUrl` (GGUF download URL, used by the setup flow), `Llm:LlamaSharp:ContextSize` (default 4096), `Llm:LlamaSharp:GpuLayers` (default 0).
- `IsAvailable` / `GetStatusAsync`: checks whether the file at `ModelPath` exists and is a valid GGUF → `NeedsSetup = true` if not.
- Model is loaded once at DI container startup (singleton scope); reloaded without restart if the setup flow writes a new model.

#### `TemplateFallbackProvider`
- Always available (`IsAvailable = true`, `NeedsSetup = false`), zero external dependencies.
- `GenerateAsync` / `StreamAsync` call the template engine (Phase D4) to assemble deterministic prose.
- Acts as the ultimate fallback when no other provider is configured or available.

### 3.3 — Provider Selection and Runtime Config (`backend/GeoTime.Api/Llm/LlmProviderFactory.cs`, `LlmSettingsService.cs`)

#### `LlmSettingsService` (new singleton)
Holds the **runtime-mutable** LLM configuration that the UI can change without restarting the server. On startup it seeds itself from `appsettings.json`; any UI change persists the new settings to a `llm-settings.json` user file (in the app data directory, gitignored):

```csharp
public class LlmSettingsService
{
    public string ActiveProvider { get; private set; }  // "Gemini", "Ollama", etc.
    public Dictionary<string, ProviderSettings> ProviderConfigs { get; }

    public void SetActiveProvider(string name);
    public void UpdateProviderConfig(string name, ProviderSettings settings);
    public void Save();   // persist to llm-settings.json
}

public record ProviderSettings(
    string? ApiKey,
    string? Model,
    string? BaseUrl,
    string? ModelPath,
    string? ModelUrl,
    int?    ContextSize,
    int?    GpuLayers
);
```

#### `LlmProviderFactory`
Registered as a singleton; holds references to all provider instances. `GetActiveProvider()` returns the provider matching `LlmSettingsService.ActiveProvider`. If that provider is not available, falls back down the preference list to the first available provider.

### 3.4 — LLM Settings API Endpoints (`backend/GeoTime.Api/Program.cs`)

```
GET  /api/llm/providers
→ Returns list of all providers with their current status:
  [{ name, displayName, isAvailable, needsSetup, activeModel, statusMessage }]

GET  /api/llm/active
→ Returns the current active provider name and its settings (API key redacted).

PUT  /api/llm/active
Body: { "provider": "Gemini", "settings": { "apiKey": "...", "model": "gemini-2.0-flash" } }
→ Updates active provider + config; saves to llm-settings.json. No restart required.

POST /api/llm/setup/{provider}
→ Triggers the setup flow for a local provider (Ollama or LlamaSharp).
→ Returns 202 Accepted immediately; progress is streamed via SSE on /api/llm/setup/{provider}/progress.

GET  /api/llm/setup/{provider}/progress   (SSE endpoint)
→ Streams LlmSetupProgress events until setup completes or fails.
```

### 3.5 — Local LLM Setup Service (`backend/GeoTime.Api/Llm/LocalLlmSetupService.cs`)

`LocalLlmSetupService` orchestrates the full installation flow for local providers. Each step reports progress via a `Channel<LlmSetupProgress>` that the SSE endpoint drains and forwards to the browser.

```csharp
public record LlmSetupProgress(
    string Step,           // e.g., "Checking prerequisites", "Downloading Ollama installer",
                           //        "Installing Ollama", "Pulling model gemma3",
                           //        "Loading model", "Verifying", "Complete"
    int    PercentTotal,   // 0–100 overall progress
    string Detail,         // e.g., "487 MB / 2.1 GB  (23%)" during download
    bool   IsComplete,
    bool   IsError,
    string? ErrorMessage
);
```

#### Ollama Setup Flow

1. **Check prerequisites** — Detect OS; verify curl/wget available.
2. **Check if Ollama is installed** — Test `ollama --version` subprocess.
3. **Download Ollama installer** — If not installed: stream-download the appropriate installer binary from `https://ollama.com/install.sh` (Linux/macOS) or `.exe` (Windows), reporting byte progress.
4. **Install Ollama** — Run the installer subprocess; capture stdout/stderr; report each installer log line as a `Detail` update.
5. **Start Ollama service** — Run `ollama serve` as a background process; wait up to 30 s for `/api/tags` health check to respond.
6. **Pull model** — Run `ollama pull {model}`; parse the JSON progress output Ollama emits (`{"status":"pulling manifest"}`, `{"status":"downloading","completed":N,"total":N}`) and map to percentage `Detail`.
7. **Verify** — Send a short test prompt via `OllamaProvider`; verify non-empty response.
8. **Complete** — Mark `IsComplete = true`; `LlmSettingsService.SetActiveProvider("Ollama")`.

#### LlamaSharp Setup Flow

1. **Check prerequisites** — Verify sufficient disk space (configurable, default 5 GB) and that `Llm:LlamaSharp:ModelPath` directory is writable.
2. **Determine model URL** — Use `Llm:LlamaSharp:ModelUrl` from settings if set; otherwise show a default (e.g., Gemma-3 4B Q4_K_M from Hugging Face).
3. **Download GGUF model file** — HTTP range-request streaming download with byte progress; supports resume if partial file exists (via `Range:` header).
4. **Validate GGUF header** — Read first 4 bytes and verify `GGUF` magic number.
5. **Load model into LlamaSharpProvider** — Call `provider.ReloadModel(path)`.
6. **Verify** — Send a test prompt; verify response.
7. **Complete** — Persist `ModelPath` to `llm-settings.json`; mark active provider.

### 3.6 — LLM Settings UI (`src/ui/app-shell.ts`, `src/main.ts`)

The frontend is kept minimal: it calls the API and renders the response. All logic lives on the server.

#### Settings Button
- Add a **⚙ LLM** settings button to the existing toolbar (next to layer toggles).
- On click, open a `#llm-settings-panel` side-panel (not a blocking modal, so the globe remains visible).

#### Provider Selection Panel

The panel fetches `GET /api/llm/providers` on open and renders:

```
Active LLM Provider
┌─────────────────────────────────────────────────────────────┐
│  ◉ Gemini          ✓ Connected   [Model: gemini-2.0-flash ▼]│
│  ○ OpenAI          ✓ Connected   [Model: gpt-4o-mini     ▼]│
│  ○ Anthropic       ✗ No API key  [Model: claude-haiku-3-5▼]│
│  ○ Ollama          ⚠ Not running  [Model: gemma3         ▼] [Setup ▶]│
│  ○ LlamaSharp      ⚠ Not set up  [Model: gemma-3-4b-q4  ▼] [Setup ▶]│
│  ○ Template        ✓ Always available                       │
└─────────────────────────────────────────────────────────────┘
  [API Key / URL field for selected cloud provider]
  [Save & Apply]
```

- Selecting a radio button and clicking **Save & Apply** calls `PUT /api/llm/active`.
- Cloud providers show an API key input field (password type) that submits only when changed.
- Status indicators update live: every 10 s the panel re-fetches `GET /api/llm/providers` if it is open.

#### Local LLM Setup Flow

When the user clicks **Setup ▶** next to Ollama or LlamaSharp:

1. A `#llm-setup-progress` sub-panel slides open below the provider row.
2. Frontend calls `POST /api/llm/setup/{provider}` (fire-and-forget).
3. Frontend opens an `EventSource` on `GET /api/llm/setup/{provider}/progress`.
4. Each `LlmSetupProgress` SSE event updates:
   - **Step label**: e.g., "Downloading Ollama installer…"
   - **Progress bar**: `<progress value={percentTotal} max="100">` styled with the app's dark theme.
   - **Detail line**: e.g., "487 MB / 2.1 GB (23%)" — monospace, dimmed.
   - **Step list**: a vertical checklist (`✓`, `⏳`, `○`) of all steps, with the current step highlighted.
5. On `IsComplete = true`: close the `EventSource`; show a green ✓ "Ready" badge; refresh the provider list.
6. On `IsError = true`: show the `ErrorMessage` in red; offer a **Retry** button (calls `POST /api/llm/setup/{provider}` again).
7. The user can close the sub-panel at any time; setup continues in the background and the panel can be re-opened to see progress.

The frontend adds zero setup logic — it only opens an `EventSource` and renders the progress events it receives.

### 3.7 — Phase D3 Tests

**Backend (`backend/GeoTime.Tests/LlmProviderTests.cs`)**
- `LlmProviderFactory` with no keys configured → returns `TemplateFallbackProvider`.
- `OllamaProvider` with unreachable URL → `GetStatusAsync` returns `NeedsSetup = true`.
- `LlamaSharpProvider` with missing model file → `GetStatusAsync` returns `NeedsSetup = true`.
- Mock `GeminiProvider` HTTP handler: verify correct request format and retry on 429.
- `PUT /api/llm/active` with valid Gemini config → `LlmSettingsService.ActiveProvider == "Gemini"`.
- `GET /api/llm/providers` returns all 6 entries including `TemplateFallbackProvider`.

**Backend (`backend/GeoTime.Tests/LocalLlmSetupTests.cs`)**
- `LocalLlmSetupService` Ollama flow: mock subprocess executor and HTTP downloader; verify progress events emitted in correct order (Check → Download → Install → Start → Pull → Verify → Complete).
- Resume download: verify that if a partial file exists, the download resumes using HTTP `Range:` header.
- Failure path: mock installer subprocess returning exit code 1 → `IsError = true` event emitted with correct `ErrorMessage`.
- GGUF validation: a file with invalid magic bytes → `IsError = true`.

**Frontend (`e2e/app-shell.spec.ts`)**
- Click ⚙ LLM button → `#llm-settings-panel` appears.
- Provider list contains at least "Template" with status "Always available".
- Mock `PUT /api/llm/active` → panel closes and toolbar shows active provider name.

**File additions for Phase D3**

| File | Change |
|------|--------|
| `backend/GeoTime.Api/Llm/ILlmProvider.cs` | Updated interface with `GetStatusAsync`, `StreamAsync` |
| `backend/GeoTime.Api/Llm/LlmSettingsService.cs` | New — runtime-mutable settings |
| `backend/GeoTime.Api/Llm/LocalLlmSetupService.cs` | New — Ollama + LlamaSharp setup orchestrator |
| `backend/GeoTime.Api/Llm/GeminiProvider.cs` | New |
| `backend/GeoTime.Api/Llm/OpenAiProvider.cs` | New |
| `backend/GeoTime.Api/Llm/AnthropicProvider.cs` | New |
| `backend/GeoTime.Api/Llm/OllamaProvider.cs` | New |
| `backend/GeoTime.Api/Llm/LlamaSharpProvider.cs` | New |
| `backend/GeoTime.Api/Llm/TemplateFallbackProvider.cs` | New |
| `backend/GeoTime.Api/Llm/LlmProviderFactory.cs` | New |
| `backend/GeoTime.Api/Program.cs` | Add LLM settings + setup SSE endpoints |
| `backend/GeoTime.Api/appsettings.json` | Document all config keys |
| `backend/GeoTime.Tests/LlmProviderTests.cs` | New |
| `backend/GeoTime.Tests/LocalLlmSetupTests.cs` | New |
| `src/ui/app-shell.ts` | Add ⚙ LLM button; add settings panel + setup sub-panel HTML + CSS |
| `src/main.ts` | Wire settings button open/close; wire `EventSource` setup progress |
| `e2e/app-shell.spec.ts` | Add LLM settings panel E2E tests |

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
| `backend/GeoTime.Api/Llm/ILlmProvider.cs` | New — provider interface with `GetStatusAsync` and `StreamAsync` |
| `backend/GeoTime.Api/Llm/LlmSettingsService.cs` | New — runtime-mutable provider config (persisted to `llm-settings.json`) |
| `backend/GeoTime.Api/Llm/LocalLlmSetupService.cs` | New — Ollama + LlamaSharp guided setup with SSE progress streaming |
| `backend/GeoTime.Api/Llm/GeminiProvider.cs` | New |
| `backend/GeoTime.Api/Llm/OpenAiProvider.cs` | New |
| `backend/GeoTime.Api/Llm/AnthropicProvider.cs` | New |
| `backend/GeoTime.Api/Llm/OllamaProvider.cs` | New |
| `backend/GeoTime.Api/Llm/LlamaSharpProvider.cs` | New |
| `backend/GeoTime.Api/Llm/TemplateFallbackProvider.cs` | New |
| `backend/GeoTime.Api/Llm/LlmProviderFactory.cs` | New — provider selection factory |
| `backend/GeoTime.Api/Program.cs` | Add `POST /api/describe`, `POST /api/describe/stream`, `GET /api/state/eventlayermap`, LLM settings + setup SSE endpoints |
| `backend/GeoTime.Api/appsettings.json` | Document all LLM provider config keys |
| `backend/GeoTime.Tests/StratigraphyTests.cs` | New |
| `backend/GeoTime.Tests/ContextAssemblerTests.cs` | New |
| `backend/GeoTime.Tests/LlmProviderTests.cs` | New — provider factory, status checks, Gemini retry |
| `backend/GeoTime.Tests/LocalLlmSetupTests.cs` | New — setup flow progress order, resume, failure, GGUF validation |
| `backend/GeoTime.Tests/DescriptionTemplateTests.cs` | New |
| `backend/GeoTime.Tests/DescriptionApiTests.cs` | New |
| `src/api/backend-client.ts` | Add `describeCell()`, `fetchEventLayerMap()`, LLM settings calls |
| `src/ui/app-shell.ts` | Add ⚙ LLM button + settings panel + setup sub-panel; add ℹ button + description modal; add event layer dropdown |
| `src/main.ts` | Wire LLM settings panel; wire `EventSource` setup progress; wire modal open/close; wire event layer toggle |
| `src/render/globe-renderer.ts` | Add event layer overlay texture + blend |
| `e2e/app-shell.spec.ts` | Add LLM settings panel, description modal, and event layer overlay E2E tests |

---

## Dependency Check

| Dependency | Purpose | Advisory check required? |
|------------|---------|--------------------------|
| `LLamaSharp` NuGet (+ `LLamaSharp.Backend.Cpu` or `.Cuda12`) | Local GGUF inference | Yes — check NuGet advisory DB before adding |
| No new NuGet needed for Gemini / OpenAI / Anthropic / Ollama | All use standard `HttpClient` | No |
| No new npm packages | All frontend changes use existing Three.js + fetch | No |

The `LlamaSharpProvider` is entirely optional; the system works without it. Only add the NuGet package in a dedicated phase when a local LLM is needed.
