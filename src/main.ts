// ─── GeoTime — Main Entry Point ─────────────────────────────────────────────
// The frontend now delegates all simulation/calculation work to the C# .NET
// backend via REST API calls. The frontend only handles display (Three.js
// rendering, UI shell, cross-section Canvas 2D rendering).

import './style.css';
import { GRID_SIZE } from './shared/types';
import { GlobeRenderer } from './render/globe-renderer';
import { LabelRenderer } from './render/label-renderer';
import { renderCrossSection, exportCrossSectionPNG } from './render/cross-section-renderer';
import { AppShell } from './ui/app-shell';
import * as api from './api/backend-client';
import type { CrossSectionProfile as SharedCrossSectionProfile, LatLon } from './shared/types';

// ── Bootstrap ───────────────────────────────────────────────────────────────

const appEl = document.getElementById('app');
if (!appEl) {
  throw new Error('Root element #app not found in the document.');
}

const shell = new AppShell(appEl);
const viewportEl = shell.getViewportElement();
const renderer = new GlobeRenderer(viewportEl);
const labelRenderer = new LabelRenderer(shell.getLabelLayer());

// ── Simulation state (display-only buffers populated from backend) ──────────

/** Current simulation time (Ma), updated from backend. */
let simTimeMa = -4500;
/** Whether the simulation is paused locally. */
let paused = false;
/** Simulation rate (Ma per second of real time). */
let simRate = 1;

// Initialise the HUD time display immediately so it never shows "--"
shell.setSimTime(simTimeMa);
shell.setTimelineCursor(0);

// ── Planet generation via backend ───────────────────────────────────────────

function randomSeed(): number {
  return (Math.random() * 0xffff_fffe + 1) >>> 0;
}

async function doGeneratePlanet(seed: number): Promise<void> {
  try {
    const result = await api.generatePlanet(seed);
    simTimeMa = result.timeMa;
    totalTickCount = 0;

    // Fetch initial display data from backend
    const [heightData, plateData, tempData, precipData] = await Promise.all([
      api.getHeightMap(),
      api.getPlateMap(),
      api.getTemperatureMap(),
      api.getPrecipitationMap(),
    ]);

    const heightMap = new Float32Array(heightData);
    const plateMap = new Uint16Array(plateData);

    renderer.updateHeightMap(heightMap, GRID_SIZE);
    renderer.updatePlateMap(plateMap, GRID_SIZE);

    // Set biome-influenced base texture for the default (non-overlay) view.
    // This gives the globe varied colouring: deserts, forests, tundra, etc.
    renderer.updateBiomeBaseMap(
      new Float32Array(tempData),
      new Float32Array(precipData),
      heightMap,
      GRID_SIZE,
    );

    shell.setSeed(result.seed);
    shell.setTriangleCount(renderer.getTriangleCount());
    shell.setSimTime(simTimeMa);
    shell.setTimelineCursor(4500 + simTimeMa);

    // Update load-state button availability (snapshots exist if we ever saved)
    api.listSnapshots().then((info) => {
      shell.setLoadStateEnabled(info.count > 0);
    }).catch(() => {/* ignore */});

    // Fetch and display the compute backend indicator (GPU vs CPU)
    api.getComputeInfo().then((info) => {
      shell.setComputeMode(info.isGpu, info.deviceName, info.memoryMb ?? 0, renderer.getWebGLRendererInfo());
    }).catch(() => {/* backend may not be ready yet – ignore */});

    // Close any open cross-section panel on new planet
    shell.hideCrossSection();
    shell.setDrawMode(false);
    drawModeActive = false;
    drawPoints = [];

    // Fetch feature labels from backend and pass to label renderer (Phase L5).
    api.fetchFeatureLabels()
      .then((labels) => labelRenderer.setLabels(labels))
      .catch(() => {/* labels are non-critical */});

    // Fetch available event layer types and populate dropdown (Phase D6).
    api.fetchEventLayerTypes()
      .then((types) => shell.setEventLayerTypes(types))
      .catch(() => {/* event layer types are non-critical */});

    // Encode seed in URL fragment for sharing
    window.location.hash = `seed=${result.seed}`;
  } catch (err) {
    console.error('Failed to generate planet:', err);
  }
}

// Parse seed from URL fragment if available
function seedFromURL(): number | null {
  const hash = window.location.hash;
  const match = hash.match(/seed=(\d+)/);
  if (match) {
    const val = Number(match[1]);
    if (val > 0 && val <= 0xffff_fffe) return val;
  }
  return null;
}

// Generate the first planet (from URL seed or random)
doGeneratePlanet(seedFromURL() ?? randomSeed());

// ── Cross-Section Draw Mode ─────────────────────────────────────────────────

let drawModeActive = false;
let drawPoints: LatLon[] = [];
let lastCrossSectionProfile: api.CrossSectionProfile | null = null;

// Cross-section zoom level (1 = 100%, range 0.5 – 4×)
let crossSectionZoom = 1;

/** Render a cross-section profile into the panel canvas at the current zoom level. */
function renderCrossSectionToPanel(profile: api.CrossSectionProfile, showLabels: boolean): void {
  const canvas = shell.getCrossSectionCanvas();
  const scrollEl = shell.getCrossSectionScrollEl();
  const panelW = scrollEl.clientWidth || 960;
  const w = Math.max(200, Math.round(panelW * crossSectionZoom));
  const h = Math.max(100, Math.round(280 * crossSectionZoom));
  // The API and shared types have compatible shapes; cast via unknown for safety
  renderCrossSection(profile as unknown as SharedCrossSectionProfile, {
    width: w,
    height: h,
    showLabels,
    showLegend: true,
  }, canvas);
}

shell.onDrawMode(() => {
  drawModeActive = !drawModeActive;
  shell.setDrawMode(drawModeActive);
  if (drawModeActive) {
    drawPoints = [];
  }
});

shell.onLabelToggle((visible) => {
  // Re-render if a profile is active
  if (lastCrossSectionProfile) {
    renderCrossSectionToPanel(lastCrossSectionProfile, visible);
  }
});

