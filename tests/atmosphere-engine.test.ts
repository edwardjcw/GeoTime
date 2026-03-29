import { AtmosphereEngine } from '../src/geo/atmosphere-engine';
import { EventBus } from '../src/kernel/event-bus';
import { EventLog } from '../src/kernel/event-log';
import {
  GRID_SIZE,
  TOTAL_BUFFER_SIZE,
  createStateBufferLayout,
} from '../src/shared/types';
import type { StateBufferViews, AtmosphericComposition } from '../src/shared/types';

// ─── Helpers ────────────────────────────────────────────────────────────────

function makeViews(): StateBufferViews {
  const buf = new ArrayBuffer(TOTAL_BUFFER_SIZE) as unknown as SharedArrayBuffer;
  return createStateBufferLayout(buf);
}

function makeAtmosphere(co2Fraction = 0.000280): AtmosphericComposition {
  return { n2: 0.78, o2: 0.21, co2: co2Fraction, h2o: 0.01 };
}

function setupBasicTerrain(views: StateBufferViews): void {
  for (let row = 0; row < GRID_SIZE; row++) {
    const latDeg = 90 - (row / (GRID_SIZE - 1)) * 180;
    for (let col = 0; col < GRID_SIZE; col++) {
      const i = row * GRID_SIZE + col;
      views.heightMap[i] = 200;
      views.temperatureMap[i] = 30 * Math.cos((latDeg * Math.PI) / 180);
      views.precipitationMap[i] = 500;
    }
  }
}

// ─── Tests ──────────────────────────────────────────────────────────────────

describe('AtmosphereEngine', () => {
  let bus: EventBus;
  let eventLog: EventLog;

  beforeEach(() => {
    bus = new EventBus();
    eventLog = new EventLog();
  });

  it('should construct without error', () => {
    expect(() => new AtmosphereEngine(bus, eventLog, 42)).not.toThrow();
  });

  it('should return null before initialization', () => {
    const engine = new AtmosphereEngine(bus, eventLog, 42);
    const result = engine.tick(-100, 1);
    expect(result).toBeNull();
  });

  it('should return null for zero deltaMa', () => {
    const engine = new AtmosphereEngine(bus, eventLog, 42, { minTickInterval: 1.0 });
    const views = makeViews();
    setupBasicTerrain(views);
    engine.initialize(views, makeAtmosphere());
    const result = engine.tick(-100, 0);
    expect(result).toBeNull();
  });

  it('should process a tick and return results', { timeout: 15000 }, () => {
    const engine = new AtmosphereEngine(bus, eventLog, 42, { minTickInterval: 1.0 });
    const views = makeViews();
    setupBasicTerrain(views);
    engine.initialize(views, makeAtmosphere());

    const result = engine.tick(-100, 5);
    expect(result).not.toBeNull();
    if (result) {
      expect(result.climate).toBeDefined();
      expect(result.weather).toBeDefined();
    }
  });

  it('should emit CLIMATE_UPDATE event', { timeout: 15000 }, () => {
    const engine = new AtmosphereEngine(bus, eventLog, 42, { minTickInterval: 1.0 });
    const views = makeViews();
    setupBasicTerrain(views);
    engine.initialize(views, makeAtmosphere());

    const events: unknown[] = [];
    bus.on('CLIMATE_UPDATE', (payload) => events.push(payload));

    engine.tick(-100, 5);
    expect(events.length).toBeGreaterThan(0);
  });

  it('should emit ICE_AGE_ONSET when temperature drops very low', { timeout: 15000 }, () => {
    const engine = new AtmosphereEngine(bus, eventLog, 42, { minTickInterval: 1.0 });
    const views = makeViews();

    // Set up very cold conditions
    for (let i = 0; i < GRID_SIZE * GRID_SIZE; i++) {
      views.heightMap[i] = 100;
      views.temperatureMap[i] = -50; // extremely cold
    }

    engine.initialize(views, makeAtmosphere(0.00001)); // near-zero CO2

    const events: unknown[] = [];
    bus.on('ICE_AGE_ONSET', (payload) => events.push(payload));

    engine.tick(-100, 5);
    expect(events.length).toBeGreaterThan(0);
  });

  it('sub-tick batching: should not process before minTickInterval', () => {
    const engine = new AtmosphereEngine(bus, eventLog, 42, { minTickInterval: 1.0 });
    const views = makeViews();
    setupBasicTerrain(views);
    engine.initialize(views, makeAtmosphere());

    // deltaMa < minTickInterval
    const result1 = engine.tick(-100, 0.5);
    expect(result1).toBeNull();

    // Enough to trigger
    const result2 = engine.tick(-99.5, 0.5);
    expect(result2).not.toBeNull();
  });

  it('should be deterministic from the same seed', { timeout: 15000 }, () => {
    const bus1 = new EventBus(); const log1 = new EventLog();
    const bus2 = new EventBus(); const log2 = new EventLog();

    const engine1 = new AtmosphereEngine(bus1, log1, 55, { minTickInterval: 1.0 });
    const engine2 = new AtmosphereEngine(bus2, log2, 55, { minTickInterval: 1.0 });

    const views1 = makeViews();
    const views2 = makeViews();
    setupBasicTerrain(views1);
    setupBasicTerrain(views2);

    engine1.initialize(views1, makeAtmosphere());
    engine2.initialize(views2, makeAtmosphere());

    const r1 = engine1.tick(-100, 5);
    const r2 = engine2.tick(-100, 5);

    expect(r1).not.toBeNull();
    expect(r2).not.toBeNull();
    if (r1 && r2) {
      expect(r1.climate.meanTemperature).toBeCloseTo(r2.climate.meanTemperature, 8);
      expect(r1.weather.frontCount).toBe(r2.weather.frontCount);
    }
  });

  it('temperature map should be updated after tick', { timeout: 15000 }, () => {
    const engine = new AtmosphereEngine(bus, eventLog, 42, { minTickInterval: 1.0 });
    const views = makeViews();
    views.temperatureMap.fill(0);
    setupBasicTerrain(views);
    engine.initialize(views, makeAtmosphere());

    engine.tick(-100, 5);

    let anyChanged = false;
    for (let i = 0; i < GRID_SIZE * GRID_SIZE; i += 100) {
      if (Math.abs(views.temperatureMap[i]) > 0.01) {
        anyChanged = true;
        break;
      }
    }
    expect(anyChanged).toBe(true);
  });

  it('getClimateEngine() should return a ClimateEngine', () => {
    const engine = new AtmosphereEngine(bus, eventLog, 42);
    const climateEngine = engine.getClimateEngine();
    expect(climateEngine).toBeDefined();
    expect(typeof climateEngine.tick).toBe('function');
  });

  it('getWeatherEngine() should return a WeatherEngine', () => {
    const engine = new AtmosphereEngine(bus, eventLog, 42);
    const weatherEngine = engine.getWeatherEngine();
    expect(weatherEngine).toBeDefined();
    expect(typeof weatherEngine.tick).toBe('function');
  });

  it('should handle re-initialization without error', { timeout: 15000 }, () => {
    const engine = new AtmosphereEngine(bus, eventLog, 42, { minTickInterval: 1.0 });
    const views1 = makeViews();
    const views2 = makeViews();
    setupBasicTerrain(views1);
    setupBasicTerrain(views2);

    engine.initialize(views1, makeAtmosphere());
    engine.tick(-100, 2);

    expect(() => {
      engine.initialize(views2, makeAtmosphere());
      engine.tick(-100, 2);
    }).not.toThrow();
  });
});
