# GeoTime

A planetary geology simulator with a **C# .NET backend** for simulation and a **TypeScript/Vite/Three.js frontend** for display. GeoTime models the full 4.5 billion year evolution of an Earth-like planet — from plate tectonics and volcanic eruptions to erosion, climate, weather, vegetation, and soil formation.

## Architecture

GeoTime uses a client-server architecture where all computation-heavy simulation runs on the .NET backend, and the frontend only handles rendering and user interaction:

```
┌─────────────────────┐         REST API         ┌─────────────────────────┐
│    Frontend (TS)     │ ◄─────────────────────► │    Backend (C# .NET)     │
│                      │                          │                          │
│  • Three.js globe    │   POST /api/planet/gen   │  • Planet generation     │
│  • Cross-section 2D  │   POST /api/sim/advance  │  • Tectonic engine       │
│  • UI shell / HUD    │   GET  /api/state/*      │  • Surface processes     │
│  • Layer overlays    │   POST /api/crosssection  │  • Climate & weather     │
│                      │                          │  • Vegetation engine     │
│                      │                          │  • Biomatter engine      │
└─────────────────────┘                          │  • Cross-section engine  │
                                                  └──────────────────────────┘
```

## Features

- **Plate Tectonics**: Voronoi-based plates with Euler rotation, convergent/divergent/transform boundaries, subduction, continental collision, rift valleys
- **Volcanism**: Stratovolcanoes, shield volcanoes, submarine volcanism with eruption scaling
- **Stratigraphy**: Per-cell 64-layer geological stacks recording rock type, age, and deformation
- **Surface Processes**: D∞ fluvial erosion, glacial erosion with ELA, aeolian/chemical weathering, USDA soil orders
- **Climate System**: 3-cell atmospheric circulation, Milankovitch cycles, greenhouse forcing, ice-albedo feedback
- **Weather**: Frontal systems, tropical cyclones, orographic precipitation, 10 cloud genera
- **Vegetation**: Miami Model NPP, biomass accumulation, stochastic forest fires, albedo feedback
- **Biomatter**: Simple non-plant organisms (microbes, plankton, reef builders) driving ocean chemistry, biogenic sedimentation, atmospheric O₂/CH₄, and petroleum source rocks (feature-flagged)
- **Cross-Section Viewer**: Interactive great-circle cross-sections with split-scale rendering
- **Geological Descriptions**: Context-backed descriptions with streaming-first UI and batch fallback
- **Performance**: Adaptive tick rate, deterministic seeded PRNG, CPU/GPU compute paths
- **UI**: Globe viewport, collapsible sidebar, layer overlays, geological timeline, inspect panel, URL seed sharing

## Getting Started

### Backend (C# .NET)

```bash
cd backend
dotnet restore
dotnet build
dotnet run --project GeoTime.Api   # Starts API server on http://localhost:5000
dotnet test "GeoTime.Tests/GeoTime.Tests.csproj"  # Run backend tests
```

Optional GPU diagnostic:

```bash
cd backend
dotnet run --project GeoTime.Diagnostic
```

### Performance session logs

Each backend process writes a JSONL performance log for optimization analysis. On startup the console prints the path, and the frontend logs it in the browser console after connecting.

- Default directory: `backend/GeoTime.Api/logs/`
- Override: set `GEOTIME_PERF_LOG_DIR` or `GeoTime:PerfLogDirectory` in configuration
- Query current path: `GET /api/diagnostics/session`

Logged events include:

- `session_start` / `session_end` — machine, .NET, and process metadata
- `planet_generate` — generation wall time, plate/hotspot counts, compute backend
- `simulation_advance` — per-engine tick timings, GPU activity, adaptive-resolution overhead, feature detection, event deposition
- `api_request` — wall time and response size for every `/api/*` call
- `client_advance_cycle` / `client_planet_generate` — frontend round-trip timings, FPS, and stats mirrored from the API

Example analysis:

```bash
# Slowest simulation advances
rg '"event":"simulation_advance"' backend/GeoTime.Api/logs/*.jsonl
```

### Frontend (TypeScript/Vite)

```bash
npm install
npm run dev        # Start dev server (connects to backend at localhost:5000)
npm run build      # Production build (tsc + vite)
npm run test       # Run unit tests (Vitest)
npx playwright test --reporter=list  # Run E2E tests (Playwright)
```

