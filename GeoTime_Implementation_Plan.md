# GeoTime — Planetary Geological Simulation Engine
## Comprehensive Implementation Plan v1.2
**Multi-Agent Architecture · Earth-Analog Iteration**

---

## Table of Contents

1. [Vision & Scope](#1-vision--scope)
2. [Agent Roster](#2-agent-roster)
3. [Inter-Agent Interfaces](#3-inter-agent-interfaces)
4. [Implementation Phases](#4-implementation-phases)
5. [Cross-Section Viewer System](#5-cross-section-viewer-system)
6. [Geological Layer Catalog](#6-geological-layer-catalog)
7. [Soil Type Catalog](#7-soil-type-catalog)
8. [Cloud Type Catalog](#8-cloud-type-catalog)
9. [Atmospheric & Climate Modeling](#9-atmospheric--climate-modeling)
10. [Biomatter System](#10-biomatter-system)
11. [Recommended Technology Stack](#11-recommended-technology-stack)
12. [Risk Register](#12-risk-register)
13. [Per-Agent Briefing Notes](#13-per-agent-briefing-notes)

---

## 1. Vision & Scope

GeoTime is a real-time, interactive planetary simulation visualizer. Given an Earth-like planet with a randomized initial state, it simulates all major geological, surface, and atmospheric processes across geological time — rendering them with physically-based textures, dynamic weather, and full temporal navigation. The user can observe a planet evolve from molten rock to complex continents over billions of years, then zoom in and draw a cross-section anywhere on the surface to see the full stratigraphic record down to the core.

### Core Design Principle

Every system must be loosely coupled via message-passing interfaces. Agents own discrete domains. The Simulation Kernel drives all of them on a shared timeline tick. No agent directly mutates another agent's canonical state — it emits an event, and the owning agent responds.

### Iteration 1 Deliverables

- Randomized, seeded Earth-analog planet
- Full plate tectonic simulation with all boundary types
- All geological surface processes (erosion, rivers, glaciation, volcanism)
- Complete stratigraphic layer tracking with tilting, folding, and unconformities
- **Cross-section viewer**: user draws a line on the surface, sees a vertical slice from atmosphere to core, with all geological layers rendered at true dip angles
- **Label toggle**: each striation/layer labeled by rock type, soil type, age, and formation name
- All 12 USDA soil orders represented with correct formation conditions
- All cloud genera and species represented in the atmosphere, driven by the climate simulation
- Atmospheric general circulation and weather systems
- Day/night cycle with solar angle
- Physically-based terrain texturing by rock type, soil type, and biome
- Optional vegetation growth modeling (feature-flagged)
- Simple biomatter beyond plants: microbes, plankton, reef organisms — influencing ocean chemistry, biogenic sedimentation, atmospheric composition, and petroleum source-rock formation (feature-flagged)
- Temporal scrubber: forward and backward through geological time
- Free-orbit camera with zoom to any surface location

### Out of Scope (v1)

- Non-Earth-analog planet types
- Multi-star systems or orbital mechanics
- Evolutionary biology / complex fauna (simple biomatter such as microbes, plankton, and reef organisms are in scope — see Section 10)
- Multiplayer or collaborative editing
- Real-time surface editing by user
- Civilization placement

---

## 2. Agent Roster

Six specialized agents own discrete simulation and rendering domains. A seventh — the Simulation Kernel — orchestrates the shared timeline. All agents communicate via a typed event bus backed by SharedArrayBuffer for zero-copy state sharing.

| ID | Name | Domain |
|----|------|--------|
| AGENT-KERNEL | Simulation Kernel | Clock, event bus, snapshots, rewind, worker orchestration |
| AGENT-PROC | Procedural Generator | Initial planet seed: plates, topography, ocean/continent ratio, hotspots |
| AGENT-GEO | Geology Engine | Tectonics, volcanism, erosion, rivers, glaciation, stratigraphy |
| AGENT-ATMO | Atmosphere & Climate | GCM, weather systems, clouds, ice ages, day/night solar forcing |
| AGENT-RENDER | Rendering & Visuals | GPU pipeline, PBR terrain, atmospheric scattering, cloud layer, LOD |
| AGENT-SECTION | Cross-Section System | Profile path sampling, layer geometry reconstruction, label rendering |
| AGENT-UI | UI & Interaction | Camera, time controls, HUD, info overlays, draw tool, settings |

### AGENT-KERNEL

Owns the global simulation clock and event bus. Responsible for:
- `SimClock.advance(dt_years)` — the sole entry point for time progression
- Distributing ticks to all agents at appropriate rates (geology runs at coarser Δt than weather)
- Maintaining a Web Worker pool for compute-heavy agents
- Producing compressed state snapshots at configurable intervals for rewind support
- Enforcing the canonical shared data schema

### AGENT-PROC

One-time generator. Activated at planet creation. Responsible for:
- Voronoi-based tectonic plate seed generation (8–20 plates)
- Simplex-noise initial height field on an icosphere
- Ocean/continent ratio parameter (target 70% ocean ± 15%)
- Initial mantle temperature field and convection cell seeding
- Mantle plume / hotspot placement (2–5 random locations)
- Atmospheric composition initialization (N₂, O₂, CO₂, H₂O baseline)
- All outputs must be deterministic given the same uint32 seed

### AGENT-GEO

Owns all subsurface and surface geological processes:
- **Plate tectonics**: Euler rotation per plate, boundary classification, subduction, rifting, collision
- **Volcanism**: All volcano types (see Section 6), eruption scheduling, degassing
- **Erosion**: Hydraulic, glacial, aeolian, chemical weathering
- **Fluvial systems**: Drainage networks, river incision, alluvial fans, deltas
- **Stratigraphy**: Layer stack per grid cell — each layer records rock type, age, thickness, dip angle, and deformation history
- **Soil formation**: Pedogenesis driven by parent rock, climate, vegetation, and time (see Section 7)

### AGENT-ATMO

Owns all atmosphere and climate state:
- Simplified GCM: 3-box energy balance model upgradable to shallow-water spectral model
- Solar forcing: latitude × declination × Milankovitch orbital parameters
- Differential land/ocean heating
- Cloud field generation per cloud type (see Section 8)
- Precipitation, temperature, and wind vector fields (consumed by GEO and RENDER)
- Greenhouse gas budget, ice-albedo feedback, ice age onset/recovery

### AGENT-RENDER

All GPU-side work:
- Icosphere mesh with adaptive quad-tree LOD
- Height-map displacement shader
- Physically-based rock type texturing (rock age, composition, weathering grade)
- Soil type texture layer blended over bedrock where soil cover exists
- Biome texture blending using Whittaker classification (temperature × precipitation)
- Atmospheric scattering: Rayleigh + Mie shaders
- Cloud layer: volumetric procedural clouds by type, animated from wind vectors
- Day/night terminator with axial tilt and orbital position
- Volcano growth meshes and lava flow shaders
- Snow/ice accumulation layer

### AGENT-SECTION

Cross-section viewer (new in v1.1):
- Listens for a CROSS_SECTION_PATH event from UI containing a polyline on the sphere surface
- Samples the stratigraphy stack at N points along the path
- Reconstructs layer geometry in 2D profile space, preserving true dip angles, folds, faults, and unconformities
- Renders the profile as an interactive 2D panel alongside the globe
- Manages the label toggle: each layer labeled with rock type, soil horizon (if applicable), age, and formation name
- Handles layers that pinch out, are truncated by erosion, or are overturned

### AGENT-UI

Camera and interaction layer:
- Free-orbit arcball camera with zoom-to-cursor and inertia damping
- Draw tool: user clicks/taps two or more points on the globe to define a cross-section path
- Cross-section panel: toggleable side panel or overlay showing the SECTION output
- Time scrubber: calls `KERNEL.seekTo(t)` only — never advances clock directly
- Layer toggle panel: tectonic plates, precipitation, temperature, soil type, cloud type overlays
- Info inspector: click globe → ray-cast → sample all state maps → display report card
- Settings: vegetation toggle, label toggle, performance quality slider

---

## 3. Inter-Agent Interfaces

All agents communicate through the typed event bus. The following are the canonical shared data structures. All live in SharedArrayBuffers allocated by KERNEL on startup.

### Shared State Maps (all N×N grids, default 2048×2048, spherically projected)

| Buffer | Type | Owner | Description |
|--------|------|-------|-------------|
| `heightMap` | Float32Array | GEO | Surface elevation in meters above sea level |
| `crustThicknessMap` | Float32Array | GEO | Crustal thickness in km (used for isostasy) |
| `rockTypeMap` | Uint8Array | GEO | Surface rock type enum (see Section 6) |
| `rockAgeMap` | Float32Array | GEO | Age of surface rock in Ma (million years) |
| `plateMap` | Uint16Array | GEO | Plate ID per cell, max 255 plates |
| `soilTypeMap` | Uint8Array | GEO | USDA soil order per cell (see Section 7) |
| `soilDepthMap` | Float32Array | GEO | Soil depth in meters |
| `temperatureMap` | Float32Array | ATMO | Mean annual surface temperature in °C |
| `precipitationMap` | Float32Array | ATMO | Annual precipitation in mm/yr |
| `windUMap` | Float32Array | ATMO | Wind U-component (east-west) in m/s |
| `windVMap` | Float32Array | ATMO | Wind V-component (north-south) in m/s |
| `cloudTypeMap` | Uint8Array | ATMO | Dominant cloud genus per cell (see Section 8) |
| `cloudCoverMap` | Float32Array | ATMO | Cloud fraction 0.0–1.0 per cell |
| `biomassMap` | Float32Array | VEG (optional) | Vegetation biomass kg C/m², feature-flagged |
| `biomatterMap` | Float32Array | GEO (optional) | Non-plant biomatter density kg C/m² (microbes, plankton, reef), feature-flagged |
| `organicCarbonMap` | Float32Array | GEO (optional) | Buried organic carbon kg C/m² (kerogen / petroleum precursor), feature-flagged |

### Stratigraphy Stack (separate data structure, per-cell)

Each cell maintains a stack of `StratigraphicLayer` records. This is the most critical data structure for the cross-section viewer.

```
StratigraphicLayer {
  rockType:       Uint8      // enum: see Section 6
  age_deposited:  Float32    // Ma when this layer was deposited or emplaced
  thickness:      Float32    // current thickness in meters (compressed by burial)
  dip_angle:      Float32    // degrees from horizontal (0 = flat, 90 = vertical)
  dip_direction:  Float32    // azimuth of downslope direction (0–360°)
  deformation:    Uint8      // enum: UNDEFORMED, FOLDED, FAULTED, METAMORPHOSED, OVERTURNED
  unconformity:   Bool       // true if erosional gap exists above this layer
  soilHorizon:    Uint8      // 0 = none, else soil order enum (topmost layer only)
  formationName:  Uint16     // index into a string table of formation names
}
```

The stack is ordered from youngest (top) to oldest (bottom). The AGENT-SECTION samples this stack along the cross-section path and reconstructs true geometry.

### Event Bus — Key Events

| Event | Payload | Emitter | Consumers |
|-------|---------|---------|-----------|
| `TICK` | `{ t, dt }` | KERNEL | All agents |
| `VOLCANIC_ERUPTION` | `{ lat, lon, type, vei, t }` | GEO | RENDER, ATMO |
| `PLATE_COLLISION` | `{ plateA, plateB, t, length_km }` | GEO | RENDER |
| `PLATE_RIFT` | `{ plateId, t, length_km }` | GEO | RENDER |
| `ICE_AGE_ONSET` | `{ t, severity }` | ATMO | GEO, RENDER |
| `ICE_AGE_END` | `{ t }` | ATMO | GEO, RENDER |
| `CROSS_SECTION_PATH` | `{ points: [{lat,lon}], t }` | UI | SECTION |
| `CROSS_SECTION_READY` | `{ profileData, labels }` | SECTION | UI, RENDER |
| `LABEL_TOGGLE` | `{ visible: bool }` | UI | SECTION |
| `SEEK_TO` | `{ t }` | UI | KERNEL |
| `SNAPSHOT_READY` | `{ snapshotId, t }` | KERNEL | UI |
| `BIOMATTER_UPDATE` | `{ t, totalBiomatter, meanProductivity }` | GEO | RENDER, ATMO |
| `OXYGENATION_EVENT` | `{ t, deltaO2_pct }` | GEO | ATMO, RENDER |
| `PETROLEUM_DEPOSIT` | `{ cellIndex, t, thickness_m }` | GEO | SECTION |

### SimClock

- `SimClock.t`: current simulation time in years (0 = present, negative = past, e.g., -4.5e9 = 4.5 Ga)
- `SimClock.rate`: simulation years per real second (user-adjustable, range 1 yr/s to 100 Myr/s)
- `SimClock.advance(dt_years)`: called each animation frame; distributes `TICK` events to all agents
- `SimClock.seekTo(t)`: loads nearest snapshot, then replays forward to exact `t`

---

## 4. Implementation Phases

### Phase 1 — Foundation (Weeks 1–3)

**Goal**: Shared architecture, planet generation, static globe rendering.

**AGENT-KERNEL**
- Typed event bus with pub/sub and topic filtering
- SharedArrayBuffer allocation and layout for all state maps
- Web Worker pool with message routing
- `SimClock` implementation with variable rate
- Binary snapshot format (Flatbuffers + Brotli compression)

**AGENT-PROC**
- Seeded PRNG (xoshiro256**)
- Voronoi plate generation with Lloyd relaxation
- Simplex noise height field on icosphere (4 octaves)
- Ocean fraction targeting, sea level calibration
- Mantle plume hotspot seeding
- Baseline atmospheric composition assignment

**AGENT-RENDER**
- Icosphere mesh with quad-tree LOD (max depth 18 = ~2m triangles at surface)
- Height-map displacement in vertex shader
- Basic PBR material pipeline (albedo, normal, roughness, metallic)
- Free-orbit arcball camera with inertia
- Placeholder sun directional light

**AGENT-UI**
- App shell: globe viewport, collapsible sidebar, top HUD
- New Planet button (triggers PROC)
- Seed display and copy control
- FPS and triangle count performance overlay

---

### Phase 2 — Plate Tectonics & Volcanism (Weeks 4–7)

**Goal**: The planet now evolves. Mountains rise, ocean crust subducts, volcanoes grow.

**AGENT-GEO: Tectonics**
- Euler rotation vectors per plate, updated each tectonic tick
- Boundary classification at every cell edge: convergent, divergent, transform
- Subduction zone modeling: oceanic plate sinks under continental, forming trench + volcanic arc
- Continental collision: crustal thickening, fold-and-thrust belt formation
- Rift valley opening: graben formation, eventual seafloor spreading
- Isostatic adjustment: crustal thickness drives surface elevation
- Hotspot tracks: fixed mantle plumes pierce moving plates, creating island chains

**AGENT-GEO: Volcanism**
- Shield volcano growth model: low silica, effusive, broad profile, hotspot-linked
- Stratovolcano (composite cone): subduction-arc setting, explosive, steep flanks
- Cinder cone: monogenetic, short-lived, rift or arc setting
- Caldera formation: collapse after large magma chamber evacuation
- Flood basalt (large igneous province): areal eruption over millions of years at rift initiation
- Submarine volcanism at mid-ocean ridges: pillow basalt, hydrothermal vents
- Each eruption records a new StratigraphicLayer (basalt, rhyolite, pyroclastic, etc.)
- Volcanic degassing: CO₂ and SO₂ injected into ATMO's greenhouse gas budget

**AGENT-GEO: Stratigraphy Initialization**
- Each plate's initial crust assigned a base stack of Precambrian basement layers
- New layers appended as volcanic, sedimentary, or metamorphic events occur
- Dip angle updated when compression or extension deforms a cell

**AGENT-RENDER: Rock & Volcano Visuals**
- Rock type texture atlas: textures keyed to rockType enum
- Volcano mesh procedural growth scaled to eruption volume
- Lava flow surface shader with incandescent glow attenuating to cooled basalt
- Mountain snow-cap elevation threshold from ATMO temperature field
- Oceanic trench rendering from negative height values

**AGENT-KERNEL: Temporal System**
- Variable time scale: 1 yr → 100 Myr per real second
- Keyframe snapshots at configurable intervals (default every 10 Myr sim-time)
- Forward scrubbing: advance clock
- Backward scrubbing: load nearest earlier snapshot + fast-forward to target time
- Geological event log: eruptions, collisions, rifts recorded with timestamps

---

### Phase 3 — Erosion, Rivers & Surface Processes (Weeks 8–11)

**Goal**: Tectonics builds terrain; erosion carves it. Rivers, glaciers, and weathering give the surface its character.

**AGENT-GEO: Fluvial Erosion**
- Hydraulic erosion: particle-based simulation adapted to spherical height field (Müller 2017 approach)
- Stream power law for river incision: `E = K · A^m · S^n`
- D∞ flow routing for drainage network computation
- River channel incision: V-shaped valley formation, meander development
- Alluvial fan deposition at mountain fronts
- Delta formation at river mouths: distributary networks, progradation into ocean
- Stream capture: when divide migration connects two watersheds
- Sediment budget: eroded material tracked to depocenters

**AGENT-GEO: Glacial Erosion**
- Glacier extent driven by ATMO temperature field: equilibrium line altitude calculation
- Glacial erosion: quarrying (plucking) + abrasion proportional to sliding velocity
- U-shaped valley formation in glacial troughs
- Cirque, arête, and horn development at glaciated headwalls
- Fjord formation where glaciers reach sea level
- Moraine deposition: terminal, lateral, medial
- Outwash plain formation beyond glacier terminus
- Ice sheet mass balance: accumulation vs. ablation from ATMO precipitation

**AGENT-GEO: Aeolian & Chemical Weathering**
- Aeolian dune field formation in arid zones (low precipitation, high wind velocity)
- Sand dune types: barchan, transverse, linear, star — determined by wind regime
- Chemical weathering rate: exponential function of temperature and moisture
- Karst formation: carbonate rock dissolution forming sinkholes and cave systems
- Laterite formation: deep chemical weathering in tropical humid zones
- Each weathered product appended as a new StratigraphicLayer (regolith, laterite, caliche, etc.)

**AGENT-GEO: Pedogenesis (Soil Formation)**
- Soil formation activated where: surface rock exposed, temperature > -20°C, some moisture present
- Rate depends on: parent rock type, climate (JENNY's CLORPT: climate, organisms, relief, parent material, time)
- Each USDA soil order has specific formation criteria (see Section 7)
- `soilTypeMap` and `soilDepthMap` updated each soil-formation tick
- Soil order written into the topmost StratigraphicLayer for cross-section labeling

**AGENT-RENDER: Surface Detail**
- River mesh: tube geometry scaled to stream order (Strahler number)
- Flow direction visualization option (velocity coloring)
- Delta geometry: fan of distributary river segments
- Glacier surface texture with flow-line striations
- Canyon wall rock layer striping derived from the stratigraphy stack
- Dynamic normal map detail layer generated from erosion micro-relief
- Soil texture blended over bedrock where `soilDepthMap > 0.1 m`

---

### Phase 4 — Atmosphere, Climate & Weather (Weeks 12–15)

**Goal**: The atmosphere comes alive. Climate zones drive geology; geology reshapes climate. All cloud types appear.

**AGENT-ATMO: General Circulation**
- 3-cell model: Hadley (0–30°), Ferrel (30–60°), Polar (60–90°) per hemisphere
- Solar insolation: `I = S₀ · cos(θ) · (1 - α)` where θ = solar zenith angle, α = albedo
- Differential heating: land heats and cools faster than ocean (heat capacity ratio ~4:1)
- Trade winds, westerlies, polar easterlies emerge from Coriolis + pressure gradient
- ITCZ position migrates with the solar declination angle, influenced by SST
- Monsoon systems: land-ocean pressure reversal at seasonal boundaries
- Ocean thermohaline circulation: simplified box model (3 boxes: tropical, deep, polar)
- Jet stream position tracks polar front; guides mid-latitude storm tracks

**AGENT-ATMO: Ice Ages & Forcing**
- Milankovitch cycle parameters: eccentricity (100 kyr), obliquity (41 kyr), precession (23 kyr)
- Greenhouse forcing: `ΔT = λ · ln(CO₂ / CO₂_ref)` where λ = climate sensitivity ~3°C per doubling
- CO₂ input: volcanic degassing from GEO; CO₂ drawdown by silicate weathering (Urey reaction)
- Ice-albedo feedback: ice reflects more solar radiation, amplifying cooling
- Snowball Earth threshold: if equatorial temperature drops below -10°C, trigger glacial runaway
- Ice age onset/recovery emits events for RENDER (snow/ice shader) and GEO (glacial erosion activation)

**AGENT-ATMO: Weather Systems**
- Frontal systems: cold and warm fronts generated along the polar jet path
- Fronts propagate eastward at ~30–60 km/hr simulation-scaled speed
- Low-pressure cyclones and high-pressure anticyclones placed stochastically along fronts
- Tropical cyclone spawning: sea surface temperature > 26°C, low wind shear, sufficient Coriolis
- Hurricane track: steered by trade winds and then the westerlies
- Precipitation field updated from frontal/cyclonic lifting + orographic effect (windward vs. leeward)
- Orographic precipitation: windward slope multiplier from terrain slope angle and wind speed

**AGENT-ATMO: Cloud Generation (see Section 8 for full catalog)**
- Cloud fields generated per cloud type based on atmospheric instability, moisture, and lift mechanism
- Each cloud type has altitude range, coverage fraction, and optical thickness
- `cloudTypeMap` records dominant genus per cell; `cloudCoverMap` records fraction
- Cloud fields update at a weather timescale (faster than geology, slower than rendering)
- Cloud motion driven by windUMap and windVMap at appropriate altitude

**AGENT-RENDER: Atmosphere & Clouds**
- Rayleigh scattering: wavelength-dependent sky color (blue sky, red sunrise/sunset)
- Mie scattering: sun corona, haze effects near horizon
- Volumetric cloud layer: separate mesh layer at altitude, opacity from cloudCoverMap
- Cloud type texture atlas: different shaders/textures per cloud genus
- Animated cloud motion from wind vectors
- Day/night terminator: great-circle boundary computed from axial tilt + orbital position
- Auroral oval: particle emission effect at high latitudes when solar activity flag active
- Hurricane spiral cloud geometry on tropical cyclone events
- Biome texture atlas updated: 12 biomes from Whittaker diagram (temperature × precipitation)
- Seasonal leaf color shift in deciduous forest biomes

---

### Phase 5 — Cross-Section Viewer (Weeks 13–15, parallel with Phase 4)

**Goal**: User draws a line on the surface and sees a geologically accurate cross-section.

Full specification in Section 5. Key deliverables:

**AGENT-SECTION**
- Polyline sampler: N evenly-spaced sample points along great-circle path
- Stratigraphy stack retrieval at each sample point from GEO's data
- 2D profile reconstruction with true dip angles, fold shapes, unconformities
- Layer pinch-out and truncation handling (erosional surfaces clip layers)
- Fault plane rendering where displacement is recorded
- Label generation: rock type, age, soil horizon ID, formation name string
- Moho discontinuity visualization (crust/mantle boundary from crustThicknessMap)
- Core/mantle boundary: fixed at Earth-radius scale (2891 km depth)
- Interactive panel: zoom and pan within cross-section; hover for layer detail

**AGENT-UI: Draw Tool**
- Pencil/polyline mode activates on toolbar button
- User clicks points on globe surface to define path; path shown as draped line
- On path completion (double-click or close button): emits CROSS_SECTION_PATH event
- Cross-section panel slides in from side or bottom
- Label toggle button in panel header
- Export cross-section as PNG option

---

### Phase 6 — Vegetation & Polish (Weeks 16–18)

**Goal**: Optional vegetation, full integration testing, performance optimization, release candidate.

**Optional Vegetation Module (feature-flagged)**
- Net primary productivity (NPP) from temperature and precipitation: Miami Model approximation
- Biomass accumulation rate scaled to NPP; biomass cleared by glaciation or desertification
- Biome-appropriate tree density written to biomassMap
- Forest fire stochastic model: dry season + high biomass → fire probability
- Vegetation albedo feedback: forests darker than grassland → warming feedback
- Grass coverage on slopes where soil depth sufficient and precipitation > 250 mm/yr

**Performance Pass**
- WASM (Rust) for erosion particle inner loops
- WebGPU compute shaders for parallel erosion on GPU
- Tile-based streaming for height map at max LOD
- Sparse delta snapshots: only cells that changed since last keyframe stored
- Brotli compression of snapshot deltas
- Adaptive tick rate: when paused, reduce to 0; at max speed, maximize compute budget

**Integration Validation**
- Full 4.5 Gyr simulation smoke test on 3 random seeds
- Continent distribution plausibility: check that at least 2 major continents form
- Climate zone coverage: all major biomes must appear on at least one seed
- Erosion volume conservation: total sediment mass must balance eroded rock mass
- Stratigraphy consistency: no layer can appear above a younger unconformity
- Cloud type coverage: all 10 genera must appear during a 1 Myr weather simulation
- Soil type coverage: at least 8 of 12 USDA orders must appear on each full-run planet
- Cross-browser WebGPU test suite; WebGL 2 fallback validation

**UI Polish**
- Click-to-inspect: ray-cast → full location report (elevation, rock type, soil order, biome, climate)
- Geological timeline: horizontal strip below globe showing major events (eruptions, collisions, ice ages)
- Layer overlay panel: tectonic plates, temperature, precipitation, soil type, cloud type (toggleable)
- Planet URL sharing: seed encoded in URL fragment
- Timelapse export: render to WebM at 30 fps

---

### Phase 7 — Biomatter (Weeks 19–21)

**Goal**: Simple non-plant biomatter that reshapes ocean chemistry, sedimentation, atmosphere, and produces petroleum source rocks. Feature-flagged, building on the vegetation module from Phase 6.

**AGENT-GEO: Biomatter Engine (feature-flagged)**
- **Microbial mats / stromatolites**: earliest life form; appear on shallow marine cells when temperature > 10°C and sunlight available; produce laminated carbonate structures recorded in stratigraphy as SED_LIMESTONE or SED_CHERT
- **Cyanobacteria**: oxygenic photosynthesis; emit OXYGENATION_EVENT when cumulative biomatter exceeds threshold; drive the Great Oxygenation Event analog, converting atmosphere from anoxic to oxic over hundreds of Myr
- **Marine plankton (phytoplankton & zooplankton)**: productivity driven by ocean cell temperature (10–25°C optimum), nutrient upwelling proximity, and sunlight; phytoplankton contribute O₂ to ATMO; zooplankton shells produce biogenic sediment (SED_CHALK from coccoliths, SED_CHERT from radiolarians/diatoms, SED_DIATOMITE)
- **Reef organisms (corals, sponges)**: colonize warm shallow marine cells (18–30°C, depth < 50 m); build carbonate reef structures that modify local heightMap and deposit SED_LIMESTONE layers; reef growth rate proportional to temperature and water clarity
- **Benthic organisms**: seafloor biomatter in deep marine cells; bioturbation mixes upper sediment layers, increasing weathering rate of top stratigraphy layer
- **Fungi & decomposers**: terrestrial cells with soil depth > 0.1 m and organic litter; accelerate pedogenesis rate by 20–40%; increase soil organic carbon content

**AGENT-GEO: Petroleum Source Rocks**
- **Organic carbon burial**: when biomatter (marine plankton, microbial mats) dies in anoxic marine basins, organic carbon accumulates in organicCarbonMap
- **Kerogen formation**: buried organic carbon at depth > 2 km and temperature 60–120°C (oil window) converts to kerogen; recorded as a new StratigraphicLayer with rockType SED_OIL_SHALE
- **Petroleum migration**: simplified model — kerogen layers capped by impermeable shale or evaporite form petroleum trap; flagged in stratigraphy for cross-section display
- **Coal analogs**: terrestrial peat (SED_PEAT) from high-biomass vegetation zones, when buried to depth > 1 km, converts to SED_COAL; rate depends on burial depth and time

**AGENT-ATMO: Biomatter ↔ Atmosphere Feedback**
- Cyanobacteria and phytoplankton O₂ production increases atmospheric O₂ concentration over geological time
- Methane production by anaerobic microbes in anoxic wetlands and ocean sediments adds CH₄ to greenhouse gas budget
- CO₂ drawdown by marine biomatter (biological pump): plankton fix CO₂ at surface, organic matter sinks and sequesters carbon in deep ocean sediment
- Atmospheric O₂ level gates: below 2% O₂, only anaerobic biomatter active; above 2%, aerobic organisms enabled; above 15%, terrestrial organisms and fire possible

**AGENT-GEO: Biogenic Sedimentation**
- Coccolith ooze → SED_CHALK: deposited in deep marine cells when plankton productivity is high
- Radiolarian / diatom ooze → SED_CHERT / SED_DIATOMITE: deposited in cold, nutrient-rich upwelling zones
- Coral reef limestone → SED_LIMESTONE: deposited in warm shallow marine cells with active reef organisms
- Stromatolite layers → SED_LIMESTONE: laminated carbonate in shallow tidal flat cells with microbial mats
- Phosphorite → SED_PHOSPHORITE: deposited in upwelling zones where organic matter concentration is high
- Banded iron formation → SED_IRONSTONE: deposited in Archean–Proterozoic oceans when dissolved iron reacts with newly produced O₂ from cyanobacteria

**AGENT-RENDER: Biomatter Visuals**
- biomatterMap overlay: toggleable heat-map layer showing non-plant biomatter density
- Reef structures: subtle heightMap bump and distinct texture on warm shallow marine cells
- Stromatolite texture on ancient shallow marine shelf cells
- organicCarbonMap overlay: dark organic-rich layer visible in cross-section

**AGENT-UI: Biomatter Controls**
- Biomatter toggle in settings panel (feature-flagged, like vegetation)
- Cell inspection expanded: biomatter density, organic carbon, reef presence
- Layer overlay: biomatter density map (marine + terrestrial combined)

---

## 5. Cross-Section Viewer System

### Overview

The cross-section viewer is one of the most distinctive features of GeoTime v1. When a user draws a line across any part of the globe, the system renders a geologically accurate vertical profile from the surface down to the Earth's core, showing all stratigraphic layers at their true orientation — accounting for tilting, folding, faulting, and unconformities.

### User Interaction Flow

1. User activates the **Draw Cross-Section** tool in the toolbar
2. User clicks two or more points on the globe surface to define the profile path
3. A draped line appears on the 3D globe surface showing the selected transect
4. User double-clicks or presses Enter to confirm
5. AGENT-UI emits a `CROSS_SECTION_PATH` event with the array of `{lat, lon}` points
6. AGENT-SECTION processes the path and emits `CROSS_SECTION_READY`
7. A 2D panel slides into the UI showing the cross-section
8. The user can toggle layer labels on/off with the **Labels** button
9. The user can hover over any layer in the 2D panel to see a detailed popup

### Technical Implementation

#### Path Sampling (AGENT-SECTION)

- Interpolate the polyline as a sequence of great-circle arcs between user-specified points
- Sample at N equally-spaced arc-length intervals along the full path (N = 512 default, configurable)
- For each sample point, retrieve:
  - The full `StratigraphicLayer[]` stack from AGENT-GEO's stratigraphy data
  - The current surface height from `heightMap`
  - The crust thickness from `crustThicknessMap`
  - The soil type and depth from `soilTypeMap` and `soilDepthMap`

#### Layer Geometry Reconstruction

Each layer at each sample point has:
- A **thickness** and **depth below surface**
- A **dip angle** and **dip direction** — these define how far the layer boundary shifts horizontally per unit of depth
- A **deformation flag** — FOLDED layers use a sinusoidal perturbation of the dip angle along strike

The 2D cross-section is rendered as a profile where:
- **X-axis** = distance along the surface path (in km)
- **Y-axis** = elevation/depth (surface at top, core at bottom, scaled logarithmically below 100 km)

For each sample column x:
- Layer boundaries are drawn at their computed depths
- The horizontal offset of each boundary from vertical is: `Δx = thickness / tan(dip_angle)` projected into the profile plane
- Layer contacts are connected between adjacent sample columns to produce the layer polygon

**Unconformities** are rendered as a distinct visual break (hatched line) between layers where `unconformity = true`, indicating an erosional gap in the record.

**Faults** are detected where a layer is present in column x but absent in column x+1 due to displacement; rendered as a thick offset line with the fault type annotation.

**Overturned layers** (dip > 90°) are rendered with reversed layer order indicators.

#### Vertical Scale

The cross-section must cover from the surface to the Earth's core (~6371 km). Because most geological interest is in the upper crust (0–50 km), a **split vertical scale** is used:

- **Upper section (0–100 km)**: linear scale, shows crust and uppermost mantle
- **Lower section (100–6371 km)**: logarithmic scale, shows mantle zones and core; labeled but compactly rendered
- A scale break indicator marks the transition

Labeled zones in the lower section (non-variable, Earth-standard):
- Upper crust (0–15 km average, varies per cell from crustThicknessMap)
- Lower crust (15–30 km average)
- Moho discontinuity (exact depth from crustThicknessMap)
- Lithospheric mantle (Moho to ~100 km)
- Asthenosphere (100–410 km)
- Transition zone (410–660 km)
- Lower mantle (660–2891 km)
- Core-mantle boundary (2891 km)
- Outer core (2891–5150 km, liquid)
- Inner core (5150–6371 km, solid)

#### Label Rendering

When labels are toggled on:
- Each distinct layer polygon receives a centered label
- Label content: `[Formation Name] · [Rock Type] · [Age] Ma · [Soil Horizon if applicable]`
- Labels use a leader line if the layer is too thin for inline text
- Labels cull if the layer is less than 3 pixels thick at the current zoom level
- A **legend panel** alongside the cross-section lists all visible layer types with color swatches
- Labels are anti-collision resolved: overlapping labels cascade offset to avoid overlap

#### Interaction in the Cross-Section Panel

- **Zoom**: mouse wheel or pinch — zooms into the profile independently of the globe
- **Pan**: click and drag
- **Hover**: shows a tooltip with the full layer record for that stratigraphy entry
- **Sync**: when the user pans the timeline, the cross-section updates to show stratigraphy at that time — useful for watching layers deposit and erode in real time

---

## 6. Geological Layer Catalog

All layer types that can appear in the stratigraphic record. Each has a `rockType` enum value, a texture, and a color for cross-section rendering.

### Igneous Rock Types

| Enum | Name | Formation Context | Texture Notes |
|------|------|------------------|--------------|
| IGN_BASALT | Basalt | Mid-ocean ridge, shield volcano, flood basalt | Dark grey, fine-grained, vesicular near surface |
| IGN_GABBRO | Gabbro | Oceanic lower crust (intrusive equivalent of basalt) | Dark, coarse-grained, interlocking crystals |
| IGN_RHYOLITE | Rhyolite | Felsic volcanic, caldera eruptions | Light pink/grey, glassy, pumiceous |
| IGN_GRANITE | Granite | Continental batholiths, continental crust roots | Pink/white, coarse-grained, speckled |
| IGN_ANDESITE | Andesite | Stratovolcano flanks, subduction arcs | Grey, porphyritic |
| IGN_DACITE | Dacite | High-silica stratovolcano | Light grey-brown, porphyritic |
| IGN_OBSIDIAN | Obsidian | Rapid cooling of rhyolite | Black, glassy, conchoidal fracture |
| IGN_PUMICE | Pumice | Vesicular ejecta from explosive eruptions | White-cream, highly porous |
| IGN_PERIDOTITE | Peridotite | Lithospheric mantle, ultramafic intrusions | Olive green, coarse |
| IGN_KOMATIITE | Komatiite | Archean high-temperature lavas (rare in modern) | Dark green, spinifex texture |
| IGN_SYENITE | Syenite | Alkalic intrusives, rift settings | Pink-grey, coarse |
| IGN_DIORITE | Diorite | Subduction zone intrusives (intermediate) | Salt-and-pepper grey |
| IGN_PYROCLASTIC | Pyroclastic Flow Deposit (Ignimbrite) | Explosive caldera eruptions | Pink-brown, welded tuff texture |
| IGN_TUFF | Volcanic Tuff | Ash fall from explosive eruptions | Pale grey, fine-grained |
| IGN_PILLOW_BASALT | Pillow Basalt | Submarine eruption at mid-ocean ridges | Dark, rounded pillowy forms |

### Sedimentary Rock Types

| Enum | Name | Formation Context | Texture Notes |
|------|------|------------------|--------------|
| SED_SANDSTONE | Sandstone | River channel, beach, desert dune, shallow marine | Tan-orange, visible grain texture |
| SED_SHALE | Shale | Deep marine, lake, river floodplain (fine-grained mud) | Dark grey-brown, fissile lamination |
| SED_LIMESTONE | Limestone | Warm shallow marine (carbonate) | Light grey-cream, may show fossil texture |
| SED_DOLOSTONE | Dolostone | Diagenetically altered limestone | Light tan, sugary texture |
| SED_CONGLOMERATE | Conglomerate | High-energy fluvial, alluvial fan, shoreline | Multi-colored pebble texture |
| SED_BRECCIA | Breccia | Angular fragments: fault zone, landslide, volcanic | Angular multi-colored clasts |
| SED_COAL | Coal | Swamp/peat in warm humid climates | Black, layered |
| SED_CHALK | Chalk | Deep marine carbonate (coccolith ooze) | White, very fine-grained |
| SED_CHERT | Chert | Siliceous deep marine (radiolarian/diatom ooze) | Dark red-brown, very fine |
| SED_EVAPORITE | Evaporite (Halite/Gypsum) | Restricted basin evaporation | White-pink, crystalline |
| SED_TURBIDITE | Turbidite | Deep-sea turbidity current deposit | Graded beds, dark base to light top |
| SED_TILLITE | Tillite | Lithified glacial till | Poorly sorted, striated clasts |
| SED_LOESS | Loess | Wind-blown silt from periglacial zones | Pale yellow-buff, massive |
| SED_IRONSTONE | Banded Iron Formation | Archean/Proterozoic ocean chemistry | Red-grey banding, iron-rich |
| SED_PHOSPHORITE | Phosphorite | Upwelling zones, organic-rich shelves | Dark grey-brown, nodular |
| SED_MUDSTONE | Mudstone | Lacustrine, low-energy marine | Dark grey, blocky |
| SED_SILTSTONE | Siltstone | Transitional between sandstone and shale | Light grey-brown |
| SED_ARKOSE | Arkose | Feldspar-rich sandstone near granite source | Pink-grey, coarse |
| SED_GREYWACKE | Greywacke | Poorly sorted deep-sea fan sandstone | Dark grey, poorly sorted |
| SED_DIATOMITE | Diatomite | Siliceous ooze, lacustrine or marine | White, very fine |
| SED_PEAT | Peat | Active accumulation of organic matter | Black-brown, fibrous |
| SED_LATERITE | Laterite | Deep tropical chemical weathering | Red-orange, iron/aluminium enriched |
| SED_CALICHE | Caliche (Calcrete) | Arid soil carbonate accumulation | White, nodular |
| SED_REGOLITH | Regolith | Unconsolidated weathered surface material | Variable, matches parent rock |

### Metamorphic Rock Types

| Enum | Name | Protolith | Formation Context |
|------|------|-----------|------------------|
| MET_SLATE | Slate | Shale | Low-grade regional metamorphism |
| MET_PHYLLITE | Phyllite | Shale | Low-medium grade; silky sheen |
| MET_SCHIST | Schist | Shale / basalt | Medium-grade; mica-rich foliation |
| MET_GNEISS | Gneiss | Granite / schist | High-grade; banded, coarse |
| MET_QUARTZITE | Quartzite | Sandstone | Contact or regional; very hard |
| MET_MARBLE | Marble | Limestone / dolostone | Contact or regional; crystalline carbonate |
| MET_AMPHIBOLITE | Amphibolite | Basalt | Medium-high grade; dark, hornblende-rich |
| MET_ECLOGITE | Eclogite | Basalt (subducted) | Ultra-high pressure (>1.5 GPa) in subduction |
| MET_BLUESCHIST | Blueschist | Basalt (subducted) | High pressure, low temperature; subduction |
| MET_HORNFELS | Hornfels | Shale / mudstone | Contact metamorphism near intrusion |
| MET_SERPENTINITE | Serpentinite | Peridotite / dunite | Hydrothermal alteration of mantle rocks |
| MET_MYLONITE | Mylonite | Any rock | Ductile fault zone shear |

### Special / Deep Earth Units

| Enum | Name | Location | Notes |
|------|------|----------|-------|
| DEEP_LITHMAN | Lithospheric Mantle | 30–100 km | Peridotite; rigid, moves with plate |
| DEEP_ASTHEN | Asthenosphere | 100–410 km | Partially molten; enables plate motion |
| DEEP_TRANS | Mantle Transition Zone | 410–660 km | Phase changes in olivine |
| DEEP_LOWMAN | Lower Mantle | 660–2891 km | Bridgmanite-dominated |
| DEEP_CMB | Core-Mantle Boundary | ~2891 km | D'' layer; ultra-low velocity zone |
| DEEP_OUTCORE | Outer Core | 2891–5150 km | Liquid iron-nickel; generates magnetic field |
| DEEP_INCORE | Inner Core | 5150–6371 km | Solid iron-nickel; anisotropic |

---

## 7. Soil Type Catalog

All 12 USDA Soil Taxonomy orders must be represented. Each has specific formation conditions that AGENT-GEO's pedogenesis module must implement.

### Formation Conditions Summary

| USDA Order | Name | Climate | Parent Material | Age Required | Key Process |
|------------|------|---------|----------------|-------------|-------------|
| ENTISOL | Entisols | Any | Any | Very young | Minimal development; fresh deposits, steep slopes, or very arid |
| INCEPTISOL | Inceptisols | Humid to subhumid | Any | Young-moderate | Weak but visible horizon development; B horizon forming |
| MOLLISOL | Mollisols | Semiarid to subhumid | Any (grassland) | Moderate | Deep dark mollic epipedon from grass root accumulation |
| ALFISOL | Alfisols | Humid, temperate to subtropical | Any | Moderate-old | Clay-enriched B horizon (argillic); forest soils |
| ULTISOL | Ultisols | Humid subtropical to tropical | Weatherable minerals | Old | Highly leached, low base saturation, red-yellow argillic |
| OXISOL | Oxisols | Humid tropical | Tropical lowland | Very old | Extreme weathering; iron/aluminium oxides dominate; lateritic |
| SPODOSOL | Spodosols | Cool humid (boreal, temperate) | Sandy, acidic parent | Moderate | Spodic horizon: illuviated iron + aluminium + organic matter |
| HISTOSOL | Histosols | Humid, cold to cool | Organic material | Any | Organic matter > 20% by weight; peat, muck |
| ARIDISOL | Aridisols | Arid (<250 mm precip) | Any | Any | Aridity limits leaching; carbonate/salt accumulation |
| VERTISOL | Vertisols | Subhumid to semiarid, seasonal | High-smectite clay | Moderate | Shrink-swell cracking clays; inverts upper profile |
| ANDISOL | Andisols | Any, volcanic setting | Volcanic ash/tephra | Young-moderate | Amorphous allophane minerals from volcanic parent |
| GELISOL | Gelisols | Arctic/subarctic, alpine | Any | Any | Permafrost within 100 cm; cryoturbation |

### Cross-Section Label Convention

In the cross-section viewer, the topmost layer of each cell is labeled with the soil order if `soilDepthMap > 0.1 m`. The label format is:

`[Soil Order] / [Horizon] / [Depth cm]`

For example: `Oxisol / Bw / 0–180 cm` or `Mollisol / A / 0–40 cm`.

### Soil Horizon Stack (for label detail popup)

| Horizon | Description |
|---------|-------------|
| O | Organic litter layer (uncomposted plant material) |
| A | Topsoil with organic matter; darkest mineral horizon |
| E | Eluvial horizon; leached of clay, iron, and aluminium |
| B | Subsoil; accumulation of leached materials from above |
| C | Weathered parent material; little pedogenic alteration |
| R | Unweathered bedrock |

Special B-horizon designators visible in cross-section labels:
- Bt: argillic (clay accumulation) — Alfisols, Ultisols
- Bh: spodic organic accumulation — Spodosols
- Bs: spodic iron/aluminium — Spodosols
- Bo: oxic horizon — Oxisols
- Bk: carbonate accumulation — Aridisols, Mollisols
- By: gypsum accumulation — Aridisols
- Bz: salt accumulation — Aridisols
- Bg: gleyed (waterlogged) — Histosols, Inceptisols in wetlands

---

## 8. Cloud Type Catalog

All 10 cloud genera and their relevant species and varieties must be represented in the atmospheric simulation. Cloud generation is driven by altitude, atmospheric instability, moisture content, and lifting mechanism.

### High-Level Cloud Genera (Base Altitude > 6,000 m)

| Genus | Abbrev | Altitude | Formation Mechanism | Visual Character | Precip |
|-------|--------|----------|--------------------|--------------------|--------|
| Cirrus | Ci | 6,000–12,000 m | Ice crystals at jet-stream level; outflow from deep convection | Thin white streaks, mare's tails, halos | None |
| Cirrocumulus | Cc | 6,000–12,000 m | Shallow convection in upper troposphere | Small white puffs in rows (mackerel sky), no shadowing | Rare virga |
| Cirrostratus | Cs | 6,000–12,000 m | Broad upper-level moisture sheet, warm front advance | Thin white veil, sun/moon halo, gradual thickening | None |

### Mid-Level Cloud Genera (Base Altitude 2,000–6,000 m)

| Genus | Abbrev | Altitude | Formation Mechanism | Visual Character | Precip |
|-------|--------|----------|--------------------|--------------------|--------|
| Altocumulus | Ac | 2,000–7,000 m | Mid-level gravity waves, mesoscale convection | Grey-white patches or rolls, waves, some shadowing | Virga possible |
| Altostratus | As | 2,000–7,000 m | Warm frontal lift, thick mid-level moisture | Grey uniform sheet, sun diffused (ground-glass effect) | Light rain, virga |
| Nimbostratus | Ns | 900–4,000 m | Deep frontal lift; warm or occluded front | Dark grey featureless sheet, ragged base | Persistent rain/snow |

### Low-Level Cloud Genera (Base Altitude < 2,000 m)

| Genus | Abbrev | Altitude | Formation Mechanism | Visual Character | Precip |
|-------|--------|----------|--------------------|--------------------|--------|
| Stratus | St | 0–500 m | Radiation cooling, advection fog lifting | Grey, flat, uniform; fog when touching surface | Drizzle |
| Stratocumulus | Sc | 500–2,000 m | Boundary-layer convection, trade wind inversion | Grey-white rolls or patches; most common cloud globally | Light drizzle |

### Vertically Developed Genera (All Altitudes)

| Genus | Abbrev | Altitude | Formation Mechanism | Visual Character | Precip |
|-------|--------|----------|--------------------|--------------------|--------|
| Cumulus | Cu | 500–6,000 m | Thermal convection over heated surface | White puffy, flat base, cauliflower top | Showers if congestus |
| Cumulonimbus | Cb | 500–15,000 m | Deep moist convection, thunderstorm; ITCZ, frontal | Towering anvil top (cirrus outflow), dark base | Heavy rain, hail, lightning |

### Cloud Species and Varieties (for label detail and visual differentiation)

| Cloud | Species | Description |
|-------|---------|-------------|
| Cirrus | fibratus | Straight or slightly curved filaments |
| Cirrus | uncinus | Hooked, comma-shaped (mare's tails) |
| Cirrus | spissatus | Dense, associated with Cb anvil |
| Altocumulus | castellanus | Turret-like tops; instability indicator |
| Altocumulus | lenticularis | Lens-shaped; mountain wave clouds |
| Altocumulus | floccus | Tufted, ragged base; moderate instability |
| Stratocumulus | lenticularis | Wave-form over terrain |
| Stratocumulus | castellanus | Instability in marine boundary layer |
| Cumulus | humilis | Shallow fair-weather cumulus; good stability |
| Cumulus | mediocris | Moderate vertical development |
| Cumulus | congestus | Tall, vigorous; heavy showers |
| Cumulonimbus | calvus | Early stage; rounded top, no anvil |
| Cumulonimbus | capillatus | Mature; fibrous ice-crystal anvil |

### Special Cloud Features

| Feature | Associated With | Trigger Condition |
|---------|----------------|------------------|
| Mammatus | Cb anvil | Turbulent downdraughts at anvil base |
| Pileus | Cumulus / Cb | Rapid updraught lifting moist stable layer |
| Arcus (shelf cloud) | Cb | Gust front lifting moist boundary layer |
| Wall cloud | Cb / supercell | Low-level inflow into rotating updraught |
| Virga | As, Ac, Cu | Precipitation evaporating before reaching surface |
| Fog (Stratus at surface) | St | Radiation cooling or advection over cold surface |
| Pyrocumulonimbus | Cb | Fire-generated convection (triggered by large volcanic eruptions or wildfires) |

### Cloud Generation Rules for AGENT-ATMO

- **Cirrus / Cirrostratus**: generate when jet stream is present and upper-level humidity > 40%; advance ahead of warm fronts
- **Nimbostratus**: generate along warm and occluded fronts; sustained precipitation
- **Cumulonimbus**: generate where CAPE (Convective Available Potential Energy) > 1000 J/kg; i.e., steep lapse rate + high surface moisture; mandatory in ITCZ band
- **Stratocumulus**: generate in trade wind zones where low-level inversion is strong (subtropical high)
- **Altocumulus castellanus**: generate where mid-level instability exceeds threshold — precursor to afternoon thunderstorms
- **Lenticular (Ac/Sc lenticularis)**: generate on lee side of mountain ranges where wind speed > 20 m/s
- **Fog / Stratus**: generate when surface temperature drops below dew point; coastal zones with cold ocean upwelling
- **Pyrocumulonimbus**: triggered by VOLCANIC_ERUPTION events with VEI ≥ 5

---

## 9. Atmospheric & Climate Modeling

### Energy Balance

The planet's energy budget follows Stefan-Boltzmann and greenhouse physics:

- Incoming solar radiation: `S_in = S₀/4 · (1 - α_planet)` where `S₀ = 1361 W/m²`, `α_planet` = planetary albedo
- Outgoing longwave: `S_out = ε · σ · T_eff⁴`
- Greenhouse enhancement: `ΔT = λ · ln(CO₂/CO₂_ref)` where `λ ≈ 3°C per doubling`
- Latent heat transport: poleward heat transport by atmosphere and ocean

### Milankovitch Parameters

| Parameter | Period | Range | Effect |
|-----------|--------|-------|--------|
| Eccentricity | ~100 kyr | 0.0–0.06 | Modulates seasonal intensity |
| Obliquity (axial tilt) | ~41 kyr | 22.1°–24.5° | Controls polar vs. equatorial insolation contrast |
| Precession | ~23 kyr | Cycles through all seasons | Which hemisphere has summer at perihelion |

### Weathering-CO₂ Feedback (Urey Reaction)

Silicate weathering consumes CO₂:
`CaSiO₃ + CO₂ → CaCO₃ + SiO₂`

Rate increases with: higher temperature, higher precipitation, more exposed fresh silicate rock (e.g., after major mountain-building event). This creates a negative feedback that stabilizes climate over multi-million year timescales. AGENT-GEO must report fresh silicate rock exposure rate to ATMO.

### Albedo Values by Surface Type

| Surface | Albedo |
|---------|--------|
| Fresh snow | 0.80–0.90 |
| Sea ice | 0.50–0.70 |
| Desert sand | 0.30–0.40 |
| Grassland | 0.15–0.25 |
| Tropical forest | 0.10–0.15 |
| Open ocean | 0.06–0.10 |
| Bare basalt | 0.05–0.10 |
| Cumulonimbus cloud top | 0.70–0.90 |
| Cirrus cloud | 0.10–0.40 |

---

## 10. Biomatter System

Beyond plant vegetation (covered by the optional VegetationEngine), GeoTime models simple non-plant biomatter — microbes, plankton, reef-building organisms, fungi, and decomposers. These organisms drive fundamental planetary changes: oxygenating the atmosphere, producing biogenic sediments, altering ocean chemistry, and generating petroleum source rocks.

### Biomatter Categories

| Category | Habitat | Key Output | Geological Record |
|----------|---------|------------|-------------------|
| Cyanobacteria | Shallow marine, tidal flats | O₂ production, stromatolite structures | SED_LIMESTONE (laminated), SED_IRONSTONE (via iron oxidation) |
| Microbial mats | Shallow marine, lagoons | Carbonate precipitation, organic carbon burial | SED_LIMESTONE, SED_CHERT |
| Phytoplankton (coccolithophores) | Open ocean, photic zone | O₂ production, coccolith shells, CO₂ drawdown | SED_CHALK |
| Phytoplankton (diatoms) | Cold nutrient-rich ocean | O₂ production, siliceous frustules | SED_DIATOMITE, SED_CHERT |
| Zooplankton (foraminifera, radiolaria) | Open ocean | Carbonate/siliceous test accumulation | SED_LIMESTONE, SED_CHERT |
| Reef organisms (corals, sponges) | Warm shallow marine (18–30°C) | Carbonate reef framework, local height modification | SED_LIMESTONE |
| Benthic organisms | Seafloor, all depths | Bioturbation of upper sediment layers | Increased weathering rate of top stratigraphy layer |
| Fungi & decomposers | Terrestrial soils | Organic litter breakdown, accelerated pedogenesis | Enhanced soil organic carbon; faster soil order maturation |
| Anaerobic microbes | Anoxic basins, wetlands | CH₄ (methane) production, organic carbon preservation | Kerogen, SED_OIL_SHALE, petroleum precursors |

### Biomatter Productivity Model

Biomatter productivity is computed per cell based on habitat type:

**Marine biomatter** (ocean cells):
- `productivity = base_rate × temp_factor × nutrient_factor × light_factor`
- `temp_factor`: bell curve centered at 20°C for tropical plankton; shifted to 5°C for cold-water diatoms
- `nutrient_factor`: proportional to proximity to upwelling zones (divergent ocean boundaries, western continental margins)
- `light_factor`: 1.0 for cells in photic zone (depth < 200 m); decreases with depth for benthic organisms
- Maximum marine biomatter: 5 kg C/m²

**Terrestrial biomatter** (land cells, non-plant):
- Fungi and decomposers: `productivity = 0.2 × vegetation_biomass` (proportional to plant litter availability)
- Requires soil depth > 0.1 m, temperature > 0°C
- Maximum terrestrial non-plant biomatter: 2 kg C/m²

### Atmospheric Interactions

Biomatter drives major atmospheric composition changes over geological time:

| Process | Direction | Mechanism | Timescale |
|---------|-----------|-----------|-----------|
| Oxygenic photosynthesis | O₂ ↑ | Cyanobacteria + phytoplankton fix CO₂, release O₂ | 100 Myr–1 Gyr |
| Biological pump | CO₂ ↓ | Plankton fix CO₂ at surface; organic matter sinks to deep ocean | 1–10 Myr |
| Methanogenesis | CH₄ ↑ | Anaerobic microbes in anoxic basins and wetlands produce methane | 1–100 Myr |
| Great Oxygenation Event | O₂ ↑↑ | Cumulative cyanobacteria output exceeds reducing sinks (dissolved iron, volcanic gases) | One-time threshold event |

**O₂ gating rules**:
- < 0.1% O₂: only anaerobic microbes and cyanobacteria active
- 0.1–2% O₂: aerobic marine organisms enabled (plankton, reef organisms)
- 2–15% O₂: terrestrial fungi and decomposers enabled
- \> 15% O₂: fire becomes possible; vegetation fire model activates

### Ocean Chemistry Effects

| Effect | Trigger | Impact |
|--------|---------|--------|
| Carbonate compensation depth (CCD) | Marine plankton productivity + ocean temperature | Below CCD, carbonate shells dissolve; above CCD, SED_CHALK / SED_LIMESTONE deposited |
| Banded iron formation | Cyanobacteria O₂ output in iron-rich Archean ocean | SED_IRONSTONE deposited until dissolved iron exhausted |
| Ocean anoxia events | High organic productivity + poor ocean circulation | Organic carbon preserved in anoxic bottom water → petroleum source rocks |
| Reef building | Coral/sponge colonization of warm shallow cells | Local heightMap increase (up to +30 m over 10 Myr); SED_LIMESTONE deposition |
| Phosphorite deposition | High biomatter productivity in upwelling zones | SED_PHOSPHORITE layers in stratigraphy |

### Petroleum Source-Rock Pipeline

Petroleum formation follows a multi-step geological pipeline:

1. **Organic carbon accumulation**: High-productivity marine biomatter dies and settles in anoxic basin → organicCarbonMap increases
2. **Burial**: Sedimentation covers organic-rich layer; recorded in stratigraphy
3. **Oil window** (60–120°C, burial depth 2–4 km): Kerogen matures into hydrocarbons → new StratigraphicLayer with `rockType = SED_OIL_SHALE`
4. **Gas window** (120–200°C, burial depth > 4 km): Remaining kerogen cracks to natural gas
5. **Petroleum trap**: Oil/gas migrates upward until capped by impermeable layer (SED_SHALE, SED_EVAPORITE) → flagged as petroleum reservoir in stratigraphy for cross-section display

### Biogenic Sediment Deposition Rules

| Sediment Type | Enum | Condition | Deposition Rate |
|---------------|------|-----------|-----------------|
| Chalk | SED_CHALK | Ocean cell, depth > CCD, coccolith productivity > threshold | 0.01–0.05 m/Myr |
| Chert | SED_CHERT | Cold ocean, upwelling zone, radiolarian/diatom productivity high | 0.005–0.02 m/Myr |
| Diatomite | SED_DIATOMITE | Cold nutrient-rich ocean or lacustrine, diatom bloom zone | 0.01–0.04 m/Myr |
| Reef limestone | SED_LIMESTONE | Warm shallow marine (18–30°C), reef organism active | 0.1–1.0 m/Myr |
| Stromatolite carbonate | SED_LIMESTONE | Shallow tidal flat, microbial mat active, Precambrian or low-grazing | 0.01–0.1 m/Myr |
| Phosphorite | SED_PHOSPHORITE | Upwelling zone, high organic concentration | 0.001–0.01 m/Myr |
| Banded iron formation | SED_IRONSTONE | Archean ocean, dissolved iron + cyanobacteria O₂ | 0.01–0.05 m/Myr (ceases after iron exhaustion) |
| Oil shale | SED_OIL_SHALE | Buried organic carbon in oil window (60–120°C, 2–4 km depth) | Conversion from existing organic layer |

### New Rock Type

The biomatter system introduces one new rock type to the geological layer catalog:

| Enum | Name | Formation Context | Texture Notes |
|------|------|------------------|--------------|
| SED_OIL_SHALE | Oil Shale (Kerogen-rich) | Mature organic carbon in oil window | Dark brown-black, laminated, organic-rich |

---

## 11. Recommended Technology Stack

### Rendering

| Component | Technology | Rationale |
|-----------|-----------|-----------|
| Primary GPU API | WebGPU | Compute shaders; modern pipeline; future-proof |
| Fallback GPU API | WebGL 2 | Safari and older browsers |
| Scene graph | Three.js r160+ | Mature, well-documented |
| Shaders | WGSL (WebGPU) / GLSL (fallback) | Required by respective APIs |
| Texture compression | KTX2 + Basis Universal | Runtime transcoding to GPU-native format |
| Globe projection | Spherical icosphere, subdivided quad-tree LOD | Uniform vertex density; no pole singularity |

### Simulation Compute

| Component | Technology | Rationale |
|-----------|-----------|-----------|
| Erosion inner loops | Rust → WASM (wasm-pack) | 10–50× faster than JS for particle loops |
| Parallelism | Web Workers | True parallel execution; KERNEL manages pool |
| Zero-copy state | SharedArrayBuffer | All state maps shared between main thread and workers |
| GPU erosion | WebGPU Compute Shaders | Massively parallel hydraulic erosion |

### Data & State

| Component | Technology | Rationale |
|-----------|-----------|-----------|
| Snapshot format | Flatbuffers | Zero-copy deserialization; fast seek |
| Snapshot compression | Brotli | Better ratio than gzip; browser-native |
| Local snapshot storage | IndexedDB | Persistent across sessions |
| Stratigraphy stack | Custom binary format | Variable-length per cell; must be memory-mapped |
| Planet sharing | URL fragment (base64 seed + params) | No server required |

### Build & Test

| Component | Technology |
|-----------|-----------|
| Bundler | Vite |
| Language | TypeScript (strict) |
| Rust toolchain | wasm-pack + cargo |
| Unit tests | Vitest |
| Visual regression | Playwright + pixelmatch |
| Component isolation | Storybook |
| Performance profiling | Chrome DevTools + WebGPU Timing API |

---

## 12. Risk Register

| Risk | Severity | Agent | Mitigation |
|------|----------|-------|-----------|
| WebGPU not yet supported on all browsers (Safari lagging) | HIGH | RENDER | WebGL 2 fallback renderer with feature detection at runtime; reduced cloud quality in fallback |
| Geological simulation too slow at geological time scales | HIGH | KERNEL, GEO | Multi-rate ticking: geology at coarse Δt; weather at fine Δt; WASM + GPU compute for inner loops |
| Stratigraphy stacks consume excessive memory (2048² × N layers) | HIGH | GEO, KERNEL | Run-length encode identical adjacent layers; hard cap at 256 layers per cell; merge thin layers below threshold thickness |
| State rewind requires enormous snapshot storage | MEDIUM | KERNEL | Sparse delta snapshots; keyframe every 10 Myr; store only changed cells; Brotli compression |
| Cross-section geometry reconstruction artifacts at faults and overturned beds | MEDIUM | SECTION | Clamp dip angles; detect crossing layer contacts; insert fault planes explicitly rather than interpolating |
| Plate boundary detection producing visual artifacts | MEDIUM | GEO | Voronoi relaxation + spring forces; smooth boundary interpolation over multiple ticks; avoid single-cell boundaries |
| Climate ↔ geology feedback loop numerical instability | MEDIUM | ATMO, GEO | Operator splitting: advance geology first, then climate with updated topography; clamp feedback rates |
| Cloud type coverage: not all 10 genera appearing on simulated planet | MEDIUM | ATMO | Ensure cloud generation rules cover all climate zones; add minimum spawn thresholds per zone type |
| Soil coverage: not all 12 orders appearing on a typical planet | MEDIUM | GEO | Guarantee climate zone diversity via PROC; add minimum coverage assertion in integration test suite |
| Texture memory exceeding GPU budget at max LOD | LOW | RENDER | Streaming KTX2 tiles; virtual texturing; only resident tiles for camera-visible region |
| Vegetation feature scope creep | LOW | All | Feature-flagged from build time; disable for release candidate |
| Biomatter feature scope creep into evolutionary biology | LOW | GEO, ATMO | Keep biomatter simple: no speciation, no food webs, no behavioral modeling; organisms are productivity functions, not individuals |
| Petroleum source-rock pipeline complexity | LOW | GEO | Simplified burial/maturation model; no fluid-flow simulation; flag petroleum traps in stratigraphy only |
| Biomatter ↔ atmosphere feedback loop instability | MEDIUM | GEO, ATMO | Clamp O₂ and CH₄ rate-of-change per tick; operator-split biomatter and atmosphere updates |
| Cross-section label readability at all zoom levels | LOW | SECTION | Dynamic font size; leader lines; label LOD culling below 3 px layer height |

---

## 13. Per-Agent Briefing Notes

> **For all agents**: Read Section 3 (Inter-Agent Interfaces) before writing any code. Never mutate another agent's canonical data directly — always emit an event and let the owning agent update its own state. The KERNEL agent has final authority over clock advancement.

### AGENT-KERNEL

- Build the event bus first — every other agent blocks on it
- Define all shared ArrayBuffer layouts in `shared-types.ts`; all agents import this single source of truth — it must never be duplicated
- Implement `SimClock.advance(dt_years)` as the sole clock entry point
- The snapshot format must be agreed with SECTION agent before Phase 2 begins, as stratigraphy stacks are the most complex data structure
- Design the Web Worker pool to allow per-agent tick rate configuration: GEO might tick every 1000 sim-years, ATMO every 10 sim-years, RENDER every real frame

### AGENT-PROC

- Everything must be deterministic given the same uint32 seed — use xoshiro256** or PCG
- Voronoi plate generation: target 10–16 plates with Lloyd relaxation (3 iterations minimum)
- Ensure the initial height field produces at least one connected ocean basin and at least one continental landmass
- Place hotspots only within plates, not on boundaries
- PROC's responsibility ends the moment all maps are written and KERNEL is notified — it does not run during the simulation

### AGENT-GEO

- Build tectonics first — erosion and stratigraphy depend on stable plate infrastructure
- Write the Rust WASM erosion module from the start; do not prototype in JavaScript with intent to port later
- The stratigraphy stack is the contract with AGENT-SECTION: document every field in the `StratigraphicLayer` struct before Phase 2 begins
- All plate parameters must be deterministic given the same PRNG state
- Emit `VOLCANIC_ERUPTION` and `PLATE_COLLISION` events consistently — RENDER and ATMO depend on them
- Soil formation (pedogenesis) must be tied to the CLORPT factors: document which climate inputs you read from ATMO at each soil-formation tick
- Dip angle updates must be gradual and physically motivated — avoid sudden discontinuities that would create visual artifacts in the cross-section
- **Biomatter (Phase 7)**: The biomatter engine is feature-flagged like vegetation. Biomatter productivity is a per-cell function of habitat type (marine vs. terrestrial), temperature, and nutrients — keep it simple, no individual organisms
- **Biogenic sedimentation**: Cyanobacteria, plankton, and reef organisms deposit new StratigraphicLayers (SED_CHALK, SED_CHERT, SED_LIMESTONE, etc.) — reuse existing sediment rock types where possible
- **Petroleum pipeline**: Organic carbon burial → kerogen maturation is a depth/temperature threshold check on existing stratigraphy — no fluid-flow simulation needed
- Emit `BIOMATTER_UPDATE` and `OXYGENATION_EVENT` consistently — ATMO depends on them for atmosphere composition changes

### AGENT-ATMO

- Start with the 3-box energy balance model for rapid iteration and validate climate zone placement first
- Temperature and precipitation maps must be updated every climate tick; GEO and RENDER both read them
- CO₂ must increase when GEO emits volcanic eruption events; implement the Urey weathering sink as a function of fresh silicate exposure reported by GEO
- Ice-age onset and recovery must emit events consumed by GEO (glacial erosion activation) and RENDER (ice/snow shader)
- All 10 cloud genera must be generatable; ensure each has at least one trigger condition that will be met on a typical planet within 500 Myr
- Cloud cover must affect surface albedo, which feeds back into the temperature calculation
- **Biomatter (Phase 7)**: Accept `BIOMATTER_UPDATE` and `OXYGENATION_EVENT` from GEO to update atmospheric O₂ and CH₄ concentrations; O₂ level gates control which biomatter categories are active
- The biological pump (plankton CO₂ fixation → deep ocean burial) provides a long-term CO₂ sink alongside the Urey weathering reaction — both must be represented

### AGENT-RENDER

- Build the icosphere + displacement pipeline in Week 1 before any other rendering work
- The atmospheric scattering shader is the single highest visual-impact item; allocate extra time for it
- Biome texture blending: implement as a 2D texture lookup — X-axis = mean annual temperature, Y-axis = annual precipitation, producing the Whittaker biome classification naturally
- Day/night terminator: compute great-circle boundary from SimClock.t + planetary axial tilt + orbital position; animate smoothly
- Rock type texture atlas: at minimum, cover all enum values in Section 6 with distinct visual treatments
- Soil type texture layer: semi-transparent blending over bedrock; thicker soil = more opaque soil texture
- Cloud rendering: each cloud genus should have a distinct visual signature — do not use a single cloud texture for all types

### AGENT-SECTION

- Read Section 5 in full before writing any code
- The stratigraphy stack data contract with GEO is your most important external dependency — get it in writing (i.e., agreed shared-types.ts) before Phase 5 begins
- The vertical scale split (linear 0–100 km, logarithmic 100–6371 km) is mandatory — user research shows geologists find the full-scale view useless without it
- Dip angle reconstruction: test against synthetic stratigraphy with known geometries (horizontal, 30° monocline, anticline, syncline, overturned) before integrating real simulation data
- Label anti-collision: implement a greedy left-right offset cascade — simpler than force-directed, sufficient for this use case
- The cross-section must update when the timeline changes — subscribe to the `TICK` event and resample stratigraphy if a cross-section path is active

### AGENT-UI

- The time scrubber must call `KERNEL.seekTo(t)` only — never advance the clock directly from UI
- Camera: arcball orbit with smooth inertia damping and zoom-to-cursor (not zoom-to-center)
- The draw cross-section tool must show a live preview of the path draped on the globe as the user places points
- All map overlay layers (plates, temperature, precipitation, soil, cloud) are shader uniforms or texture swaps in RENDER — communicate via event, not direct state mutation
- The info inspector popup (click on globe → ray-cast → sample all maps) should display: elevation, rock type, age, soil order, biome, mean temperature, annual precipitation, dominant cloud type
- Settings panel: vegetation toggle, label toggle, quality slider (LOD bias), cross-section sample count
- **Biomatter (Phase 7)**: Add biomatter toggle to settings panel; expand cell inspection to show biomatter density, organic carbon, and reef presence; add biomatter density overlay to layer panel
- Mobile touch support must be considered from the start — pinch-zoom, two-finger pan, long-press for cross-section draw

---

*End of GeoTime Implementation Plan v1.2*

*6 Agents · 7 Domains · 7 Phases · 21 Weeks · 1 Planet at a Time*
