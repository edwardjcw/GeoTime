// ─── GeoTime — Main Entry Point ─────────────────────────────────────────────

import './style.css';
import { TOTAL_BUFFER_SIZE, GRID_SIZE, createStateBufferLayout } from './shared/types';
import { EventBus } from './kernel/event-bus';
import { SimClock } from './kernel/sim-clock';
import { EventLog } from './kernel/event-log';
import { SnapshotManager } from './kernel/snapshot-manager';
import { GlobeRenderer } from './render/globe-renderer';
import { PlanetGenerator } from './proc/planet-generator';
import { TectonicEngine } from './geo/tectonic-engine';
import { SurfaceEngine } from './geo/surface-engine';
import { AtmosphereEngine } from './geo/atmosphere-engine';
import { CrossSectionEngine } from './geo/cross-section-engine';
import { VegetationEngine } from './geo/vegetation-engine';
import { renderCrossSection, exportCrossSectionPNG } from './render/cross-section-renderer';
import { AppShell } from './ui/app-shell';
import type { StateBufferViews, LatLon } from './shared/types';

// ── Bootstrap ───────────────────────────────────────────────────────────────

const appEl = document.getElementById('app');
if (!appEl) {
  throw new Error('Root element #app not found in the document.');
}

// ── Shared state buffer ─────────────────────────────────────────────────────

function allocateBuffer(): ArrayBufferLike {
  if (typeof SharedArrayBuffer !== 'undefined') {
    return new SharedArrayBuffer(TOTAL_BUFFER_SIZE);
  }
  return new ArrayBuffer(TOTAL_BUFFER_SIZE);
}

let buffer = allocateBuffer();
let stateViews: StateBufferViews = createStateBufferLayout(
  buffer as SharedArrayBuffer,
);

// ── Core systems ────────────────────────────────────────────────────────────

const bus = new EventBus();
const clock = new SimClock(bus, { maxFrameBudget: 0.05 });
const eventLog = new EventLog();
const snapshotManager = new SnapshotManager(10, 500);
const shell = new AppShell(appEl);

const viewportEl = shell.getViewportElement();
const renderer = new GlobeRenderer(viewportEl);

let tectonicEngine: TectonicEngine | null = null;
let surfaceEngine: SurfaceEngine | null = null;
let atmosphereEngine: AtmosphereEngine | null = null;
let vegetationEngine: VegetationEngine | null = null;

// Phase 5: Cross-Section Engine (persistent, re-initialised on planet generation)
const crossSectionEngine = new CrossSectionEngine(bus);

// ── Planet generation ───────────────────────────────────────────────────────

function randomSeed(): number {
  return (Math.random() * 0xffff_fffe + 1) >>> 0;
}