Playwright is configured to start the backend API and frontend preview on `127.0.0.1` for E2E runs.

Set the backend URL via environment variable if needed:
```bash
VITE_API_BASE=http://localhost:5000 npm run dev
```

### Unreal Engine Version

The `unreal/GeoTimeUE/` directory contains an Unreal Engine 5 project that connects to the same C# backend via the REST API and renders the terrain using UE5's landscape and procedural tools.

**Prerequisites:**
- Unreal Engine 5.3 or later
- The C# backend must be running (see above)

**Steps:**
1. Start the C# backend first:
   ```bash
   cd backend
   dotnet run --project GeoTime.Api
   ```
2. Open the Unreal Engine project:
   - Launch the Unreal Editor
   - Click **Browse** and navigate to `unreal/GeoTimeUE/GeoTimeUE.uproject`
   - Open the project (allow it to compile shaders on first launch)
3. Play in Editor:
   - Press **Play** (or **Alt+P**) to start the simulation view
   - The plugin fetches terrain and camera state from the backend at `http://localhost:5000`
4. Key API endpoints used by the UE plugin:
   - `GET /api/unreal/terrain-meta` — terrain dimensions and scale
   - `GET /api/unreal/heightmap-raw` — raw 16-bit heightmap bytes
   - `GET /api/unreal/terrain-tile/{x}/{y}/{lod}` — streamed terrain tiles
   - `GET /api/unreal/camera` — camera position/orientation
   - `PUT /api/unreal/camera` — update camera state (auto-sets mode: `firstperson` < 0.1 km altitude, `orbit` otherwise)

## Project Structure

### Backend (`backend/`)
- `GeoTime.Core/Models/` — Enums, data models (RockType, SoilOrder, PlateInfo, etc.)
- `GeoTime.Core/Proc/` — Seeded PRNG (Xoshiro256\*\*), simplex noise, planet generator
- `GeoTime.Core/Kernel/` — Event bus, simulation clock, event log, snapshot manager
- `GeoTime.Core/Engines/` — All geological simulation engines (tectonic, erosion, glacial, weathering, pedogenesis, climate, weather, vegetation, biomatter, cross-section)
- `GeoTime.Core/SimulationOrchestrator.cs` — Top-level simulation manager
- `GeoTime.Api/Program.cs` — REST API endpoints
- `GeoTime.Tests/` — xUnit backend tests (412 passing in latest full proof)
- `GeoTime.Diagnostic/` — optional console utility for GPU compute mode/device diagnostics

### Frontend (`src/`)
- `src/api/backend-client.ts` — REST API client for backend communication
- `src/shared/types.ts` — Shared TypeScript type definitions
- `src/render/` — Three.js globe renderer, icosphere mesh, cross-section Canvas 2D renderer
- `src/ui/` — DOM-based app shell with HUD, sidebar, inspect panel, timeline
- `tests/` — Vitest unit tests (430 passing in latest full proof)
- `e2e/` — Playwright browser tests (102 passing in latest full proof)

## API Reference

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/planet/generate` | Generate new planet (body: `{ seed: number }`) |
| POST | `/api/simulation/advance` | Advance simulation (body: `{ deltaMa: number }`) |
| GET | `/api/simulation/time` | Get current time and seed |
| GET | `/api/state/heightmap` | Get height map array |
| GET | `/api/state/platemap` | Get plate assignment map |
| GET | `/api/state/temperaturemap` | Get temperature map |
| GET | `/api/state/precipitationmap` | Get precipitation map |
| GET | `/api/state/biomassmap` | Get biomass map |
| GET | `/api/state/biomattermap` | Get biomatter density map |
| GET | `/api/state/organiccarbonmap` | Get organic carbon map |
| GET | `/api/state/plates` | Get plate info |
| GET | `/api/state/hotspots` | Get hotspot info |
| GET | `/api/state/atmosphere` | Get atmospheric composition |
| GET | `/api/state/events?count=N` | Get geological event log |
| GET | `/api/state/inspect/:cellIndex` | Inspect a grid cell |
| POST | `/api/crosssection` | Get cross-section profile |

## Migration

See [migration.md](migration.md) for the full migration plan from the original all-frontend TypeScript architecture to the current client-server design.