shell.onExportPng(() => {
  const canvas = shell.getCrossSectionCanvas();
  const dataUrl = exportCrossSectionPNG(canvas);
  const link = document.createElement('a');
  link.download = 'cross-section.png';
  link.href = dataUrl;
  link.click();
});

shell.onCloseCrossSection(() => {
  lastCrossSectionProfile = null;
  drawModeActive = false;
  shell.setDrawMode(false);
  drawPoints = [];
  crossSectionZoom = 1;
});

shell.onCrossSectionZoomIn(() => {
  crossSectionZoom = Math.min(4, crossSectionZoom + 0.5);
  if (lastCrossSectionProfile) {
    renderCrossSectionToPanel(lastCrossSectionProfile, shell.areLabelsVisible());
  }
});

shell.onCrossSectionZoomOut(() => {
  crossSectionZoom = Math.max(0.5, crossSectionZoom - 0.5);
  if (lastCrossSectionProfile) {
    renderCrossSectionToPanel(lastCrossSectionProfile, shell.areLabelsVisible());
  }
});

shell.onCrossSectionZoomReset(() => {
  crossSectionZoom = 1;
  if (lastCrossSectionProfile) {
    renderCrossSectionToPanel(lastCrossSectionProfile, shell.areLabelsVisible());
  }
});

// ── Layer overlay toggle handling ────────────────────────────────────────────

// Track which non-plate data overlay layers are currently active
const activeDataLayers = new Set<string>();

/** Temperature (°C) → RGB: blue (cold) through white (0°C) to red (hot). */
function temperatureColor(t: number): [number, number, number] {
  if (t <= -40) return [0, 0, 200];
  if (t < 0) {
    const f = (t + 40) / 40;
    return [Math.round(f * 255), Math.round(f * 255), 200 + Math.round(f * 55)];
  }
  const f = Math.min(1, t / 45);
  return [200 + Math.round(f * 55), Math.round((1 - f) * 200), Math.round((1 - f) * 200)];
}

/** Precipitation (mm/yr) → RGB: tan (dry) to deep blue-green (wet). */
function precipitationColor(p: number): [number, number, number] {
  const f = Math.min(1, p / 2500);
  return [
    Math.round(200 - f * 180),
    Math.round(160 + f * 60),
    Math.round(60 + f * 180),
  ];
}

/** Cloud proxy — precipitation scaled to white/grey cloud appearance. */
function cloudColor(p: number): [number, number, number] {
  const f = Math.min(1, p / 2500);
  const v = Math.round(60 + f * 195);
  return [v, v, v];
}

/** Biomass (kg/m²) → RGB: nearly black (barren) to vivid green (lush). */
function biomassColor(b: number): [number, number, number] {
  const f = Math.min(1, b / 12);
  return [Math.round(10 + f * 20), Math.round(40 + f * 190), Math.round(10 + f * 20)];
}

/** Elevation (m) → RGB: vivid topographic bands (ocean → peaks). */
function topoColor(h: number): [number, number, number] {
  if (h < -5000) return [0, 20, 140];
  if (h < -200) {
    const f = (h + 5000) / 4800;
    return [Math.round(f * 40), Math.round(f * 100 + 60), Math.round(120 + f * 100)];
  }
  if (h < 0)   return [100, 200, 240];
  if (h < 500) return [50,  210,  70];
  if (h < 1500) return [210, 190, 50];
  if (h < 3000) return [190, 110, 35];
  if (h < 5000) return [160, 110, 90];
  return [255, 255, 255];
}

/**
 * USDA soil order (0-12) → RGB colour for the soil layer overlay.
 * Must match the SoilOrder enum in the backend (Enums.cs).
 */
function soilOrderColor(order: number): [number, number, number] {
  switch (order) {
    case 0:  return [180, 180, 180]; // None (grey)
    case 1:  return [160, 120, 60];  // Alfisols  – brown
    case 2:  return [100, 80,  40];  // Andisols  – dark brown (volcanic)
    case 3:  return [230, 200, 120]; // Aridisols – sandy yellow
    case 4:  return [200, 180, 140]; // Entisols  – light tan
    case 5:  return [220, 240, 255]; // Gelisols  – icy pale blue
    case 6:  return [60,  100, 60];  // Histosols – organic dark green
    case 7:  return [140, 170, 100]; // Inceptisols – muted olive
    case 8:  return [180, 140, 80];  // Mollisols – warm tan (grassland)
    case 9:  return [80,  50,  20];  // Oxisols   – deep red-brown (tropical)
    case 10: return [100, 130, 160]; // Spodosols – grey-blue (boreal)
    case 11: return [120, 80,  40];  // Ultisols  – rust (humid sub-tropical)
    case 12: return [160, 100, 140]; // Vertisols – mauve (clay-rich)
    default: return [180, 180, 180];
  }
}

// ── Rock type / soil order enum maps (must match backend Enums.cs) ─────────

const ROCK_TYPE_NAMES: Record<number, string> = {
  0: 'Basalt', 1: 'Granite', 2: 'Sandstone', 3: 'Limestone', 4: 'Shale',
  5: 'Marble', 6: 'Quartzite', 7: 'Gneiss', 8: 'Peridotite', 9: 'Andesite',
  10: 'Rhyolite', 11: 'Chalk', 12: 'Coal', 13: 'Oil Shale',
};

const SOIL_ORDER_NAMES: Record<number, string> = {
  0: 'None', 1: 'Alfisols', 2: 'Andisols', 3: 'Aridisols', 4: 'Entisols',
  5: 'Gelisols', 6: 'Histosols', 7: 'Inceptisols', 8: 'Mollisols',
  9: 'Oxisols', 10: 'Spodosols', 11: 'Ultisols', 12: 'Vertisols',
};

// ── Layer legend definitions ──────────────────────────────────────────────────

