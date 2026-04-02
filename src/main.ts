// ─── GeoTime — Main Entry Point ─────────────────────────────────────────────
// The frontend now delegates all simulation/calculation work to the C# .NET
// backend via REST API calls. The frontend only handles display (Three.js
// rendering, UI shell, cross-section Canvas 2D rendering).

import './style.css';
import { GRID_SIZE } from './shared/types';
import { GlobeRenderer } from './render/globe-renderer';
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

    // Fetch initial display data from backend
    const [heightData, plateData] = await Promise.all([
      api.getHeightMap(),
      api.getPlateMap(),
    ]);

    const heightMap = new Float32Array(heightData);
    const plateMap = new Uint16Array(plateData);

    renderer.updateHeightMap(heightMap, GRID_SIZE);
    renderer.updatePlateMap(plateMap, GRID_SIZE);

    shell.setSeed(result.seed);
    shell.setTriangleCount(renderer.getTriangleCount());
    shell.setSimTime(simTimeMa);
    shell.setTimelineCursor(4500 + simTimeMa);

    // Close any open cross-section panel on new planet
    shell.hideCrossSection();
    shell.setDrawMode(false);
    drawModeActive = false;
    drawPoints = [];

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
 * Fetch map data for a specific non-plate layer and convert to a displayable RGBA texture.
 * Returns `null` for layers whose texture update is handled internally (e.g. 'soil').
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
    for (let i = 0; i < cellCount; i++) {
      const [r, g, b] = topoColor(data[i]);
      rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = 200;
    }
  } else {
    // 'soil' and any unrecognised layer: use biome (Whittaker) colours via updateClimateMap.
    // The texture update is handled here; return null so the caller skips updateColorMap.
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
    if (layer === 'plates') {
      if (active) {
        const plateData = await api.getPlateMap();
        renderer.updatePlateMap(new Uint16Array(plateData), GRID_SIZE);
      }
      renderer.setPlateOverlayVisible(active);
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
  } catch (err) {
    console.error(`Failed to toggle layer ${layer}:`, err);
  }
});

// ── Globe click handling for cross-section draw mode ────────────────────────

shell.onInspectClick((x: number, y: number) => {
  if (!drawModeActive) return;

  const viewportEl = shell.getViewportElement();
  const latLon = renderer.screenToLatLon(
    x,
    y,
    viewportEl.clientWidth,
    viewportEl.clientHeight,
  );
  if (!latLon) return;

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
});

// ── Wire UI callbacks ───────────────────────────────────────────────────────

shell.onNewPlanet(() => {
  doGeneratePlanet(randomSeed());
});

shell.onPauseToggle(() => {
  paused = !paused;
  shell.setPaused(paused);
});

shell.onRateChange((rate) => {
  simRate = Math.max(0.001, Math.min(100, rate));
});

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

function loop(now: number): void {
  requestAnimationFrame(loop);

  const dtMs = now - lastTime;
  lastTime = now;

  // Accumulate time for backend simulation calls
  if (!paused) {
    simAccum += dtMs;
    const deltaMa = (simAccum / 1000) * simRate;

    // Send simulation advance to backend at throttled intervals
    if (simAccum >= SIM_UPDATE_INTERVAL && !pendingSimRequest && deltaMa > 0) {
      pendingSimRequest = true;
      simAccum = 0;

      api.advanceSimulation(deltaMa)
        .then(async (result) => {
          simTimeMa = result.timeMa;
          shell.setSimTime(simTimeMa);
          shell.setTimelineCursor(4500 + simTimeMa);

          // Fetch updated height map for rendering
          const heightData = await api.getHeightMap();
          const heightMap = new Float32Array(heightData);
          renderer.updateHeightMap(heightMap, GRID_SIZE);
        })
        .catch((err) => {
          console.error('Simulation advance failed:', err);
        })
        .finally(() => {
          pendingSimRequest = false;
        });
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
