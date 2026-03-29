// ─── GeoTime — Main Entry Point ─────────────────────────────────────────────

import './style.css';
import { TOTAL_BUFFER_SIZE, GRID_SIZE, createStateBufferLayout } from './shared/types';
import { EventBus } from './kernel/event-bus';
import { SimClock } from './kernel/sim-clock';
import { GlobeRenderer } from './render/globe-renderer';
import { PlanetGenerator } from './proc/planet-generator';
import { AppShell } from './ui/app-shell';
import type { StateBufferViews } from './shared/types';

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
const clock = new SimClock(bus);
const shell = new AppShell(appEl);

const viewportEl = shell.getViewportElement();
const renderer = new GlobeRenderer(viewportEl);

// ── Planet generation ───────────────────────────────────────────────────────

function randomSeed(): number {
  return (Math.random() * 0xffff_fffe + 1) >>> 0;
}

function generatePlanet(seed: number): void {
  // Re-allocate to clear previous state
  buffer = allocateBuffer();
  stateViews = createStateBufferLayout(buffer as SharedArrayBuffer);

  const generator = new PlanetGenerator(seed);
  generator.generate(stateViews);

  renderer.updateHeightMap(stateViews.heightMap, GRID_SIZE);
  renderer.updatePlateMap(stateViews.plateMap, GRID_SIZE);

  shell.setSeed(seed);
  shell.setTriangleCount(renderer.getTriangleCount());

  // Reset clock to initial geological time
  clock.seekTo(-4500);
  shell.setSimTime(clock.t);

  bus.emit('PLANET_GENERATED', { seed, timeMa: clock.t });
}

// Generate the first planet
generatePlanet(randomSeed());

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

function loop(now: number): void {
  requestAnimationFrame(loop);

  const dtMs = now - lastTime;
  lastTime = now;

  // Advance simulation (convert ms → seconds for the SimClock)
  clock.advance(dtMs / 1000);
  shell.setSimTime(clock.t);

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