const LAYER_LEGENDS: Record<string, { title: string; items: Array<{ color: string; label: string }> }> = {
  temperature: {
    title: 'Temperature',
    items: [
      { color: 'rgb(0,0,200)',     label: '<= -40 \u00b0C (polar)' },
      { color: 'rgb(100,100,220)', label: '-20 \u00b0C (cold)' },
      { color: 'rgb(255,255,255)', label: '0 °C (freezing)' },
      { color: 'rgb(230,120,120)', label: '+20 °C (warm)' },
      { color: 'rgb(255,0,0)',     label: '+45 °C (hot)' },
    ],
  },
  precipitation: {
    title: 'Precipitation',
    items: [
      { color: 'rgb(200,160,60)',   label: '0 mm/yr (arid)' },
      { color: 'rgb(120,190,140)',  label: '600 mm/yr' },
      { color: 'rgb(40,180,180)',   label: '1500 mm/yr' },
      { color: 'rgb(20,220,240)',   label: '2500+ mm/yr (wet)' },
    ],
  },
  clouds: {
    title: 'Cloud Cover (proxy)',
    items: [
      { color: 'rgb(60,60,60)',   label: 'Clear / dry' },
      { color: 'rgb(150,150,150)', label: 'Partly cloudy' },
      { color: 'rgb(255,255,255)', label: 'Overcast / wet' },
    ],
  },
  biomass: {
    title: 'Biomass',
    items: [
      { color: 'rgb(10,40,10)',   label: '0 kg/m² (barren)' },
      { color: 'rgb(15,120,15)',  label: '4 kg/m²' },
      { color: 'rgb(20,190,20)',  label: '8 kg/m²' },
      { color: 'rgb(30,230,30)',  label: '12+ kg/m² (lush)' },
    ],
  },
  topo: {
    title: 'Topography',
    items: [
      { color: 'rgb(0,20,140)',   label: 'Deep ocean' },
      { color: 'rgb(20,100,180)', label: 'Shallow ocean' },
      { color: 'rgb(100,200,240)', label: 'Coastal / shelf' },
      { color: 'rgb(50,210,70)',  label: 'Lowland' },
      { color: 'rgb(210,190,50)', label: 'Highland' },
      { color: 'rgb(190,110,35)', label: 'Mountains' },
      { color: 'rgb(255,255,255)', label: 'Peaks / ice' },
    ],
  },
  biome: {
    title: 'Biome (Whittaker)',
    items: [
      { color: 'rgb(240,248,255)', label: 'Ice / polar desert' },
      { color: 'rgb(200,220,240)', label: 'Tundra' },
      { color: 'rgb(100,130,100)', label: 'Boreal forest' },
      { color: 'rgb(60,120,50)',   label: 'Temperate rainforest' },
      { color: 'rgb(80,150,60)',   label: 'Temperate deciduous' },
      { color: 'rgb(200,200,120)', label: 'Grassland / shrubland' },
      { color: 'rgb(240,220,130)', label: 'Hot desert' },
      { color: 'rgb(170,200,80)',  label: 'Savanna' },
      { color: 'rgb(10,80,10)',    label: 'Tropical rainforest' },
    ],
  },
  soil: {
    title: 'Soil Orders (USDA)',
    items: [
      { color: 'rgb(160,120,60)',  label: 'Alfisols (moist forest)' },
      { color: 'rgb(100,80,40)',   label: 'Andisols (volcanic)' },
      { color: 'rgb(230,200,120)', label: 'Aridisols (desert)' },
      { color: 'rgb(200,180,140)', label: 'Entisols (young/thin)' },
      { color: 'rgb(220,240,255)', label: 'Gelisols (permafrost)' },
      { color: 'rgb(60,100,60)',   label: 'Histosols (organic/bog)' },
      { color: 'rgb(140,170,100)', label: 'Inceptisols (weakly dev.)' },
      { color: 'rgb(180,140,80)',  label: 'Mollisols (grassland)' },
      { color: 'rgb(80,50,20)',    label: 'Oxisols (tropical)' },
      { color: 'rgb(100,130,160)', label: 'Spodosols (boreal)' },
      { color: 'rgb(120,80,40)',   label: 'Ultisols (humid subtrop.)' },
      { color: 'rgb(160,100,140)', label: 'Vertisols (clay-rich)' },
    ],
  },
  plates: {
    title: 'Tectonic Plates',
    items: [
      { color: 'rgba(128,200,100,0.5)', label: 'Plate (random colour)' },
      { color: 'rgba(200,100,128,0.5)', label: 'Plate boundary' },
    ],
  },
  weather: {
    title: 'Weather Patterns',
    items: [
      { color: 'rgb(255,130,10)',  label: 'ITCZ (tropical convergence)' },
      { color: 'rgb(40,130,230)',  label: 'Polar front' },
      { color: 'rgb(255,240,120)', label: 'Subtropical high (dry air)' },
      { color: 'rgb(130,140,165)', label: 'Orographic front' },
      { color: 'rgba(255,80,50,0.9)',  label: '🌀 Tropical cyclone' },
      { color: 'rgba(80,160,255,0.9)', label: '🌀 Extratropical cyclone' },
      { color: 'rgb(40,200,255)',  label: '💨 Wind particles (toggle)' },
    ],
  },
};

// ── Weather state ─────────────────────────────────────────────────────────────
let weatherMonth = 0;         // currently displayed month (0 = January)
let weatherLayerPausedSim = false;
let lastWeatherResult: api.WeatherPatternResult | null = null; // cached for wind toggle

/** Build an RGBA texture from weather pattern data — meteorological map style.
 *
 * The texture is mostly transparent so the terrain shows through as a base.
 * Atmospheric features are rendered as semi-transparent colored overlays:
 *   - ITCZ: warm orange glow near the equatorial convergence zone
 *   - Polar front: cool blue tint at 50-70° latitude
 *   - Subtropical high: faint pale-yellow haze at 22-38° (dry, sinking air)
 *   - Orographic front: grey shadow on steep mountain slopes
 *   - Cyclones: circular gradient blooms (red for tropical, blue for extratropical)
 *
 * Jet-stream animation is handled separately by the wind-particle canvas overlay,
 * so it is intentionally omitted from the static texture.
 */
