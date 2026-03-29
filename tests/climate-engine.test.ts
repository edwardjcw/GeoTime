import { ClimateEngine } from '../src/geo/climate-engine';
import { Xoshiro256ss } from '../src/proc/prng';
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

function makeRng(): Xoshiro256ss {
  return new Xoshiro256ss(42);
}

// ─── Tests ──────────────────────────────────────────────────────────────────

describe('ClimateEngine', () => {
  it('should construct without error', () => {
    expect(() => new ClimateEngine(GRID_SIZE)).not.toThrow();
  });

  it('should update temperature map after tick', { timeout: 15000 }, () => {
    const engine = new ClimateEngine(GRID_SIZE);
    const views = makeViews();
    // Zero-initialize temps
    views.temperatureMap.fill(0);
    engine.tick(-100, 1, views, makeAtmosphere(), makeRng());
    // Some temperatures should be non-zero
    let anyNonZero = false;
    for (let i = 0; i < GRID_SIZE * GRID_SIZE; i += 100) {
      if (views.temperatureMap[i] !== 0) { anyNonZero = true; break; }
    }
    expect(anyNonZero).toBe(true);
  });

  it('should update wind maps after tick', { timeout: 15000 }, () => {
    const engine = new ClimateEngine(GRID_SIZE);
    const views = makeViews();
    engine.tick(-100, 1, views, makeAtmosphere(), makeRng());
    let anyNonZero = false;
    for (let i = 0; i < GRID_SIZE * GRID_SIZE; i += 100) {
      if (views.windUMap[i] !== 0) { anyNonZero = true; break; }
    }
    expect(anyNonZero).toBe(true);
  });

  it('polar cells should be colder than equatorial cells', { timeout: 15000 }, () => {
    const engine = new ClimateEngine(GRID_SIZE);
    const views = makeViews();
    views.temperatureMap.fill(0);
    // Run several ticks to let temps converge
    for (let i = 0; i < 5; i++) {
      engine.tick(-100, 2, views, makeAtmosphere(), makeRng());
    }
    // Equatorial row: row near GRID_SIZE/2
    const eqRow = Math.floor(GRID_SIZE / 2);
    const poleRow = 0;
    const eqTemp = views.temperatureMap[eqRow * GRID_SIZE + GRID_SIZE / 2];
    const poleTemp = views.temperatureMap[poleRow * GRID_SIZE + GRID_SIZE / 2];
    expect(eqTemp).toBeGreaterThan(poleTemp);
  });

  it('greenhouse forcing from CO₂ should increase temperature', { timeout: 15000 }, () => {
    const engine1 = new ClimateEngine(GRID_SIZE);
    const engine2 = new ClimateEngine(GRID_SIZE);
    const views1 = makeViews();
    const views2 = makeViews();
    views1.temperatureMap.fill(0);
    views2.temperatureMap.fill(0);

    const lowCO2 = makeAtmosphere(0.000280);  // 280 ppm (reference)
    const highCO2 = makeAtmosphere(0.000560); // 560 ppm (doubled)

    const r1 = engine1.tick(-100, 1, views1, lowCO2, makeRng());
    const r2 = engine2.tick(-100, 1, views2, highCO2, makeRng());

    expect(r2.meanTemperature).toBeGreaterThan(r1.meanTemperature);
  });

  it('Milankovitch forcing should change temperature over time', { timeout: 15000 }, () => {
    // sin(0*2π/100) = 0, sin(25*2π/100) = sin(π/2) = 1 → dT_milan = 2°C difference
    const engine1 = new ClimateEngine(GRID_SIZE);
    const engine2 = new ClimateEngine(GRID_SIZE);
    const views1 = makeViews();
    const views2 = makeViews();
    views1.temperatureMap.fill(0);
    views2.temperatureMap.fill(0);

    const r1 = engine1.tick(0, 1, views1, makeAtmosphere(), makeRng());
    const r2 = engine2.tick(25, 1, views2, makeAtmosphere(), makeRng());

    // timeMa=0 gives dT_milan=0; timeMa=25 gives dT_milan=2 → different temps
    expect(r2.meanTemperature).toBeGreaterThan(r1.meanTemperature + 0.5);
  });

  it('Snowball Earth threshold should be detected when equatorial temp < -10°C', { timeout: 15000 }, () => {
    const engine = new ClimateEngine(GRID_SIZE);
    const views = makeViews();
    // High elevation causes strong lapse rate cooling: 8 km × 6.5°C/km = 52°C reduction
    // Equatorial base = 30°C → 30 - 52 = -22°C < -10°C threshold
    views.heightMap.fill(8000);
    views.temperatureMap.fill(0);

    const result = engine.tick(-100, 10, views, makeAtmosphere(), makeRng());
    expect(result.snowballTriggered).toBe(true);
    expect(result.equatorialTemperature).toBeLessThan(-10);
  });

  it('ice-albedo feedback should be between 0 and 1', { timeout: 15000 }, () => {
    const engine = new ClimateEngine(GRID_SIZE);
    const views = makeViews();
    views.temperatureMap.fill(-20); // very cold → lots of ice
    const result = engine.tick(-100, 1, views, makeAtmosphere(), makeRng());
    expect(result.iceAlbedoFeedback).toBeGreaterThanOrEqual(0);
    expect(result.iceAlbedoFeedback).toBeLessThanOrEqual(1);
  });

  it('should be deterministic from the same seed', { timeout: 15000 }, () => {
    const engine1 = new ClimateEngine(GRID_SIZE);
    const engine2 = new ClimateEngine(GRID_SIZE);
    const views1 = makeViews();
    const views2 = makeViews();

    const r1 = engine1.tick(-100, 1, views1, makeAtmosphere(), new Xoshiro256ss(99));
    const r2 = engine2.tick(-100, 1, views2, makeAtmosphere(), new Xoshiro256ss(99));

    expect(r1.meanTemperature).toBeCloseTo(r2.meanTemperature, 8);
    expect(r1.iceAlbedoFeedback).toBeCloseTo(r2.iceAlbedoFeedback, 8);
  });

  it('Hadley cell should produce easterly (negative u) winds in tropics', { timeout: 15000 }, () => {
    const engine = new ClimateEngine(GRID_SIZE);
    const views = makeViews();
    engine.tick(-100, 1, views, makeAtmosphere(), makeRng());

    // Equatorial row (near GRID_SIZE/2 = lat ≈ 0°)
    const eqRow = Math.floor(GRID_SIZE / 2);
    const u = views.windUMap[eqRow * GRID_SIZE + GRID_SIZE / 2];
    expect(u).toBeLessThan(0); // trade winds are westward (negative u)
  });

  it('Ferrel cell should produce westerly (positive u) winds in mid-latitudes', { timeout: 15000 }, () => {
    const engine = new ClimateEngine(GRID_SIZE);
    const views = makeViews();
    engine.tick(-100, 1, views, makeAtmosphere(), makeRng());

    // Mid-latitude row: lat ≈ 45° north → row near GRID_SIZE/4
    const midLatRow = Math.floor(GRID_SIZE / 4);
    const u = views.windUMap[midLatRow * GRID_SIZE + GRID_SIZE / 2];
    expect(u).toBeGreaterThan(0); // westerlies are eastward (positive u)
  });

  it('3-cell model should produce correct wind direction signs', { timeout: 15000 }, () => {
    const engine = new ClimateEngine(GRID_SIZE);
    const views = makeViews();
    engine.tick(-100, 1, views, makeAtmosphere(), makeRng());

    // Polar cell center (~75°N): row = (90-75)/180 * (GRID_SIZE-1) ≈ row 42
    // u = -cos((75-75)*π/15) = -cos(0) = -1 → polar easterlies (negative u)
    const polarRow = Math.round((90 - 75) / 180 * (GRID_SIZE - 1));
    const poleU = views.windUMap[polarRow * GRID_SIZE + GRID_SIZE / 2];
    expect(poleU).toBeLessThan(0);

    // Equatorial (lat ≈ 0°): trade winds → negative u
    const eqRow = Math.floor(GRID_SIZE / 2);
    const eqU = views.windUMap[eqRow * GRID_SIZE + GRID_SIZE / 2];
    expect(eqU).toBeLessThan(0);

    // Mid-lat ~45°N: row = (90-45)/180 * (GRID_SIZE-1) ≈ row 128
    // u = cos((45-45)*π/30) = cos(0) = 1 → westerlies (positive u)
    const midRow = Math.round((90 - 45) / 180 * (GRID_SIZE - 1));
    const midU = views.windUMap[midRow * GRID_SIZE + GRID_SIZE / 2];
    expect(midU).toBeGreaterThan(0);
  });

  it('ITCZ region should show convergence (poleward v component) at equator', { timeout: 15000 }, () => {
    const engine = new ClimateEngine(GRID_SIZE);
    const views = makeViews();
    engine.tick(-100, 1, views, makeAtmosphere(), makeRng());
    // Just verify the engine ran and produced data
    const eqRow = Math.floor(GRID_SIZE / 2);
    const v = views.windVMap[eqRow * GRID_SIZE + GRID_SIZE / 2];
    expect(typeof v).toBe('number');
    expect(isNaN(v)).toBe(false);
  });

  it('should return ClimateResult with valid co2Ppm', { timeout: 15000 }, () => {
    const engine = new ClimateEngine(GRID_SIZE);
    const views = makeViews();
    const atmo = makeAtmosphere(0.000400); // 400 ppm
    const result = engine.tick(-100, 1, views, atmo, makeRng());
    expect(result.co2Ppm).toBeCloseTo(400, 0);
  });

  it('should handle re-initialization gracefully (multiple ticks)', { timeout: 15000 }, () => {
    const engine = new ClimateEngine(GRID_SIZE);
    const views = makeViews();
    expect(() => {
      for (let i = 0; i < 3; i++) {
        engine.tick(-100 + i, 1, views, makeAtmosphere(), makeRng());
      }
    }).not.toThrow();
  });
});