function generatePlanet(seed: number): void {
  // Re-allocate to clear previous state
  buffer = allocateBuffer();
  stateViews = createStateBufferLayout(buffer as SharedArrayBuffer);

  const generator = new PlanetGenerator(seed);
  const result = generator.generate(stateViews);

  // Initialize Phase 2 tectonic engine
  eventLog.clear();
  snapshotManager.clear();
  tectonicEngine = new TectonicEngine(bus, eventLog, seed, {
    minTickInterval: 0.1,
  });
  tectonicEngine.initialize(
    result.plates,
    result.hotspots,
    result.atmosphere,
    stateViews,
  );

  // Initialize Phase 3 surface engine
  surfaceEngine = new SurfaceEngine(bus, eventLog, seed, {
    minTickInterval: 0.5,
  });
  surfaceEngine.initialize(stateViews, tectonicEngine.stratigraphy);

  // Initialize Phase 4 atmosphere engine
  atmosphereEngine = new AtmosphereEngine(bus, eventLog, seed, {
    minTickInterval: 1.0,
  });
  atmosphereEngine.initialize(stateViews, result.atmosphere);

  // Initialize Phase 5 cross-section engine
  crossSectionEngine.clear();
  crossSectionEngine.initialize(stateViews, tectonicEngine.stratigraphy);

  // Initialize Phase 6 vegetation engine (feature-flagged)
  vegetationEngine = new VegetationEngine(bus, eventLog, seed, {
    minTickInterval: 1.0,
  });
  vegetationEngine.initialize(stateViews);

  // Take initial snapshot
  snapshotManager.takeSnapshot(-4500, buffer);

  renderer.updateHeightMap(stateViews.heightMap, GRID_SIZE);
  renderer.updatePlateMap(stateViews.plateMap, GRID_SIZE);

  shell.setSeed(seed);
  shell.setTriangleCount(renderer.getTriangleCount());

  // Reset clock to initial geological time
  clock.seekTo(-4500);
  shell.setSimTime(clock.t);

  // Close any open cross-section panel on new planet
  shell.hideCrossSection();
  shell.setDrawMode(false);
  drawModeActive = false;
  drawPoints = [];

  bus.emit('PLANET_GENERATED', { seed, timeMa: clock.t });

  // Encode seed in URL fragment for sharing
  window.location.hash = `seed=${seed}`;
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
generatePlanet(seedFromURL() ?? randomSeed());

// ── Cross-Section Draw Mode ─────────────────────────────────────────────────

let drawModeActive = false;
let drawPoints: LatLon[] = [];

shell.onDrawMode(() => {
  drawModeActive = !drawModeActive;
  shell.setDrawMode(drawModeActive);
  if (drawModeActive) {
    drawPoints = [];
  }
});

// Listen for cross-section ready events to render the profile
bus.on('CROSS_SECTION_READY', (payload) => {
  const canvas = shell.getCrossSectionCanvas();
  const panelEl = canvas.parentElement;
  const w = panelEl ? panelEl.clientWidth : 960;
  const h = 280;
  renderCrossSection(payload.profile, {
    width: w,
    height: h,
    showLabels: shell.areLabelsVisible(),
    showLegend: true,
  }, canvas);
  shell.showCrossSection();
});

shell.onLabelToggle((visible) => {
  bus.emit('LABEL_TOGGLE', { visible });
  // Re-render if a profile is active
  const profile = crossSectionEngine.getProfile();
  if (profile) {
    const canvas = shell.getCrossSectionCanvas();
    const panelEl = canvas.parentElement;
    const w = panelEl ? panelEl.clientWidth : 960;
    renderCrossSection(profile, {
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
  crossSectionEngine.clear();
  drawModeActive = false;
  shell.setDrawMode(false);
  drawPoints = [];
});

// ── Wire UI callbacks ───────────────────────────────────────────────────────

shell.onNewPlanet(() => {
  generatePlanet(randomSeed());
});

shell.onPauseToggle(() => {
  clock.togglePause();
  shell.setPaused(clock.paused);
});

shell.onRateChange((rate) => {
  clock.setRate(rate);
});

// ── Render loop ─────────────────────────────────────────────────────────────

let lastTime = performance.now();
let frameCount = 0;
let fpsAccum = 0;

/** Tectonic simulation accumulator — updates terrain less frequently than render. */
let tectonicAccum = 0;
const TECTONIC_UPDATE_INTERVAL = 100; // ms between tectonic updates

function loop(now: number): void {
  requestAnimationFrame(loop);

  const dtMs = now - lastTime;
  lastTime = now;

  // Advance simulation (convert ms → seconds for the SimClock)
  clock.advance(dtMs / 1000);
  shell.setSimTime(clock.t);

  // Run tectonic simulation at a throttled rate
  if (!clock.paused && tectonicEngine) {
    tectonicAccum += dtMs;
    if (tectonicAccum >= TECTONIC_UPDATE_INTERVAL) {
      const deltaMa = (tectonicAccum / 1000) * clock.rate;
      tectonicEngine.tick(clock.t, deltaMa);

      // Run Phase 3 surface processes after tectonics
      if (surfaceEngine) {
        surfaceEngine.tick(clock.t, deltaMa);
      }

      // Run Phase 4 atmosphere processes
      if (atmosphereEngine) {
        atmosphereEngine.tick(clock.t, deltaMa);
      }

      // Run Phase 6 vegetation processes
      if (vegetationEngine) {
        vegetationEngine.tick(clock.t, deltaMa);
      }

      tectonicAccum = 0;

      // Update GPU textures after tectonic + surface changes
      renderer.updateHeightMap(stateViews.heightMap, GRID_SIZE);

      // Take periodic snapshots
      snapshotManager.maybeTakeSnapshot(clock.t, buffer);
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