function renderWeatherPattern(result: api.WeatherPatternResult): Uint8Array {
  const cellCount = GRID_SIZE * GRID_SIZE;
  const rgba = new Uint8Array(cellCount * 4); // all zeros = transparent

  for (let i = 0; i < cellCount; i++) {
    const front = result.frontType[i] ?? 0;
    const intensity = result.frontIntensity[i] ?? 0;
    if (intensity < 0.05 || front === 0) continue;

    let r = 0, g = 0, b = 0, a = 0;

    switch (front) {
      case 1: // ITCZ — warm orange/amber convergence zone
        r = 255; g = 130; b = 10;
        a = Math.round(intensity * 170);
        break;
      case 2: // Polar front — cold blue band
        r = 40; g = 130; b = 230;
        a = Math.round(intensity * 160);
        break;
      case 3: // Subtropical high — very faint pale-yellow (dry descending air)
        r = 255; g = 240; b = 120;
        a = Math.round(intensity * 55);
        break;
      case 4: // Orographic front — grey mountain shadow
        r = 130; g = 140; b = 165;
        a = Math.round(intensity * 130);
        break;
    }

    rgba[i * 4 + 0] = r;
    rgba[i * 4 + 1] = g;
    rgba[i * 4 + 2] = b;
    rgba[i * 4 + 3] = a;
  }

  // Overlay cyclone positions as radial gradient "blooms"
  const gs = GRID_SIZE;
  for (const cyc of result.cyclonePositions ?? []) {
    const col = Math.round(((cyc.lon + 180) / 360) * gs);
    const row = Math.round(((90 - cyc.lat) / 180) * gs);
    const radius = Math.round(6 + cyc.intensity * 8);

    for (let dr = -radius; dr <= radius; dr++) {
      for (let dc = -radius; dc <= radius; dc++) {
        const dist2 = dr * dr + dc * dc;
        if (dist2 > radius * radius) continue;
        const nr = row + dr;
        const nc = ((col + dc) % gs + gs) % gs;
        if (nr < 0 || nr >= gs) continue;
        const idx = nr * gs + nc;
        const t = Math.pow(1 - Math.sqrt(dist2) / radius, 1.5);
        const alpha = Math.round(t * 200 * cyc.intensity);
        if (cyc.type === 1) {
          // Tropical — deep red/orange core
          rgba[idx * 4 + 0] = Math.max(rgba[idx * 4 + 0], Math.round(255 * t));
          rgba[idx * 4 + 1] = Math.max(rgba[idx * 4 + 1], Math.round(60 * t));
          rgba[idx * 4 + 2] = 0;
          rgba[idx * 4 + 3] = Math.max(rgba[idx * 4 + 3], alpha);
        } else {
          // Extratropical — blue/white spiral
          rgba[idx * 4 + 0] = Math.max(rgba[idx * 4 + 0], Math.round(80 * t));
          rgba[idx * 4 + 1] = Math.max(rgba[idx * 4 + 1], Math.round(160 * t));
          rgba[idx * 4 + 2] = Math.max(rgba[idx * 4 + 2], Math.round(255 * t));
          rgba[idx * 4 + 3] = Math.max(rgba[idx * 4 + 3], alpha);
        }
      }
    }
  }

  return rgba;
}

/**
 * Fetch map data for a specific non-plate layer and convert to a displayable RGBA texture.
 * Returns `null` for layers whose texture update is handled internally (e.g. 'biome').
 */
async function fetchLayerRgba(layer: string): Promise<Uint8Array | null> {
  const cellCount = GRID_SIZE * GRID_SIZE;
  const rgba = new Uint8Array(cellCount * 4);

  if (layer === 'temperature') {
    const data = await api.getTemperatureMap();
    for (let i = 0; i < cellCount; i++) {
      const [r, g, b] = temperatureColor(data[i]);
      rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = 180;
    }
  } else if (layer === 'precipitation') {
    const data = await api.getPrecipitationMap();
    for (let i = 0; i < cellCount; i++) {
      const [r, g, b] = precipitationColor(data[i]);
      rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = 180;
    }
  } else if (layer === 'clouds') {
    const data = await api.getPrecipitationMap();
    for (let i = 0; i < cellCount; i++) {
      const [r, g, b] = cloudColor(data[i]);
      rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = 160;
    }
  } else if (layer === 'biomass') {
    const data = await api.getBiomassMap();
    for (let i = 0; i < cellCount; i++) {
      const [r, g, b] = biomassColor(data[i]);
      rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = 180;
    }
  } else if (layer === 'topo') {
    const data = await api.getHeightMap();
    // Heights are in real metres. Isostatic equilibrium sets ocean cells near
    // −3200 m and continental land near +1863 m (±500 m noise).  Dynamic
    // per-run min/max normalisation compresses all land into 1–2 colour bands
    // (and can make everything look white when the range is small).
    // Instead use fixed physical reference points so the colour palette is
    // always physically meaningful regardless of the current height distribution:
    //   ocean: actual height mapped against fixed ocean reference [−6000, 0]
    //   land:  actual height mapped against fixed land reference [0, 4000]
    //           0 m → lowland green, 1500 m → highland yellow,
    //           3000 m → mountains, > 4000 m → upper peaks (never all-white).
    const OCEAN_REF_LO = -6000, OCEAN_REF_HI = -100;
    const LAND_REF_LO  =     0, LAND_REF_HI  = 4000;
    for (let i = 0; i < cellCount; i++) {
      const h = data[i];
      let scaled: number;
      if (h < 0) {
        // Clamp to ocean reference range then map to topoColor ocean palette [−7000, −200].
        const t = Math.max(0, Math.min(1, (h - OCEAN_REF_LO) / (OCEAN_REF_HI - OCEAN_REF_LO)));
        scaled = t * (-200 - (-7000)) + (-7000); // maps to [−7000, −200]
      } else {
        // Clamp to land reference range then map to topoColor land palette [0, 4000].
        scaled = Math.min(h, LAND_REF_HI);
      }
      const [r, g, b] = topoColor(scaled);
      rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = 200;
    }
  } else if (layer === 'biome') {
    // Biome (Whittaker) overlay: use temperature × precipitation classification.
    // The texture update is handled internally via updateClimateMap; return null.
    const [tempData, precipData] = await Promise.all([
      api.getTemperatureMap(),
      api.getPrecipitationMap(),
    ]);
    renderer.updateClimateMap(
      new Float32Array(tempData),
      new Float32Array(precipData),
      GRID_SIZE,
    );
    return null;
  } else if (layer === 'soil') {
    // Soil orders (USDA): fetch soil type map from backend.
    const soilData = await api.getSoilMap();
    for (let i = 0; i < cellCount; i++) {
      const [r, g, b] = soilOrderColor(soilData[i]);
      rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = 200;
    }
  } else {
    // Unrecognised layer: fall back to biome colours.
    const [tempData, precipData] = await Promise.all([
      api.getTemperatureMap(),
      api.getPrecipitationMap(),
    ]);
    renderer.updateClimateMap(
      new Float32Array(tempData),
      new Float32Array(precipData),
      GRID_SIZE,
    );
    return null;
  }
  return rgba;
}

