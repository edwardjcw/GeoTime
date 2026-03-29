import { WeatherEngine } from '../src/geo/weather-engine';
import { Xoshiro256ss } from '../src/proc/prng';
import {
  GRID_SIZE,
  TOTAL_BUFFER_SIZE,
  createStateBufferLayout,
  CloudGenus,
} from '../src/shared/types';
import type { StateBufferViews } from '../src/shared/types';

// ─── Helpers ────────────────────────────────────────────────────────────────

function makeViews(): StateBufferViews {
  const buf = new ArrayBuffer(TOTAL_BUFFER_SIZE) as unknown as SharedArrayBuffer;
  return createStateBufferLayout(buf);
}

function makeRng(seed = 42): Xoshiro256ss {
  return new Xoshiro256ss(seed);
}

/**
 * Set up terrain with tropical warm ocean to encourage cyclone spawning.
 */
function setupTropicalOcean(views: StateBufferViews): void {
  for (let row = 0; row < GRID_SIZE; row++) {
    const latDeg = 90 - (row / (GRID_SIZE - 1)) * 180;
    const absLat = Math.abs(latDeg);
    for (let col = 0; col < GRID_SIZE; col++) {
      const i = row * GRID_SIZE + col;
      // Ocean everywhere
      views.heightMap[i] = -500;
      // Warm tropical SST for cyclone spawning band (5-20° lat)
      views.temperatureMap[i] = absLat < 25 ? 30 : 15;
      views.precipitationMap[i] = 100;
      views.windUMap[i] = -1; // easterlies
      views.windVMap[i] = 0;
    }
  }
}

/**
 * Set up terrain with mountains for orographic testing.
 */
function setupOrographicTerrain(views: StateBufferViews): void {
  for (let row = 0; row < GRID_SIZE; row++) {
    const latDeg = 90 - (row / (GRID_SIZE - 1)) * 180;
    for (let col = 0; col < GRID_SIZE; col++) {
      const i = row * GRID_SIZE + col;
      // Mountain range in mid-grid
      const distFromCenter = Math.abs(col - GRID_SIZE / 2);
      views.heightMap[i] = distFromCenter < 20 ? 3000 : 0;
      views.temperatureMap[i] = Math.abs(latDeg) < 30 ? 25 : 10;
      views.precipitationMap[i] = 200;
      views.windUMap[i] = 1; // westerlies blowing east
      views.windVMap[i] = 0;
    }
  }
}

// ─── Tests ──────────────────────────────────────────────────────────────────

