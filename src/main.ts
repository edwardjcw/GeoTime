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
import {
  biomassColor,
  biomatterColor,
  cloudColor,
  computeMapStats,
  computeTopoExtents,
  organicCarbonColor,
  precipitationColor,
  scaleTopoHeight,
  soilOrderColor,
  stretchToPhysicalRange,
  temperatureColor,
  topoColor,
  type MapStats,
} from './layer-color-mapping';
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
/** Simulation rate (Ma per second of real time). Default matches the slider default (0.010 Ma/s). */
let simRate = 0.01;

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

    lastStateBundle = {
      heightMap,
      temperatureMap: new Float32Array(tempData),
      precipitationMap: new Float32Array(precipData),
    };

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

    api.reportClientPerf('client_planet_generate', {
      seed: result.seed,
      plateCount: result.plateCount,
      hotspotCount: result.hotspotCount,
      timeMa: simTimeMa,
      triangleCount: renderer.getTriangleCount(),
    });
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

/**
 * Reconnect to an existing planet running on the backend without regenerating.
 * Fetches and displays all current state.
 */
async function doReconnectPlanet(seed: number, timeMa: number): Promise<void> {
  try {
    simTimeMa = timeMa;

    // Fetch current display data from backend
    const [heightData, plateData, tempData, precipData] = await Promise.all([
      api.getHeightMap(),
      api.getPlateMap(),
      api.getTemperatureMap(),
      api.getPrecipitationMap(),
    ]);

    const heightMap = new Float32Array(heightData);
    const plateMap = new Uint16Array(plateData);

    lastStateBundle = {
      heightMap,
      temperatureMap: new Float32Array(tempData),
      precipitationMap: new Float32Array(precipData),
    };

    renderer.updateHeightMap(heightMap, GRID_SIZE);
    renderer.updatePlateMap(plateMap, GRID_SIZE);

    renderer.updateBiomeBaseMap(
      new Float32Array(tempData),
      new Float32Array(precipData),
      heightMap,
      GRID_SIZE,
    );

    shell.setSeed(seed);
    shell.setTriangleCount(renderer.getTriangleCount());
    shell.setSimTime(simTimeMa);
    shell.setTimelineCursor(4500 + simTimeMa);

    api.listSnapshots().then((info) => {
      shell.setLoadStateEnabled(info.count > 0);
    }).catch(() => {/* ignore */});

    api.getComputeInfo().then((info) => {
      shell.setComputeMode(info.isGpu, info.deviceName, info.memoryMb ?? 0, renderer.getWebGLRendererInfo());
    }).catch(() => {/* backend may not be ready yet – ignore */});

    api.fetchFeatureLabels()
      .then((labels) => labelRenderer.setLabels(labels))
      .catch(() => {/* labels are non-critical */});

    api.fetchEventLayerTypes()
      .then((types) => shell.setEventLayerTypes(types))
      .catch(() => {/* event layer types are non-critical */});

    window.location.hash = `seed=${seed}`;
  } catch (err) {
    console.error('Failed to reconnect to planet:', err);
    // Fallback: generate a new planet if reconnection fails
    return await doGeneratePlanet(seedFromURL() ?? randomSeed());
  }
}

// On initial load, check if the backend already has a planet running.
// Only generate a new planet if no planet exists.
(async () => {
  try {
    const status = await api.getPlanetStatus();
    if (status.exists) {
      await doReconnectPlanet(status.seed, status.timeMa);
    } else {
      await doGeneratePlanet(seedFromURL() ?? randomSeed());
    }
    api.getDiagnosticsSession()
      .then((info) => {
        console.info(`[GeoTime] Performance session log: ${info.logPath}`);
        api.reportClientPerf('client_session_start', {
          userAgent: navigator.userAgent,
          viewport: { width: window.innerWidth, height: window.innerHeight },
          devicePixelRatio: window.devicePixelRatio,
          logPath: info.logPath,
        });
      })
      .catch(() => {/* diagnostics optional at startup */});
  } catch {
    // Backend may not be reachable; generate a fresh planet
    await doGeneratePlanet(seedFromURL() ?? randomSeed());
  }
})();

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