shell.onLayerToggle(async (layer: string, active: boolean) => {
  try {
    if (layer === 'labels') {
      // Phase L5: geographic feature label overlay.
      labelRenderer.setVisible(active);
      return; // No globe texture change needed for labels.
    } else if (layer === 'plates') {
      if (active) {
        const plateData = await api.getPlateMap();
        renderer.updatePlateMap(new Uint16Array(plateData), GRID_SIZE);
      }
      renderer.setPlateOverlayVisible(active);
    } else if (layer === 'weather') {
      if (active) {
        activeDataLayers.add(layer);
        // Pause simulation while browsing weather patterns
        if (!paused) {
          paused = true;
          shell.setPaused(true);
          weatherLayerPausedSim = true;
        }
        shell.showWeatherMonthSelector(weatherMonth);
        const result = await api.getWeatherPattern(weatherMonth);
        lastWeatherResult = result;
        const rgba = renderWeatherPattern(result);
        renderer.updateColorMap(rgba, GRID_SIZE);
        renderer.setBiomeOverlayVisible(true);
      } else {
        activeDataLayers.delete(layer);
        shell.hideWeatherMonthSelector(); // also fires windToggleCb(false) if wind was on
        renderer.stopWindAnimation();
        lastWeatherResult = null;
        if (weatherLayerPausedSim) {
          paused = false;
          shell.setPaused(false);
          weatherLayerPausedSim = false;
        }
        if (activeDataLayers.size === 0) {
          renderer.setBiomeOverlayVisible(false);
        }
      }
    } else {
      if (active) {
        activeDataLayers.add(layer);
        const rgba = await fetchLayerRgba(layer);
        if (rgba !== null) {
          renderer.updateColorMap(rgba, GRID_SIZE);
        }
        renderer.setBiomeOverlayVisible(true);
      } else {
        activeDataLayers.delete(layer);
        if (activeDataLayers.size === 0) {
          renderer.setBiomeOverlayVisible(false);
        }
      }
    }

    // Show or hide the legend for the active layer
    if (active) {
      const legend = LAYER_LEGENDS[layer];
      if (legend) {
        shell.showLayerLegend(legend.title, legend.items);
      }
    } else {
      // Show legend for any other still-active non-plate layer
      const otherActive = [...activeDataLayers].find(l => LAYER_LEGENDS[l]);
      if (otherActive) {
        const legend = LAYER_LEGENDS[otherActive];
        shell.showLayerLegend(legend.title, legend.items);
      } else {
        shell.hideLayerLegend();
      }
    }
  } catch (err) {
    console.error(`Failed to toggle layer ${layer}:`, err);
  }
});

// Re-fetch and display weather when month changes
shell.onWeatherMonthChange(async (month: number) => {
  weatherMonth = month;
  if (activeDataLayers.has('weather')) {
    try {
      const result = await api.getWeatherPattern(month);
      lastWeatherResult = result;
      const rgba = renderWeatherPattern(result);
      renderer.updateColorMap(rgba, GRID_SIZE);
      // Restart wind animation with new month's data if it is currently active
      if (renderer.isWindAnimationActive()) {
        renderer.startWindAnimation(result.windU, result.windV, GRID_SIZE);
      }
    } catch (err) {
      console.error('Weather month change failed:', err);
    }
  }
});

// Wind map toggle
shell.onWindToggle((active: boolean) => {
  if (active && lastWeatherResult) {
    renderer.startWindAnimation(lastWeatherResult.windU, lastWeatherResult.windV, GRID_SIZE);
  } else {
    renderer.stopWindAnimation();
  }
});

// ── Globe click handling ──────────────────────────────────────────────────────
// In draw mode: collect points for a cross-section path.
// Otherwise: inspect the clicked cell and show an info popup.

shell.onInspectClick((x: number, y: number) => {
  const viewportEl = shell.getViewportElement();
  const latLon = renderer.screenToLatLon(
    x,
    y,
    viewportEl.clientWidth,
    viewportEl.clientHeight,
  );
  if (!latLon) return;

  if (drawModeActive) {
    drawPoints.push(latLon);

    if (drawPoints.length >= 2) {
      // Have enough points, request cross-section from backend
      api.getCrossSection(drawPoints)
        .then((profile) => {
          lastCrossSectionProfile = profile;
          shell.showCrossSection();
          renderCrossSectionToPanel(profile, shell.areLabelsVisible());
          drawModeActive = false;
          shell.setDrawMode(false);
        })
        .catch((err) => {
          console.error('Cross-section request failed:', err);
        });
    }
    return;
  }

  // Inspect mode: convert lat/lon → cell index → fetch info from backend
  const { lat, lon } = latLon;
  const normLon = (lon + 180) / 360;        // [0, 1]
  const normLat = (90 - lat) / 180;         // [0, 1], north = 0
  const col = Math.min(Math.floor(normLon * GRID_SIZE), GRID_SIZE - 1);
  const row = Math.min(Math.floor(normLat * GRID_SIZE), GRID_SIZE - 1);
  const cellIndex = row * GRID_SIZE + col;

  // Store for auto-refresh on subsequent advances.
  selectedCellIndex = cellIndex;
  selectedCellLat   = lat;
  selectedCellLon   = lon;

  refreshInspectCell();
});