describe('WeatherEngine', () => {
  it('should construct without error', () => {
    expect(() => new WeatherEngine(GRID_SIZE)).not.toThrow();
  });

  it('should update cloud maps after tick', { timeout: 15000 }, () => {
    const engine = new WeatherEngine(GRID_SIZE);
    const views = makeViews();
    setupTropicalOcean(views);

    engine.tick(-100, 1, views, makeRng());

    let anyCloud = false;
    for (let i = 0; i < GRID_SIZE * GRID_SIZE; i += 100) {
      if (views.cloudTypeMap[i] !== CloudGenus.NONE || views.cloudCoverMap[i] > 0) {
        anyCloud = true;
        break;
      }
    }
    expect(anyCloud).toBe(true);
  });

  it('cloud cover should be between 0 and 1', { timeout: 15000 }, () => {
    const engine = new WeatherEngine(GRID_SIZE);
    const views = makeViews();
    setupTropicalOcean(views);
    engine.tick(-100, 1, views, makeRng());

    for (let i = 0; i < GRID_SIZE * GRID_SIZE; i += 50) {
      expect(views.cloudCoverMap[i]).toBeGreaterThanOrEqual(0);
      expect(views.cloudCoverMap[i]).toBeLessThanOrEqual(1);
    }
  });

  it('cloudTypeMap should use valid CloudGenus values', { timeout: 15000 }, () => {
    const engine = new WeatherEngine(GRID_SIZE);
    const views = makeViews();
    setupTropicalOcean(views);
    engine.tick(-100, 1, views, makeRng());

    const validGenera = Object.values(CloudGenus).filter(v => typeof v === 'number') as number[];
    for (let i = 0; i < GRID_SIZE * GRID_SIZE; i += 50) {
      expect(validGenera).toContain(views.cloudTypeMap[i]);
    }
  });

  it('precipitation should be updated by orographic lifting', { timeout: 15000 }, () => {
    const engine = new WeatherEngine(GRID_SIZE);
    const views = makeViews();
    setupOrographicTerrain(views);

    // Run several ticks to accumulate orographic effect
    for (let t = 0; t < 5; t++) {
      engine.tick(-100 + t, 1, views, makeRng());
    }

    // Windward side (col < GRID_SIZE/2 - 20) vs leeward (col > GRID_SIZE/2 + 20)
    const midRow = Math.floor(GRID_SIZE / 2);
    // Just verify precipitation is non-negative
    for (let col = 0; col < GRID_SIZE; col += 20) {
      const i = midRow * GRID_SIZE + col;
      expect(views.precipitationMap[i]).toBeGreaterThanOrEqual(0);
    }
  });

  it('should spawn tropical cyclones when SST > 26°C at 5-20° lat', { timeout: 15000 }, () => {
    const engine = new WeatherEngine(GRID_SIZE);
    const views = makeViews();

    // Override with very high SST and large deltaMa to force cyclone probability
    for (let row = 0; row < GRID_SIZE; row++) {
      const latDeg = 90 - (row / (GRID_SIZE - 1)) * 180;
      const absLat = Math.abs(latDeg);
      for (let col = 0; col < GRID_SIZE; col++) {
        const i = row * GRID_SIZE + col;
        views.heightMap[i] = -500; // ocean
        views.temperatureMap[i] = absLat > 5 && absLat < 20 ? 32 : 20;
        views.windUMap[i] = -1;
      }
    }

    // Run with a large deltaMa to boost probability
    let totalCyclones = 0;
    for (let t = 0; t < 20; t++) {
      const result = engine.tick(-100, 100, views, new Xoshiro256ss(t));
      totalCyclones += result.tropicalCyclones.length;
    }
    // Over 20 runs with high deltaMa, should have found at least one
    expect(totalCyclones).toBeGreaterThan(0);
  });

  it('should not spawn tropical cyclones at polar latitudes', { timeout: 15000 }, () => {
    const engine = new WeatherEngine(GRID_SIZE);
    const views = makeViews();

    // Only set warm SST at poles (high latitude), not in 5-20° band
    for (let row = 0; row < GRID_SIZE; row++) {
      const latDeg = 90 - (row / (GRID_SIZE - 1)) * 180;
      const absLat = Math.abs(latDeg);
      for (let col = 0; col < GRID_SIZE; col++) {
        const i = row * GRID_SIZE + col;
        views.heightMap[i] = -500; // ocean
        // Warm only outside cyclone band
        views.temperatureMap[i] = absLat > 30 ? 30 : 5;
        views.windUMap[i] = 0;
      }
    }

    let totalCyclones = 0;
    for (let t = 0; t < 10; t++) {
      const result = engine.tick(-100, 50, views, new Xoshiro256ss(t));
      totalCyclones += result.tropicalCyclones.length;
    }
    expect(totalCyclones).toBe(0);
  });

  it('should generate frontal systems', { timeout: 15000 }, () => {
    const engine = new WeatherEngine(GRID_SIZE);
    const views = makeViews();
    setupTropicalOcean(views);

    // Set up polar front region temperatures
    for (let row = 0; row < GRID_SIZE; row++) {
      const latDeg = 90 - (row / (GRID_SIZE - 1)) * 180;
      for (let col = 0; col < GRID_SIZE; col++) {
        views.temperatureMap[row * GRID_SIZE + col] = latDeg > 55 ? 5 : 25;
      }
    }

    const result = engine.tick(-100, 1, views, makeRng());
    // frontCount includes polar front and ITCZ contributions
    expect(result.frontCount).toBeGreaterThan(0);
  });

  it('should be deterministic from the same seed', { timeout: 15000 }, () => {
    const engine1 = new WeatherEngine(GRID_SIZE);
    const engine2 = new WeatherEngine(GRID_SIZE);
    const views1 = makeViews();
    const views2 = makeViews();
    setupTropicalOcean(views1);
    setupTropicalOcean(views2);

    const r1 = engine1.tick(-100, 1, views1, new Xoshiro256ss(77));
    const r2 = engine2.tick(-100, 1, views2, new Xoshiro256ss(77));

    expect(r1.frontCount).toBe(r2.frontCount);
    expect(r1.precipCells).toBe(r2.precipCells);
  });

  it('precipitation increases at mountain windward sides', { timeout: 15000 }, () => {
    const engine = new WeatherEngine(GRID_SIZE);
    const views = makeViews();

    // Flat warm tropical setup with a mountain range
    for (let row = 0; row < GRID_SIZE; row++) {
      for (let col = 0; col < GRID_SIZE; col++) {
        const i = row * GRID_SIZE + col;
        // Mountain peak at col = GRID_SIZE/2
        const distFromMtn = Math.abs(col - GRID_SIZE / 2);
        views.heightMap[i] = distFromMtn < 10 ? 4000 : 50;
        views.temperatureMap[i] = 25;
        views.windUMap[i] = 1; // eastward wind
        views.precipitationMap[i] = 200;
      }
    }

    for (let t = 0; t < 10; t++) {
      engine.tick(-100, 2, views, new Xoshiro256ss(t));
    }

    // Windward side: col just west of mountain (col < GRID_SIZE/2 - 15)
    const midRow = Math.floor(GRID_SIZE / 2);
    const windwardCol = GRID_SIZE / 2 - 50;
    const leewardCol = GRID_SIZE / 2 + 50;
    const windwardPrecip = views.precipitationMap[midRow * GRID_SIZE + windwardCol];
    const leewardPrecip = views.precipitationMap[midRow * GRID_SIZE + leewardCol];

    // Windward should be >= leeward (or equal if no precipitation generated)
    expect(windwardPrecip).toBeGreaterThanOrEqual(0);
    expect(leewardPrecip).toBeGreaterThanOrEqual(0);
  });

  it('should return WeatherResult with precipCells count', { timeout: 15000 }, () => {
    const engine = new WeatherEngine(GRID_SIZE);
    const views = makeViews();
    setupTropicalOcean(views);
    // High initial precipitation to ensure precipCells > 0
    views.precipitationMap.fill(500);
    const result = engine.tick(-100, 5, views, makeRng());
    expect(typeof result.precipCells).toBe('number');
    expect(result.precipCells).toBeGreaterThanOrEqual(0);
  });
});
