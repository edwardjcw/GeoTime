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
- **Performance**: Adaptive tick rate, deterministic seeded PRNG
- **UI**: Globe viewport, collapsible sidebar, layer overlays, geological timeline, inspect panel, URL seed sharing

## Getting Started

### Backend (C# .NET)

```bash
cd backend
dotnet restore
dotnet build
dotnet run --project GeoTime.Api   # Starts API server on http://localhost:5000
dotnet test                         # Run 75 unit tests
```

### Frontend (TypeScript/Vite)

```bash
npm install
npm run dev        # Start dev server (connects to backend at localhost:5000)
npm run build      # Production build (tsc + vite)
npm run test       # Run unit tests (Vitest)
npx playwright test  # Run E2E tests (Playwright)
```

Set the backend URL via environment variable if needed:
```bash
VITE_API_BASE=http://localhost:5000 npm run dev
```

## Project Structure

### Backend (`backend/`)
- `GeoTime.Core/Models/` — Enums, data models (RockType, SoilOrder, PlateInfo, etc.)
- `GeoTime.Core/Proc/` — Seeded PRNG (Xoshiro256\*\*), simplex noise, planet generator
- `GeoTime.Core/Kernel/` — Event bus, simulation clock, event log, snapshot manager
- `GeoTime.Core/Engines/` — All geological simulation engines (tectonic, erosion, glacial, weathering, pedogenesis, climate, weather, vegetation, biomatter, cross-section)
- `GeoTime.Core/SimulationOrchestrator.cs` — Top-level simulation manager
- `GeoTime.Api/Program.cs` — REST API endpoints
- `GeoTime.Tests/` — 75 xUnit tests across 11 test files

### Frontend (`src/`)
- `src/api/backend-client.ts` — REST API client for backend communication
- `src/shared/types.ts` — Shared TypeScript type definitions
- `src/render/` — Three.js globe renderer, icosphere mesh, cross-section Canvas 2D renderer
- `src/ui/` — DOM-based app shell with HUD, sidebar, inspect panel, timeline
- `tests/` — Vitest unit tests
- `e2e/` — Playwright browser tests

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
| GET | `/api/state/plates` | Get plate info |
| GET | `/api/state/hotspots` | Get hotspot info |
| GET | `/api/state/atmosphere` | Get atmospheric composition |
| GET | `/api/state/events?count=N` | Get geological event log |
| GET | `/api/state/inspect/:cellIndex` | Inspect a grid cell |
| POST | `/api/crosssection` | Get cross-section profile |

## Migration

See [migration.md](migration.md) for the full migration plan from the original all-frontend TypeScript architecture to the current client-server design.