// ── Wire UI callbacks ───────────────────────────────────────────────────────

shell.onAbortRequest(() => {
  simRequestController?.abort();
  simRequestController = null;
  pendingSimRequest = false;
  simRequestStartMs = 0;
  shell.setProgressText('');
  resetAgentStatuses();
});

shell.onNewPlanet(() => {
  doGeneratePlanet(randomSeed());
});

shell.onPauseToggle(() => {
  const wasPlaying = paused;
  paused = !paused;
  shell.setPaused(paused);

  // Clicking play while weather layer is active → deactivate weather layer
  if (wasPlaying && !paused && activeDataLayers.has('weather')) {
    // Deactivate the weather layer via the layer toggle callback so it handles
    // hiding the selector, resetting overlay, etc.
    shell.deactivateLayer('weather');
  }
});

shell.onRateChange((rate) => {
  simRate = Math.max(0.001, Math.min(100, rate));
});

shell.onSaveState(async () => {
  try {
    await api.takeSnapshot();
    shell.setLoadStateEnabled(true);
  } catch (err) {
    console.error('Save state failed:', err);
  }
});

shell.onLoadState(async () => {
  try {
    // Restore the most recent snapshot
    const info = await api.listSnapshots();
    if (info.count === 0) return;
    const latestTime = Math.max(...info.times);
    const result = await api.restoreSnapshot(latestTime);
    simTimeMa = result.restoredTimeMa;
    shell.setSimTime(simTimeMa);
    shell.setTimelineCursor(4500 + simTimeMa);

    // Refresh display data after restore
    const [heightData, tempData, precipData] = await Promise.all([
      api.getHeightMap(),
      api.getTemperatureMap(),
      api.getPrecipitationMap(),
    ]);
    const heightMap = new Float32Array(heightData);
    renderer.updateHeightMap(heightMap, GRID_SIZE);
    renderer.updateBiomeBaseMap(
      new Float32Array(tempData),
      new Float32Array(precipData),
      heightMap,
      GRID_SIZE,
    );

    // Phase L6: refresh feature labels after snapshot restore.
    api.fetchFeatureLabels()
      .then((labels) => labelRenderer.setLabels(labels))
      .catch(() => {/* non-critical */});
  } catch (err) {
    console.error('Load state failed:', err);
  }
});

// Wire first-person mode indicator
renderer.onCameraChange((isFirstPerson) => {
  shell.setFirstPersonMode(isFirstPerson);
});

// Wire log panel open callback
shell.onLogOpen(() => {
  refreshLogPanel();
});

// ── LLM Settings Panel wiring (Phase D3) ────────────────────────────────────

/** Fetch the provider list and populate the LLM panel. */
async function refreshLlmPanel(): Promise<void> {
  try {
    const providers = await api.getLlmProviders();
    shell.setLlmProviders(providers);
  } catch (err) {
    console.error('Failed to load LLM providers:', err);
  }
}

// Wire provider selection / API-key apply
shell.onLlmSettingsChanged(async (provider, settings) => {
  try {
    await api.setLlmActive(provider, settings);
    // Refresh panel to reflect new active state
    await refreshLlmPanel();
  } catch (err) {
    console.error('Failed to set LLM provider:', err);
  }
});

// Wire local-provider setup flow
shell.onLlmSetup(async (provider) => {
  try {
    await api.startLlmSetup(provider);
    // Open SSE stream for progress events
    api.openLlmSetupProgress(provider, (event) => {
      shell.showLlmSetupProgress(event);
      if (event.isComplete) {
        // Refresh provider list now that setup is done
        refreshLlmPanel().catch((err) => console.warn('Failed to refresh LLM panel after setup:', err));
      }
    });
  } catch (err) {
    console.error(`LLM setup failed for ${provider}:`, err);
    shell.showLlmSetupProgress({
      step: 'Error',
      percentTotal: 0,
      detail: '',
      isComplete: false,
      isError: true,
      errorMessage: String(err),
    });
  }
});

// Fetch provider list once on startup so the panel is ready when opened
refreshLlmPanel().catch((err) => console.debug('LLM panel refresh failed at startup (backend may not be ready):', err));

// ── Render loop ─────────────────────────────────────────────────────────────
// The render loop now only handles display and periodically asks the backend
// to advance the simulation and fetches updated state.

let lastTime = performance.now();
let frameCount = 0;
let fpsAccum = 0;

/** Accumulator for backend simulation calls. */
let simAccum = 0;
const SIM_UPDATE_INTERVAL = 200; // ms between backend simulation calls

/** Flag to prevent overlapping backend requests. */
let pendingSimRequest = false;
/** Timestamp (ms) when the current sim request was dispatched. */
let simRequestStartMs = 0;
/** AbortController for the in-flight simulation advance fetch. */
let simRequestController: AbortController | null = null;

/** Cell index of the currently pinned inspect cell (–1 = none). */
let selectedCellIndex = -1;
let selectedCellLat   = 0;
let selectedCellLon   = 0;

/** Last tick timing stats for the log panel. */
let lastTickStats: api.TickStats | null = null;

/** Total ticks completed (from backend advance response). */
let totalTickCount = 0;

