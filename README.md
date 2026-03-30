# GeoTime

A planetary geology simulator built with TypeScript, Vite, and Three.js. GeoTime models the full 4.5 billion year evolution of an Earth-like planet — from plate tectonics and volcanic eruptions to erosion, climate, weather, vegetation, and soil formation.

## Features

- **Plate Tectonics**: Voronoi-based plates with Euler rotation, convergent/divergent/transform boundaries, subduction, continental collision, rift valleys
- **Volcanism**: Stratovolcanoes, shield volcanoes, submarine volcanism with eruption scaling
- **Stratigraphy**: Per-cell 64-layer geological stacks recording rock type, age, and deformation
- **Surface Processes**: D∞ fluvial erosion, glacial erosion with ELA, aeolian/chemical weathering, USDA soil orders
- **Climate System**: 3-cell atmospheric circulation, Milankovitch cycles, greenhouse forcing, ice-albedo feedback
- **Weather**: Frontal systems, tropical cyclones, orographic precipitation, 10 cloud genera
- **Vegetation**: Miami Model NPP, biomass accumulation, stochastic forest fires, albedo feedback
- **Cross-Section Viewer**: Interactive great-circle cross-sections with split-scale rendering
- **Performance**: Adaptive tick rate, sparse delta snapshots, deterministic seeded PRNG
- **UI**: Globe viewport, collapsible sidebar, layer overlays, geological timeline, inspect panel, URL seed sharing

## Getting Started

```bash
npm install
npm run dev        # Start dev server
npm run build      # Production build (tsc + vite)
npm run test       # Run unit tests (Vitest)
npx playwright test  # Run E2E tests (Playwright)
```

## Architecture

- `src/shared/types.ts` — All enums, interfaces, 14-view SharedArrayBuffer layout (~11.8 MB)
- `src/kernel/` — Event bus, simulation clock, event log, snapshot manager
- `src/proc/` — Seeded PRNG (xoshiro256\*\*), simplex noise, planet generator
- `src/geo/` — Tectonic, erosion, glacial, weathering, pedogenesis, climate, weather, vegetation engines
- `src/render/` — Three.js globe renderer, icosphere mesh, cross-section Canvas 2D renderer
- `src/ui/` — DOM-based app shell with HUD, sidebar, inspect panel, timeline
- `tests/` — 25 Vitest test files (351 tests)
- `e2e/` — Playwright browser tests (29 tests)

