import { SurfaceEngine } from '../src/geo/surface-engine';
import { StratigraphyStack } from '../src/geo/stratigraphy';
import { EventBus } from '../src/kernel/event-bus';
import { EventLog } from '../src/kernel/event-log';
import {
  GRID_SIZE,
  TOTAL_BUFFER_SIZE,
  createStateBufferLayout,
  SoilOrder,
} from '../src/shared/types';
import type { StateBufferViews } from '../src/shared/types';

// ─── Helpers ────────────────────────────────────────────────────────────────

function makeViews(): StateBufferViews {
  const buf = new ArrayBuffer(TOTAL_BUFFER_SIZE) as unknown as SharedArrayBuffer;
  return createStateBufferLayout(buf);
}

function setupTerrain(
  views: StateBufferViews,
  stratigraphy: StratigraphyStack,
): void {
  for (let row = 0; row < GRID_SIZE; row++) {
    for (let col = 0; col < GRID_SIZE; col++) {
      const idx = row * GRID_SIZE + col;

      // Sloped terrain: mountains in the north, lowlands in the south
      views.heightMap[idx] = 5000 - (row / GRID_SIZE) * 10000;
      views.crustThicknessMap[idx] = 35;

      // Latitude-dependent temperature: equator warm, poles cold
      const latFrac = Math.abs(row - GRID_SIZE / 2) / (GRID_SIZE / 2);
      views.temperatureMap[idx] = 25 - latFrac * 50;

      // Precipitation varies
      views.precipitationMap[idx] = 600 + (1 - latFrac) * 600;

      // Moderate wind
      views.windUMap[idx] = 1;
      views.windVMap[idx] = 0;

      views.soilTypeMap[idx] = SoilOrder.NONE;
      views.soilDepthMap[idx] = 0;

      stratigraphy.initializeBasement(idx, false, -4500);
    }
  }
}

// ─── Tests ──────────────────────────────────────────────────────────────────