/** Latest height/temperature/precipitation bundle from the most recent sim advance. */
let lastStateBundle: api.StateBundle | null = null;

function logLayerMapStats(layer: string, stats: MapStats): void {
  console.debug(
    `[layer:${layer}] min=${stats.min.toFixed(3)} max=${stats.max.toFixed(3)} ` +
    `mean=${stats.mean.toFixed(3)} stddev=${stats.stddev.toFixed(3)}`,
  );
}

async function fetchMapFloat32(
  layer: string,
  cached: Float32Array | undefined,
  fetchBinary: () => Promise<ArrayBuffer>,
  fetchJson: () => Promise<number[]>,
): Promise<Float32Array> {
  if (cached) {
    logLayerMapStats(layer, computeMapStats(cached));
    return cached;
  }
  try {
    const buf = await fetchBinary();
    const arr = new Float32Array(buf, 0, GRID_SIZE * GRID_SIZE);
    logLayerMapStats(layer, computeMapStats(arr));
    return arr;
  } catch {
    const json = await fetchJson();
    const arr = Float32Array.from(json);
    logLayerMapStats(layer, computeMapStats(arr));
    return arr;
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
  biomatter: {
    title: 'Biomatter Density',
    items: [
      { color: 'rgb(28,18,80)',    label: '0 kg C/m² (sparse)' },
      { color: 'rgb(59,52,130)',   label: '1 kg C/m²' },
      { color: 'rgb(89,86,180)',   label: '2 kg C/m²' },
      { color: 'rgb(120,120,230)', label: '3+ kg C/m² (dense)' },
    ],
  },
  'organic-carbon': {
    title: 'Organic Carbon',
    items: [
      { color: 'rgb(45,30,12)',   label: '0 kg C/m² (lean)' },
      { color: 'rgb(99,68,27)',   label: '2 kg C/m²' },
      { color: 'rgb(153,106,42)', label: '4 kg C/m²' },
      { color: 'rgb(180,125,50)', label: '5+ kg C/m² (rich)' },
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
    const data = await fetchMapFloat32(
      'temperature',
      lastStateBundle?.temperatureMap,
      api.getTemperatureMapBinary,
      api.getTemperatureMap,
    );
    const stats = computeMapStats(data);
    for (let i = 0; i < cellCount; i++) {
      const t = stretchToPhysicalRange(data[i], stats.min, stats.max, -40, 45, 2);
      const [r, g, b] = temperatureColor(t);
      rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = 180;
    }
  } else if (layer === 'precipitation') {
    const data = await fetchMapFloat32(
      'precipitation',
      lastStateBundle?.precipitationMap,
      api.getPrecipitationMapBinary,
      api.getPrecipitationMap,
    );
    const stats = computeMapStats(data);
    for (let i = 0; i < cellCount; i++) {
      const p = stretchToPhysicalRange(data[i], stats.min, stats.max, 0, 2500, 50);
      const [r, g, b] = precipitationColor(p);
      rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = 180;
    }
  } else if (layer === 'clouds') {
    const data = await fetchMapFloat32(
      'clouds',
      lastStateBundle?.precipitationMap,
      api.getPrecipitationMapBinary,
      api.getPrecipitationMap,
    );
    const stats = computeMapStats(data);
    for (let i = 0; i < cellCount; i++) {
      const p = stretchToPhysicalRange(data[i], stats.min, stats.max, 0, 2500, 50);
      const [r, g, b] = cloudColor(p);
      rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = 160;
    }
  } else if (layer === 'biomass') {
    const data = await fetchMapFloat32(
      'biomass',
      undefined,
      api.getBiomassMapBinary,
      api.getBiomassMap,
    );
    const stats = computeMapStats(data);
    for (let i = 0; i < cellCount; i++) {
      const biomass = stretchToPhysicalRange(data[i], stats.min, stats.max, 0, 12, 0.25);
      const [r, g, b] = biomassColor(biomass);
      rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = 180;
    }
  } else if (layer === 'biomatter') {
    const data = await fetchMapFloat32(
      'biomatter',
      undefined,
      api.getBiomatterMapBinary,
      api.getBiomatterMap,
    );
    const stats = computeMapStats(data);
    for (let i = 0; i < cellCount; i++) {
      const d = stretchToPhysicalRange(data[i], stats.min, stats.max, 0, 3, 0.05);
      const [r, g, b] = biomatterColor(d);
      rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = 185;
    }
  } else if (layer === 'organic-carbon') {
    const data = await fetchMapFloat32(
      'organic-carbon',
      undefined,
      api.getOrganicCarbonMapBinary,
      api.getOrganicCarbonMap,
    );
    const stats = computeMapStats(data);
    for (let i = 0; i < cellCount; i++) {
      const c = stretchToPhysicalRange(data[i], stats.min, stats.max, 0, 5, 0.1);
      const [r, g, b] = organicCarbonColor(c);
      rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = 185;
    }
  } else if (layer === 'topo') {
    const data = await fetchMapFloat32(
      'topo',
      lastStateBundle?.heightMap,
      api.getHeightMapBinary,
      api.getHeightMap,
    );
    const extents = computeTopoExtents(data);
    for (let i = 0; i < cellCount; i++) {
      const scaled = scaleTopoHeight(
        data[i],
        extents.oceanMin,
        extents.oceanMax,
        extents.landMin,
        extents.landMax,
      );
      const [r, g, b] = topoColor(scaled);
      rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = 200;
    }
  } else if (layer === 'biome') {
    const tempData = await fetchMapFloat32(
      'biome.temperature',
      lastStateBundle?.temperatureMap,
      api.getTemperatureMapBinary,
      api.getTemperatureMap,
    );
    const precipData = await fetchMapFloat32(
      'biome.precipitation',
      lastStateBundle?.precipitationMap,
      api.getPrecipitationMapBinary,
      api.getPrecipitationMap,
    );
    renderer.updateClimateMap(tempData, precipData, GRID_SIZE);
    return null;
  } else if (layer === 'soil') {
    const soilData = await api.getSoilMap();
    logLayerMapStats('soil', computeMapStats(soilData));
    for (let i = 0; i < cellCount; i++) {
      const [r, g, b] = soilOrderColor(soilData[i]);
      rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = 200;
    }
  } else {
    const tempData = await fetchMapFloat32(
      'fallback.temperature',
      lastStateBundle?.temperatureMap,
      api.getTemperatureMapBinary,
      api.getTemperatureMap,
    );
    const precipData = await fetchMapFloat32(
      'fallback.precipitation',
      lastStateBundle?.precipitationMap,
      api.getPrecipitationMapBinary,
      api.getPrecipitationMap,
    );
    renderer.updateClimateMap(tempData, precipData, GRID_SIZE);
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

  inspectPanelVisible = true;
  refreshInspectCell({ force: true });
});

// Stop auto-refresh when the user closes the inspect panel (✕ in header).
appEl.addEventListener('click', (e) => {
  const btn = (e.target as HTMLElement).closest('button');
  if (!btn || btn.textContent !== '✕') return;
  const header = btn.parentElement;
  const title = header?.querySelector('span');
  if (title?.textContent === '📍 Cell Info') {
    inspectPanelVisible = false;
  }
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
  const wasPaused = paused;
  paused = !paused;
  shell.setPaused(paused);

  // Resume: kick the completion-driven sim loop (paused ticks do not self-reschedule).
  if (wasPaused && !paused && !pendingSimRequest) {
    scheduleSimTick(0);
  }

  // Clicking play while weather layer is active → deactivate weather layer
  if (wasPaused && !paused && activeDataLayers.has('weather')) {
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

/** P0-2: Minimum delay between simulation advance attempts (ms). */
const SIM_MIN_POLL_MS = 200;
/** Safety margin over last measured backend tick duration. */
const SIM_POLL_FACTOR = 1.05;
/** Default poll delay before first tick stats are known. */
const SIM_DEFAULT_POLL_MS = 500;

/** Compute the next sim poll delay from the last backend tick duration. */
function computeSimPollDelayMs(lastTotalMs: number | null | undefined): number {
  if (lastTotalMs == null || lastTotalMs <= 0 || !Number.isFinite(lastTotalMs)) {
    return SIM_DEFAULT_POLL_MS;
  }
  return Math.max(SIM_MIN_POLL_MS, Math.round(lastTotalMs * SIM_POLL_FACTOR));
}

/** Active completion-driven sim poll timer (independent of rAF). */
let simPollTimer: ReturnType<typeof setTimeout> | null = null;

function scheduleSimTick(delayMs?: number): void {
  if (simPollTimer !== null) {
    clearTimeout(simPollTimer);
    simPollTimer = null;
  }
  const delay = delayMs ?? computeSimPollDelayMs(lastTickStats?.totalMs);
  simPollTimer = setTimeout(() => {
    simPollTimer = null;
    simTick();
  }, delay);
}

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

/** P1-3: Minimum ms between auto-refresh inspect API calls during simulation. */
const INSPECT_REFRESH_INTERVAL_MS = 5000;

/** Whether the inspect panel is currently shown (false after user closes it). */
let inspectPanelVisible = false;
/** Wall-clock time of the last inspect API dispatch. */
let lastInspectRefreshMs = 0;
/** In-flight inspect request guard. */
let inspectRefreshPending = false;

/** P1-3: Whether a throttled auto-refresh should run after a non-skipped advance. */
function shouldAutoRefreshInspectCell(
  panelVisible: boolean,
  cellIndex: number,
  nowMs: number,
  lastRefreshMs: number,
  pending: boolean,
  intervalMs: number,
): boolean {
  if (cellIndex < 0) return false;
  if (!panelVisible) return false;
  if (pending) return false;
  if (nowMs - lastRefreshMs < intervalMs) return false;
  return true;
}

/** Last tick timing stats for the log panel. */
let lastTickStats: api.TickStats | null = null;

/** Total ticks completed (from backend advance response). */
let totalTickCount = 0;
/** Last measured FPS from the render loop. */
let lastMeasuredFps = 0;

/** Re-fetch and display the pinned cell's inspection data. */
function refreshInspectCell(options?: { force?: boolean }): void {
  if (selectedCellIndex < 0) return;

  const force = options?.force ?? false;
  if (
    !force
    && !shouldAutoRefreshInspectCell(
      inspectPanelVisible,
      selectedCellIndex,
      performance.now(),
      lastInspectRefreshMs,
      inspectRefreshPending,
      INSPECT_REFRESH_INTERVAL_MS,
    )
  ) {
    return;
  }

  const lat = selectedCellLat;
  const lon = selectedCellLon;
  const cellIndex = selectedCellIndex;
  inspectRefreshPending = true;
  lastInspectRefreshMs = performance.now();

  api.inspectCell(cellIndex)
    .then((info) => {
      inspectRefreshPending = false;
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
      inspectRefreshPending = false;
      console.error('Cell inspect failed:', err);
    });
}

// ── Description Modal (Phase D5) ────────────────────────────────────────────

let cancelDescriptionStream: (() => void) | null = null;

shell.onDescribeOpen(() => {
  if (selectedCellIndex < 0) return;

  cancelDescriptionStream?.();
  cancelDescriptionStream = null;

  const cellIndex = selectedCellIndex;
  shell.showDescriptionModal();

  let receivedToken = false;
  let settled = false;

  const fallbackToBatch = (reason: unknown) => {
    if (settled) return;
    settled = true;
    cancelDescriptionStream = null;
    console.warn('Description stream failed; falling back to batch response:', reason);
    shell.showDescriptionModal();
    api.describeCell(cellIndex)
      .then((resp) => {
        shell.populateDescriptionModal(resp);
      })
      .catch((err) => {
        console.error('Description failed:', err);
        shell.showDescriptionError('Description generation failed. Check the backend logs for details.');
      });
  };

  cancelDescriptionStream = api.describeStream(
    cellIndex,
    (token) => {
      receivedToken = true;
      shell.appendDescriptionToken(token);
    },
    () => {
      if (settled) return;
      if (!receivedToken) {
        fallbackToBatch(new Error('Description stream ended without content'));
        return;
      }
      settled = true;
      cancelDescriptionStream = null;
    },
    fallbackToBatch,
  );
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
  'tectonic:advection':  'idle',
  'tectonic:collision':  'idle',
  'tectonic:boundaries': 'idle',
  'tectonic:dynamics':   'idle',
  'tectonic:volcanism':  'idle',
  surface:    'idle',
  atmosphere: 'idle',
  vegetation: 'idle',
  biomatter:  'idle',
};

function updateAgentStatuses(phase: string): void {
  if (phase === 'tectonic') {
    // Old-style "tectonic" phase — mark first sub-phase as running
    agentStatuses['tectonic:advection'] = 'running';
    agentStatuses['tectonic:collision'] = 'idle';
    agentStatuses['tectonic:boundaries'] = 'idle';
    agentStatuses['tectonic:dynamics'] = 'idle';
    agentStatuses['tectonic:volcanism'] = 'idle';
    agentStatuses.surface = 'idle';
    agentStatuses.atmosphere = 'idle';
    agentStatuses.vegetation = 'idle';
    agentStatuses.biomatter = 'idle';
  } else if (phase === 'tectonic:advection') {
    agentStatuses['tectonic:advection'] = 'done';
    agentStatuses['tectonic:collision'] = 'running';
  } else if (phase === 'tectonic:collision') {
    agentStatuses['tectonic:collision'] = 'done';
    agentStatuses['tectonic:boundaries'] = 'running';
  } else if (phase === 'tectonic:boundaries') {
    agentStatuses['tectonic:boundaries'] = 'done';
    agentStatuses['tectonic:dynamics'] = 'running';
  } else if (phase === 'tectonic:dynamics') {
    agentStatuses['tectonic:dynamics'] = 'done';
    agentStatuses['tectonic:volcanism'] = 'running';
  } else if (phase === 'tectonic:volcanism') {
    agentStatuses['tectonic:volcanism'] = 'done';
  } else if (phase === 'surface') {
    // Mark all tectonic sub-phases as done
    agentStatuses['tectonic:advection'] = 'done';
    agentStatuses['tectonic:collision'] = 'done';
    agentStatuses['tectonic:boundaries'] = 'done';
    agentStatuses['tectonic:dynamics'] = 'done';
    agentStatuses['tectonic:volcanism'] = 'done';
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
    console.debug(`[agents] unknown phase: ${phase}`);
  }
  shell.updateAgentStatuses(agentStatuses);
  shell.setAdvancedProcessingStatus(agentStatuses);
}

function resetAgentStatuses(): void {
  for (const key of Object.keys(agentStatuses)) {
    agentStatuses[key] = 'idle';
  }
  shell.updateAgentStatuses(agentStatuses);
  shell.setAdvancedProcessingStatus(agentStatuses);
}

/** Update agent statuses based on last-tick timing stats so the panel shows which engines actually ran. */
function updateAgentStatusesFromStats(stats: api.TickStats | null): void {
  if (!stats) { resetAgentStatuses(); return; }
  agentStatuses['tectonic:advection']  = (stats.tectonicAdvectionMs ?? 0)  > 0 ? 'done' : 'idle';
  agentStatuses['tectonic:collision']  = (stats.tectonicCollisionMs ?? 0)  > 0 ? 'done' : 'idle';
  agentStatuses['tectonic:boundaries'] = (stats.tectonicBoundaryMs ?? 0)   > 0 ? 'done' : 'idle';
  agentStatuses['tectonic:dynamics']   = (stats.tectonicDynamicsMs ?? 0)   > 0 ? 'done' : 'idle';
  agentStatuses['tectonic:volcanism']  = (stats.tectonicVolcanismMs ?? 0)  > 0 ? 'done' : 'idle';
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
  tectonic:              '⛰ Tectonic\u2026',
  'tectonic:advection':  '⛰ Advection\u2026',
  'tectonic:collision':  '⛰ Collision\u2026',
  'tectonic:boundaries': '⛰ Boundaries\u2026',
  'tectonic:dynamics':   '⛰ Dynamics\u2026',
  'tectonic:volcanism':  '🌋 Volcanism\u2026',
  surface:               '🌊 Surface\u2026',
  biomatter:             '🌿 Biomatter\u2026',
  complete:              '',
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
  // S8: Handle incremental height-map updates pushed mid-tick.
  // This allows the globe to show terrain changes (tectonic collision, erosion)
  // before the full tick completes, giving visual feedback during long ticks.
  onIncrementalStateData: (event) => {
    if (!pendingSimRequest) return; // ignore stale mid-tick updates
    try {
      const heightMap = new Float32Array(event.heightMap);
      if (heightMap.length === GRID_SIZE * GRID_SIZE) {
        renderer.updateHeightMap(heightMap, GRID_SIZE);
      }
    } catch {
      // Non-critical: if the incremental update fails, the full bundle
      // will arrive at the end of the tick anyway.
    }
  },
});

// Connect with a short retry delay — the backend may not be ready immediately.
setTimeout(() => simSocket.connect(), 500);

// ── Simulation tick loop (completion-driven setTimeout, independent of rAF) ─
// Schedules the next advance after each request completes so poll rate tracks
// backend tick duration (lastTickStats.totalMs). setTimeout keeps sim running
// when the tab is hidden (rAF is paused by browsers for hidden tabs).

/** Wall-clock timestamp when the previous advance was dispatched. */
let lastSimDispatchMs = performance.now();

function simTick(): void {
  if (pendingSimRequest) return;

  if (paused) {
    scheduleSimTick(SIM_MIN_POLL_MS);
    return;
  }

  const nowMs = performance.now();
  const dtMs = nowMs - lastSimDispatchMs;
  lastSimDispatchMs = nowMs;

  const deltaMa = (dtMs / 1000) * simRate;
  if (deltaMa <= 0) {
    scheduleSimTick(SIM_MIN_POLL_MS);
    return;
  }

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
      const advanceWallMs = Math.round(performance.now() - simRequestStartMs);

      // P0-1: No new tick from this request — skip bundle + perf telemetry, but still
      // sync time/tick from the response (concurrent tick may have advanced state).
      if (result.skipped) {
        simTimeMa = result.timeMa;
        shell.setSimTime(simTimeMa);
        shell.setTimelineCursor(4500 + simTimeMa);
        if (result.tickCount !== undefined) {
          totalTickCount = result.tickCount;
        }
        shell.setProgressText('');
        resetAgentStatuses();
        return;
      }

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

      // Push tick stats into the advanced log view history for charting.
      if (lastTickStats) {
        shell.pushTickHistory(lastTickStats, totalTickCount);
      }

      // Refresh the log panel if it's open.
      if (shell.isLogPanelOpen) {
        refreshLogPanel();
      }

      // Fetch updated height+temperature+precipitation in a single request.
      const bundleStart = performance.now();
      const bundle = await api.getStateBundle(GRID_SIZE * GRID_SIZE);
      const bundleWallMs = Math.round(performance.now() - bundleStart);
      lastStateBundle = bundle;
      const heightMap = bundle.heightMap;
      renderer.updateHeightMap(heightMap, GRID_SIZE);

      // P1-3: Throttled auto-refresh — panel visible + pinned cell + interval elapsed.
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
      let overlayWallMs = 0;
      if (activeDataLayers.size > 0) {
        const overlayStart = performance.now();
        // Refresh the most-recently-activated overlay (the one currently visible).
        const layers = [...activeDataLayers];
        const layerToRefresh = layers[layers.length - 1];
        if (layerToRefresh !== undefined) {
          const rgba = await fetchLayerRgba(layerToRefresh);
          if (rgba !== null) {
            renderer.updateColorMap(rgba, GRID_SIZE);
          }
        }
        overlayWallMs = Math.round(performance.now() - overlayStart);
      }

      api.reportClientPerf('client_advance_cycle', {
        deltaMa,
        tickCount: totalTickCount,
        timeMa: simTimeMa,
        simRate,
        paused,
        advanceWallMs,
        bundleWallMs,
        overlayWallMs,
        totalClientWallMs: advanceWallMs + bundleWallMs + overlayWallMs,
        fps: lastMeasuredFps,
        activeLayerCount: activeDataLayers.size,
        stats: lastTickStats,
      });
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
      if (!paused) {
        scheduleSimTick();
      }
    });
}

// Start the completion-driven sim loop (independent of tab visibility).
scheduleSimTick(SIM_DEFAULT_POLL_MS);

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
    lastMeasuredFps = Math.round((frameCount / fpsAccum) * 1000);
    shell.setFps(lastMeasuredFps);
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
