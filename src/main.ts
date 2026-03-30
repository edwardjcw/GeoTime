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
import type { LatLon } from './shared/types';

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
    const canvas = shell.getCrossSectionCanvas();
    const panelEl = canvas.parentElement;
    const w = panelEl ? panelEl.clientWidth : 960;
    renderCrossSection(lastCrossSectionProfile as never, {
      width: w,
      height: 280,
      showLabels: visible,
      showLegend: true,
    }, canvas);
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