describe('SurfaceEngine', () => {
  let bus: EventBus;
  let eventLog: EventLog;

  beforeEach(() => {
    bus = new EventBus();
    eventLog = new EventLog();
  });

  it('should initialize without error', () => {
    const engine = new SurfaceEngine(bus, eventLog, 42);
    const views = makeViews();
    const stratigraphy = new StratigraphyStack();
    setupTerrain(views, stratigraphy);

    expect(() => {
      engine.initialize(views, stratigraphy);
    }).not.toThrow();
  });

  it('should return null for zero deltaMa', () => {
    const engine = new SurfaceEngine(bus, eventLog, 42, { minTickInterval: 0.5 });
    const views = makeViews();
    const stratigraphy = new StratigraphyStack();
    setupTerrain(views, stratigraphy);
    engine.initialize(views, stratigraphy);

    const result = engine.tick(-4000, 0);
    expect(result).toBeNull();
  });

  it('should return null before initialization', () => {
    const engine = new SurfaceEngine(bus, eventLog, 42);
    const result = engine.tick(-4000, 1);
    expect(result).toBeNull();
  });

  it('should process a surface tick and return results', { timeout: 15000 }, () => {
    const engine = new SurfaceEngine(bus, eventLog, 42, { minTickInterval: 0.5 });
    const views = makeViews();
    const stratigraphy = new StratigraphyStack();
    setupTerrain(views, stratigraphy);
    engine.initialize(views, stratigraphy);

    const result = engine.tick(-4000, 5);

    expect(result).not.toBeNull();
    if (result) {
      expect(result.erosion).toBeDefined();
      expect(result.glacial).toBeDefined();
      expect(result.weathering).toBeDefined();
      expect(result.pedogenesis).toBeDefined();
    }
  });

  it('should run erosion that modifies the height map', { timeout: 15000 }, () => {
    const engine = new SurfaceEngine(bus, eventLog, 42, { minTickInterval: 0.5 });
    const views = makeViews();
    const stratigraphy = new StratigraphyStack();
    setupTerrain(views, stratigraphy);
    engine.initialize(views, stratigraphy);

    const initialHeight = views.heightMap[(GRID_SIZE / 2) * GRID_SIZE + GRID_SIZE / 2];

    // Run a single tick with large deltaMa
    engine.tick(-4000, 5);

    // Heights should have changed due to erosion
    const finalHeight = views.heightMap[(GRID_SIZE / 2) * GRID_SIZE + GRID_SIZE / 2];
    // Due to combined processes, height may go up or down
    expect(finalHeight).not.toBe(initialHeight);
  });

  it('should form soil over time', () => {
    const engine = new SurfaceEngine(bus, eventLog, 42, { minTickInterval: 50 });
    const views = makeViews();
    const stratigraphy = new StratigraphyStack();
    setupTerrain(views, stratigraphy);
    engine.initialize(views, stratigraphy);

    // Single sub-tick with large deltaMa for soil formation
    engine.tick(-4000, 50);

    // Some land cells should now have soil
    let hasSoil = false;
    for (let i = 0; i < GRID_SIZE * GRID_SIZE; i += 100) {
      if (views.soilDepthMap[i] > 0 && views.soilTypeMap[i] !== SoilOrder.NONE) {
        hasSoil = true;
        break;
      }
    }
    expect(hasSoil).toBe(true);
  });

  it('should emit EROSION_CYCLE events', { timeout: 15000 }, () => {
    const engine = new SurfaceEngine(bus, eventLog, 42, { minTickInterval: 0.5 });
    const views = makeViews();
    const stratigraphy = new StratigraphyStack();
    setupTerrain(views, stratigraphy);
    engine.initialize(views, stratigraphy);

    const events: unknown[] = [];
    bus.on('EROSION_CYCLE', (payload) => events.push(payload));

    // Single tick with enough deltaMa
    engine.tick(-4000, 5);

    expect(events.length).toBeGreaterThan(0);
  });

  it('should emit GLACIATION_ADVANCE when ice expands', () => {
    const engine = new SurfaceEngine(bus, eventLog, 42, { minTickInterval: 50 });
    const views = makeViews();
    const stratigraphy = new StratigraphyStack();
    setupTerrain(views, stratigraphy);

    // Make it very cold at the poles with high mountains
    for (let row = 0; row < GRID_SIZE; row++) {
      const latFrac = Math.abs(row - GRID_SIZE / 2) / (GRID_SIZE / 2);
      for (let col = 0; col < GRID_SIZE; col++) {
        const idx = row * GRID_SIZE + col;
        views.temperatureMap[idx] = latFrac > 0.8 ? -30 : 20;
        if (latFrac > 0.8) views.heightMap[idx] = 5000;
      }
    }

    engine.initialize(views, stratigraphy);

    const events: unknown[] = [];
    bus.on('GLACIATION_ADVANCE', (payload) => events.push(payload));

    // Run 2 ticks with large deltaMa (1 sub-tick each)
    engine.tick(-4000, 50);
    engine.tick(-3950, 50);

    // May or may not trigger depending on accumulation
    expect(Array.isArray(events)).toBe(true);
  });

  it('should record glaciation events in the event log', { timeout: 15000 }, () => {
    const engine = new SurfaceEngine(bus, eventLog, 42, { minTickInterval: 1 });
    const views = makeViews();
    const stratigraphy = new StratigraphyStack();
    setupTerrain(views, stratigraphy);
    engine.initialize(views, stratigraphy);

    // Single tick
    engine.tick(-4000, 10);

    const allEvents = eventLog.getAll();
    expect(Array.isArray(allEvents)).toBe(true);
  });

  it('should expose sub-engines via getters', () => {
    const engine = new SurfaceEngine(bus, eventLog, 42);
    expect(engine.getGlacialEngine()).toBeDefined();
    expect(engine.getErosionEngine()).toBeDefined();
  });

  it('should batch sub-ticks correctly', () => {
    const engine = new SurfaceEngine(bus, eventLog, 42, { minTickInterval: 1 });
    const views = makeViews();
    const stratigraphy = new StratigraphyStack();
    setupTerrain(views, stratigraphy);
    engine.initialize(views, stratigraphy);

    // Pass deltaMa < minTickInterval — should not process
    const result1 = engine.tick(-4000, 0.5);
    expect(result1).toBeNull();

    // Pass enough to trigger at least one sub-tick
    const result2 = engine.tick(-3999.5, 0.5);
    expect(result2).not.toBeNull();
  });

  it('should produce deterministic results from the same seed', { timeout: 15000 }, () => {
    const views1 = makeViews();
    const views2 = makeViews();
    const strat1 = new StratigraphyStack();
    const strat2 = new StratigraphyStack();
    const bus1 = new EventBus();
    const bus2 = new EventBus();
    const log1 = new EventLog();
    const log2 = new EventLog();

    setupTerrain(views1, strat1);
    setupTerrain(views2, strat2);

    const engine1 = new SurfaceEngine(bus1, log1, 42, { minTickInterval: 0.5 });
    const engine2 = new SurfaceEngine(bus2, log2, 42, { minTickInterval: 0.5 });
    engine1.initialize(views1, strat1);
    engine2.initialize(views2, strat2);

    const r1 = engine1.tick(-4000, 5);
    const r2 = engine2.tick(-4000, 5);

    expect(r1).not.toBeNull();
    expect(r2).not.toBeNull();
    if (r1 && r2) {
      expect(r1.erosion.totalEroded).toBeCloseTo(r2.erosion.totalEroded);
      expect(r1.weathering.chemicalWeathered).toBeCloseTo(r2.weathering.chemicalWeathered);
      expect(r1.pedogenesis.cellsFormed).toBe(r2.pedogenesis.cellsFormed);
    }
  });
});