/** Re-fetch and display the pinned cell's inspection data. */
function refreshInspectCell(): void {
  if (selectedCellIndex < 0) return;
  const lat = selectedCellLat;
  const lon = selectedCellLon;
  const cellIndex = selectedCellIndex;
  api.inspectCell(cellIndex)
    .then((info) => {
      shell.showInspectPanel({
        lat,
        lon,
        elevation: info.height,
        crustThickness: info.crustThickness,
        rockType: ROCK_TYPE_NAMES[info.rockType] ?? `Type ${info.rockType}`,
        rockAge: info.rockAge,
        plateId: info.plateId,
        soilOrder: SOIL_ORDER_NAMES[info.soilType] ?? `Order ${info.soilType}`,
        soilDepth: info.soilDepth,
        temperature: info.temperature,
        precipitation: info.precipitation,
        biomass: info.biomass,
        biomatterDensity: info.biomatterDensity,
        organicCarbon: info.organicCarbon,
        reefPresent: info.reefPresent,
      });
    })
    .catch((err) => {
      console.error('Cell inspect failed:', err);
    });
}

// ── Description Modal (Phase D5) ────────────────────────────────────────────

shell.onDescribeOpen(() => {
  if (selectedCellIndex < 0) return;
  shell.showDescriptionModal();
  api.describeCell(selectedCellIndex)
    .then((resp) => {
      shell.populateDescriptionModal(resp);
    })
    .catch((err) => {
      console.error('Description failed:', err);
    });
});

// ── Event Layer Overlay (Phase D6) ──────────────────────────────────────────

shell.onEventLayerChange(async (eventType: string | null) => {
  if (!eventType) {
    renderer.setEventLayerVisible(false);
    return;
  }
  try {
    const values = await api.fetchEventLayerMap(eventType);
    renderer.updateEventLayerMap(values, GRID_SIZE, eventType);
    renderer.setEventLayerVisible(true);
  } catch (err) {
    console.error('Event layer fetch failed:', err);
    renderer.setEventLayerVisible(false);
  }
});

// ── Agent status tracking ───────────────────────────────────────────────────
// Track the last known status of each simulation engine phase.  Updated via
// SignalR progress events and displayed in the clickable agent status panel.

/** Each agent's latest status: 'idle' | 'running' | 'done'. */
const agentStatuses: Record<string, 'idle' | 'running' | 'done'> = {
  tectonic:   'idle',
  surface:    'idle',
  atmosphere: 'idle',
  vegetation: 'idle',
  biomatter:  'idle',
};

function updateAgentStatuses(phase: string): void {
  // Phases arrive in order: tectonic → surface (parallel: atmosphere+vegetation) → biomatter → complete
  if (phase === 'tectonic') {
    agentStatuses.tectonic = 'running';
    agentStatuses.surface = 'idle';
    agentStatuses.atmosphere = 'idle';
    agentStatuses.vegetation = 'idle';
    agentStatuses.biomatter = 'idle';
  } else if (phase === 'surface') {
    agentStatuses.tectonic = 'done';
    agentStatuses.surface = 'running';
    agentStatuses.atmosphere = 'running';
    agentStatuses.vegetation = 'running';
  } else if (phase === 'biomatter') {
    agentStatuses.surface = 'done';
    agentStatuses.atmosphere = 'done';
    agentStatuses.vegetation = 'done';
    agentStatuses.biomatter = 'running';
  } else if (phase === 'complete') {
    agentStatuses.biomatter = 'done';
  } else {
    // Unknown phase — log and leave statuses unchanged so the panel remains readable.
    console.debug(`[agents] unknown phase: ${phase}`);
  }
  shell.updateAgentStatuses(agentStatuses);
}

function resetAgentStatuses(): void {
  for (const key of Object.keys(agentStatuses)) {
    agentStatuses[key] = 'idle';
  }
  shell.updateAgentStatuses(agentStatuses);
}

/** Update agent statuses based on last-tick timing stats so the panel shows which engines actually ran. */
function updateAgentStatusesFromStats(stats: api.TickStats | null): void {
  if (!stats) { resetAgentStatuses(); return; }
  agentStatuses.tectonic   = stats.tectonicMs   > 0 ? 'done' : 'idle';
  agentStatuses.surface    = stats.surfaceMs     > 0 ? 'done' : 'idle';
  agentStatuses.atmosphere = stats.atmosphereMs  > 0 ? 'done' : 'idle';
  agentStatuses.vegetation = stats.vegetationMs  > 0 ? 'done' : 'idle';
  agentStatuses.biomatter  = stats.biomatterMs   > 0 ? 'done' : 'idle';
  shell.updateAgentStatuses(agentStatuses);
}

/** Refresh the log panel with current timing stats and recent events. */
function refreshLogPanel(): void {
  api.getEvents(20)
    .then((events) => {
      shell.updateLogPanel(lastTickStats, events, totalTickCount);
    })
    .catch(() => {
      shell.updateLogPanel(lastTickStats, [], totalTickCount);
    });
}

// ── SignalR connection for real-time progress events ────────────────────────
// We connect to the SignalR hub to receive engine-phase progress events that
// the backend broadcasts while processing each simulation tick.  The main
// simulation loop still drives advances via the REST API; SignalR is used
// only for lightweight status feedback.
const PHASE_LABELS: Record<string, string> = {
  tectonic:  '⛰ Tectonic\u2026',
  surface:   '🌊 Surface\u2026',
  biomatter: '🌿 Biomatter\u2026',
  complete:  '',
};

const simSocket = api.createSimulationSocket({
  onConnected: (event) => {
    // When the hub sends compute info on connection, update the toolbar indicator.
    if (event.computeMode !== undefined && event.computeDevice !== undefined) {
      shell.setComputeMode(event.computeMode === 'GPU', event.computeDevice, event.computeMemoryMb ?? 0, renderer.getWebGLRendererInfo());
    }
  },
  onProgress: (event) => {
    // Only update progress text while an advance is in flight, to avoid
    // late-arriving SignalR messages overwriting the cleared state after the
    // REST response has already been processed.
    if (pendingSimRequest) {
      shell.setProgressText(PHASE_LABELS[event.phase] ?? '');
    }
    updateAgentStatuses(event.phase);
  },
  onTick: (event) => {
    // Update time display on SignalR ticks too (when SignalR advance is used).
    simTimeMa = event.timeMa;
    shell.setSimTime(simTimeMa);
  },
  onFeaturesUpdated: (event) => {
    // Phase L6: merge changed labels into the renderer's cache.
    // We do a full refresh via the REST endpoint to ensure consistency;
    // the SignalR payload is used only to trigger the refresh.
    if (labelRenderer.isVisible() && event.labels.length > 0) {
      api.fetchFeatureLabels()
        .then((labels) => labelRenderer.setLabels(labels))
        .catch(() => {/* non-critical */});
    }
  },
});

// Connect with a short retry delay — the backend may not be ready immediately.
setTimeout(() => simSocket.connect(), 500);

// ── Simulation tick loop (runs via setInterval, independent of rendering) ───
// This ensures the simulation advances even when the browser tab is hidden,
// since requestAnimationFrame is paused by browsers for hidden tabs.

/** Wall-clock timestamp of the most recently dispatched simulation tick. */
let simLastWallMs = performance.now();

function simTick(): void {
  if (paused || pendingSimRequest) return;

  const nowMs = performance.now();
  const dtMs = nowMs - simLastWallMs;
  simLastWallMs = nowMs;

  simAccum += dtMs;
  if (simAccum < SIM_UPDATE_INTERVAL) return;

  const deltaMa = (simAccum / 1000) * simRate;
  simAccum = 0;
  if (deltaMa <= 0) return;

  pendingSimRequest = true;
  simRequestStartMs = performance.now();

  shell.setProgressText('⏳ Advancing…');
  // Mark all agents as running immediately so the panel shows activity during the request.
  for (const key of Object.keys(agentStatuses)) {
    agentStatuses[key] = 'running';
  }
  shell.updateAgentStatuses(agentStatuses);
  simRequestController = new AbortController();
  api.advanceSimulation(deltaMa, simRequestController.signal)
    .then(async (result) => {
      simTimeMa = result.timeMa;
      shell.setSimTime(simTimeMa);
      shell.setTimelineCursor(4500 + simTimeMa);
      shell.setProgressText('');

      // Update timing stats and tick count.
      if (result.stats) {
        lastTickStats = result.stats;
      }
      if (result.tickCount !== undefined) {
        totalTickCount = result.tickCount;
      }
      // Use stats from response to update the agent panel with last-tick timing.
      updateAgentStatusesFromStats(lastTickStats);

      // Refresh the log panel if it's open.
      if (shell.isLogPanelOpen) {
        refreshLogPanel();
      }

      // Fetch updated height+temperature+precipitation in a single request.
      const bundle = await api.getStateBundle(GRID_SIZE * GRID_SIZE);
      const heightMap = bundle.heightMap;
      renderer.updateHeightMap(heightMap, GRID_SIZE);

      // Auto-refresh the cell info panel if a cell is pinned.
      refreshInspectCell();

      // Keep the biome base texture in sync for the default view.
      if (activeDataLayers.size === 0) {
        renderer.updateBiomeBaseMap(
          bundle.temperatureMap,
          bundle.precipitationMap,
          heightMap,
          GRID_SIZE,
        );
      }

      // Re-render any active data overlays so they reflect the new state.
      // This keeps the visual overlay in sync with the updated simulation.
      if (activeDataLayers.size > 0) {
        // Refresh the most-recently-activated overlay (the one currently visible).
        const layers = [...activeDataLayers];
        const layerToRefresh = layers[layers.length - 1];
        if (layerToRefresh !== undefined) {
          const rgba = await fetchLayerRgba(layerToRefresh);
          if (rgba !== null) {
            renderer.updateColorMap(rgba, GRID_SIZE);
          }
        }
      }
    })
    .catch((err: unknown) => {
      if (err instanceof Error && err.name === 'AbortError') return; // user-initiated or timeout-based abort
      console.error('Simulation advance failed:', err);
      shell.setProgressText('');
      resetAgentStatuses();
    })
    .finally(() => {
      pendingSimRequest = false;
      simRequestController = null;
    });
}

// Run simulation tick every SIM_UPDATE_INTERVAL ms, regardless of tab visibility.
setInterval(simTick, SIM_UPDATE_INTERVAL);

function loop(now: number): void {
  requestAnimationFrame(loop);

  const dtMs = now - lastTime;
  lastTime = now;

  // Freeze detection: warn when a sim request is taking too long
  if (pendingSimRequest) {
    const elapsedSec = Math.round((performance.now() - simRequestStartMs) / 1000);
    if (elapsedSec >= 15) {
      shell.setProgressText(`⚠️ Frozen? (${elapsedSec}s) — click to abort`);
      if (elapsedSec >= 60) {
        simRequestController?.abort();
        simRequestController = null;
        pendingSimRequest = false;
        simRequestStartMs = 0;
        shell.setProgressText('');
        resetAgentStatuses();
      }
    }
  }

  // FPS counter (update roughly every 500 ms)
  frameCount++;
  fpsAccum += dtMs;
  if (fpsAccum >= 500) {
    shell.setFps((frameCount / fpsAccum) * 1000);
    frameCount = 0;
    fpsAccum = 0;
  }

  renderer.render();

  // Phase L5: update label positions after the 3D render so the camera matrix is current.
  if (labelRenderer.isVisible()) {
    labelRenderer.update(
      renderer.getCamera(),
      viewportEl.clientWidth,
      viewportEl.clientHeight,
      renderer.getCameraDistance(),
    );
  }
}

requestAnimationFrame(loop);

// ── Window resize ───────────────────────────────────────────────────────────

function onResize(): void {
  const w = viewportEl.clientWidth;
  const h = viewportEl.clientHeight;
  if (w > 0 && h > 0) {
    renderer.resize(w, h);
  }
}

window.addEventListener('resize', onResize);
// Initial size sync
onResize();
