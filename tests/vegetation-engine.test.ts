import { describe, it, expect, vi } from 'vitest';
import {
  VegetationEngine,
  computeNPP,
  nppToBiomassRate,
  computeFireProbability,
  computeVegetationAlbedo,
  MAX_BIOMASS,
  MIN_GRASS_PRECIP,
  MIN_GRASS_SOIL_DEPTH,
  BASE_FIRE_PROBABILITY,
  DRY_SEASON_PRECIP_THRESHOLD,
  FIRE_BIOMASS_THRESHOLD,
  FIRE_BURN_FRACTION,
  FOREST_ALBEDO_MODIFIER,
  GRASSLAND_ALBEDO_MODIFIER,
} from '../src/geo/vegetation-engine';
import type { StateBufferViews } from '../src/shared/types';
import {
  TOTAL_BUFFER_SIZE,
  createStateBufferLayout,
  GRID_SIZE,
} from '../src/shared/types';
import { EventBus } from '../src/kernel/event-bus';
import { EventLog } from '../src/kernel/event-log';

// ── Helpers ─────────────────────────────────────────────────────────────────

function makeViews(): StateBufferViews {
  const buf = new ArrayBuffer(TOTAL_BUFFER_SIZE) as unknown as SharedArrayBuffer;
  return createStateBufferLayout(buf);
}

function makeBus(): EventBus {
  return new EventBus();
}

function makeLog(): EventLog {
  return new EventLog();
}

/**
 * Set uniform terrain conditions across all cells.
 */
function setupUniformTerrain(
  views: StateBufferViews,
  height: number,
  temp: number,
  precip: number,
  soilDepth: number,
): void {
  const cellCount = GRID_SIZE * GRID_SIZE;
  for (let i = 0; i < cellCount; i++) {
    views.heightMap[i] = height;
    views.temperatureMap[i] = temp;
    views.precipitationMap[i] = precip;
    views.soilDepthMap[i] = soilDepth;
    views.biomassMap[i] = 0;
  }
}

// ── computeNPP ──────────────────────────────────────────────────────────────

describe('computeNPP', () => {
  it('should return 0 for zero precipitation', () => {
    expect(computeNPP(20, 0)).toBe(0);
  });

  it('should return 0 for negative precipitation', () => {
    expect(computeNPP(20, -100)).toBe(0);
  });

  it('should increase with temperature (all else equal)', () => {
    // Use high precipitation so temperature becomes the limiting factor
    const npp10 = computeNPP(10, 5000);
    const npp20 = computeNPP(20, 5000);
    const npp30 = computeNPP(30, 5000);
    expect(npp20).toBeGreaterThan(npp10);
    expect(npp30).toBeGreaterThan(npp20);
  });

  it('should increase with precipitation (all else equal)', () => {
    const npp200 = computeNPP(20, 200);
    const npp1000 = computeNPP(20, 1000);
    const npp3000 = computeNPP(20, 3000);
    expect(npp1000).toBeGreaterThan(npp200);
    expect(npp3000).toBeGreaterThan(npp1000);
  });

  it('should be bounded by 0-3000 range', () => {
    const npp = computeNPP(35, 5000);
    expect(npp).toBeGreaterThan(0);
    expect(npp).toBeLessThanOrEqual(3000);
  });

  it('should return low NPP for cold arid conditions', () => {
    const npp = computeNPP(-10, 100);
    expect(npp).toBeLessThan(200);
  });

  it('should return high NPP for tropical conditions', () => {
    const npp = computeNPP(25, 2000);
    expect(npp).toBeGreaterThan(1500);
  });
});

// ── nppToBiomassRate ────────────────────────────────────────────────────────

describe('nppToBiomassRate', () => {
  it('should return 0 for 0 NPP', () => {
    expect(nppToBiomassRate(0)).toBe(0);
  });

  it('should return positive rate for positive NPP', () => {
    expect(nppToBiomassRate(1000)).toBeGreaterThan(0);
  });

  it('should scale linearly with NPP', () => {
    const rate1 = nppToBiomassRate(500);
    const rate2 = nppToBiomassRate(1000);
    expect(rate2 / rate1).toBeCloseTo(2, 5);
  });
});

// ── computeFireProbability ──────────────────────────────────────────────────

describe('computeFireProbability', () => {
  it('should return 0 when biomass is below threshold', () => {
    expect(computeFireProbability(200, FIRE_BIOMASS_THRESHOLD - 1)).toBe(0);
  });

  it('should return non-zero when biomass is at threshold', () => {
    expect(computeFireProbability(200, FIRE_BIOMASS_THRESHOLD)).toBeGreaterThan(0);
  });

  it('should increase with lower precipitation (drier conditions)', () => {
    const probDry = computeFireProbability(100, 20);
    const probWet = computeFireProbability(1000, 20);
    expect(probDry).toBeGreaterThan(probWet);
  });

  it('should increase with higher biomass', () => {
    const probLow = computeFireProbability(200, FIRE_BIOMASS_THRESHOLD + 1);
    const probHigh = computeFireProbability(200, MAX_BIOMASS);
    expect(probHigh).toBeGreaterThan(probLow);
  });

  it('should not exceed 1', () => {
    const prob = computeFireProbability(0, MAX_BIOMASS);
    expect(prob).toBeLessThanOrEqual(1);
  });
});

// ── computeVegetationAlbedo ─────────────────────────────────────────────────

describe('computeVegetationAlbedo', () => {
  it('should return 0 for no biomass', () => {
    expect(computeVegetationAlbedo(0)).toBe(0);
  });

  it('should return negative (warming) for high biomass (forest)', () => {
    const albedo = computeVegetationAlbedo(MAX_BIOMASS);
    expect(albedo).toBeLessThan(0);
  });

  it('should return positive (cooling) for low biomass (grassland)', () => {
    const albedo = computeVegetationAlbedo(0.5);
    expect(albedo).toBeGreaterThan(0);
  });

  it('should transition from grassland to forest albedo', () => {
    const grassAlbedo = computeVegetationAlbedo(1);
    const forestAlbedo = computeVegetationAlbedo(30);
    expect(forestAlbedo).toBeLessThan(grassAlbedo);
  });
});

// ── VegetationEngine ────────────────────────────────────────────────────────

describe('VegetationEngine', () => {
  it('should construct with default config', () => {
    const engine = new VegetationEngine(makeBus(), makeLog(), 42);
    expect(engine.enabled).toBe(true);
  });

  it('should be disableable via config', () => {
    const engine = new VegetationEngine(makeBus(), makeLog(), 42, { enabled: false });
    expect(engine.enabled).toBe(false);
  });

  it('should return null before initialization', () => {
    const engine = new VegetationEngine(makeBus(), makeLog(), 42);
    expect(engine.tick(-4500, 1)).toBeNull();
  });

  it('should return null when disabled', () => {
    const engine = new VegetationEngine(makeBus(), makeLog(), 42, { enabled: false });
    const views = makeViews();
    engine.initialize(views);
    expect(engine.tick(-4500, 1)).toBeNull();
  });

  it('should return null for zero deltaMa', () => {
    const engine = new VegetationEngine(makeBus(), makeLog(), 42, { minTickInterval: 1 });
    const views = makeViews();
    engine.initialize(views);
    expect(engine.tick(-4500, 0)).toBeNull();
  });

  it('should process a vegetation tick on land cells', () => {
    const bus = makeBus();
    const log = makeLog();
    const engine = new VegetationEngine(bus, log, 42, {
      minTickInterval: 1,
      gridSize: 4,
    });
    const views = makeViews();

    // Set up a small area with favorable conditions
    for (let i = 0; i < 16; i++) {
      views.heightMap[i] = 500;       // above sea level
      views.temperatureMap[i] = 20;   // warm
      views.precipitationMap[i] = 1000; // wet
      views.soilDepthMap[i] = 1.0;    // deep soil
      views.biomassMap[i] = 0;
    }

    engine.initialize(views);
    const result = engine.tick(-4500, 1);

    expect(result).not.toBeNull();
    expect(result!.cellsWithVegetation).toBeGreaterThan(0);
    expect(result!.totalBiomass).toBeGreaterThan(0);
    expect(result!.meanNpp).toBeGreaterThan(0);
  });

  it('should not grow biomass underwater', () => {
    const engine = new VegetationEngine(makeBus(), makeLog(), 42, {
      minTickInterval: 1,
      gridSize: 4,
    });
    const views = makeViews();

    for (let i = 0; i < 16; i++) {
      views.heightMap[i] = -100;       // underwater
      views.temperatureMap[i] = 20;
      views.precipitationMap[i] = 1000;
      views.soilDepthMap[i] = 1.0;
      views.biomassMap[i] = 5;         // pre-existing biomass
    }

    engine.initialize(views);
    engine.tick(-4500, 1);

    // All biomass should be cleared
    for (let i = 0; i < 16; i++) {
      expect(views.biomassMap[i]).toBe(0);
    }
  });

  it('should clear biomass under glaciation', () => {
    const engine = new VegetationEngine(makeBus(), makeLog(), 42, {
      minTickInterval: 1,
      gridSize: 4,
    });
    const views = makeViews();

    for (let i = 0; i < 16; i++) {
      views.heightMap[i] = 500;
      views.temperatureMap[i] = -15;   // glacial conditions
      views.precipitationMap[i] = 1000;
      views.soilDepthMap[i] = 1.0;
      views.biomassMap[i] = 10;
    }

    engine.initialize(views);
    engine.tick(-4500, 1);

    for (let i = 0; i < 16; i++) {
      expect(views.biomassMap[i]).toBe(0);
    }
  });

  it('should clear biomass in deserts', () => {
    const engine = new VegetationEngine(makeBus(), makeLog(), 42, {
      minTickInterval: 1,
      gridSize: 4,
    });
    const views = makeViews();

    for (let i = 0; i < 16; i++) {
      views.heightMap[i] = 500;
      views.temperatureMap[i] = 30;
      views.precipitationMap[i] = 20;  // extreme desert
      views.soilDepthMap[i] = 1.0;
      views.biomassMap[i] = 10;
    }

    engine.initialize(views);
    engine.tick(-4500, 1);

    for (let i = 0; i < 16; i++) {
      expect(views.biomassMap[i]).toBe(0);
    }
  });

  it('should not grow biomass with insufficient soil', () => {
    const engine = new VegetationEngine(makeBus(), makeLog(), 42, {
      minTickInterval: 1,
      gridSize: 4,
    });
    const views = makeViews();

    for (let i = 0; i < 16; i++) {
      views.heightMap[i] = 500;
      views.temperatureMap[i] = 20;
      views.precipitationMap[i] = 1000;
      views.soilDepthMap[i] = 0.01;   // too thin
      views.biomassMap[i] = 0;
    }

    engine.initialize(views);
    const result = engine.tick(-4500, 1);

    expect(result).not.toBeNull();
    expect(result!.cellsWithVegetation).toBe(0);
  });

  it('should cap biomass at MAX_BIOMASS', () => {
    const engine = new VegetationEngine(makeBus(), makeLog(), 42, {
      minTickInterval: 1,
      gridSize: 4,
    });
    const views = makeViews();

    for (let i = 0; i < 16; i++) {
      views.heightMap[i] = 500;
      views.temperatureMap[i] = 25;
      views.precipitationMap[i] = 2000;
      views.soilDepthMap[i] = 2.0;
      views.biomassMap[i] = MAX_BIOMASS - 0.01;
    }

    engine.initialize(views);
    engine.tick(-4500, 100); // many Ma

    for (let i = 0; i < 16; i++) {
      expect(views.biomassMap[i]).toBeLessThanOrEqual(MAX_BIOMASS);
    }
  });

  it('should emit VEGETATION_UPDATE event', () => {
    const bus = makeBus();
    const log = makeLog();
    const engine = new VegetationEngine(bus, log, 42, {
      minTickInterval: 1,
      gridSize: 4,
    });
    const views = makeViews();

    for (let i = 0; i < 16; i++) {
      views.heightMap[i] = 500;
      views.temperatureMap[i] = 20;
      views.precipitationMap[i] = 1000;
      views.soilDepthMap[i] = 1.0;
    }

    engine.initialize(views);

    let emitted = false;
    bus.on('VEGETATION_UPDATE', (payload) => {
      emitted = true;
      expect(payload.cellsWithVegetation).toBeGreaterThan(0);
      expect(payload.totalBiomass).toBeGreaterThan(0);
      expect(payload.meanNpp).toBeGreaterThan(0);
    });

    engine.tick(-4500, 1);
    expect(emitted).toBe(true);
  });

  it('should emit FOREST_FIRE events stochastically', () => {
    const bus = makeBus();
    const log = makeLog();
    const engine = new VegetationEngine(bus, log, 42, {
      minTickInterval: 1,
      gridSize: 4,
    });
    const views = makeViews();

    // Set up dry conditions with high biomass for fire
    for (let i = 0; i < 16; i++) {
      views.heightMap[i] = 500;
      views.temperatureMap[i] = 25;
      views.precipitationMap[i] = 100;  // dry
      views.soilDepthMap[i] = 1.0;
      views.biomassMap[i] = MAX_BIOMASS; // full biomass
    }

    engine.initialize(views);

    let fireCount = 0;
    bus.on('FOREST_FIRE', () => { fireCount++; });

    // Run many ticks to trigger fires stochastically
    for (let t = 0; t < 50; t++) {
      engine.tick(-4500 + t, 1);
    }

    expect(fireCount).toBeGreaterThan(0);
  });

  it('should log fire events', () => {
    const bus = makeBus();
    const log = makeLog();
    const engine = new VegetationEngine(bus, log, 42, {
      minTickInterval: 1,
      gridSize: 4,
    });
    const views = makeViews();

    for (let i = 0; i < 16; i++) {
      views.heightMap[i] = 500;
      views.temperatureMap[i] = 25;
      views.precipitationMap[i] = 100;
      views.soilDepthMap[i] = 1.0;
      views.biomassMap[i] = MAX_BIOMASS;
    }

    engine.initialize(views);

    for (let t = 0; t < 50; t++) {
      engine.tick(-4500 + t, 1);
    }

    const fireEntries = log.getByType('FOREST_FIRE');
    expect(fireEntries.length).toBeGreaterThan(0);
  });

  it('should be deterministic with the same seed', () => {
    function runSim(seed: number): number {
      const engine = new VegetationEngine(makeBus(), makeLog(), seed, {
        minTickInterval: 1,
        gridSize: 4,
      });
      const views = makeViews();

      for (let i = 0; i < 16; i++) {
        views.heightMap[i] = 500;
        views.temperatureMap[i] = 20;
        views.precipitationMap[i] = 800;
        views.soilDepthMap[i] = 1.0;
        views.biomassMap[i] = 15;
      }

      engine.initialize(views);
      const result = engine.tick(-4500, 10);
      return result?.totalBiomass ?? 0;
    }

    const run1 = runSim(42);
    const run2 = runSim(42);
    expect(run1).toBe(run2);
  });

  it('should batch sub-ticks when deltaMa > minTickInterval', () => {
    const bus = makeBus();
    const engine = new VegetationEngine(bus, makeLog(), 42, {
      minTickInterval: 2,
      gridSize: 4,
    });
    const views = makeViews();

    for (let i = 0; i < 16; i++) {
      views.heightMap[i] = 500;
      views.temperatureMap[i] = 20;
      views.precipitationMap[i] = 1000;
      views.soilDepthMap[i] = 1.0;
    }

    engine.initialize(views);

    let emitCount = 0;
    bus.on('VEGETATION_UPDATE', () => { emitCount++; });

    // deltaMa = 5 with minTickInterval = 2 should produce 2 sub-ticks
    engine.tick(-4500, 5);
    expect(emitCount).toBe(2);
  });

  it('should not grow biomass when precip < MIN_GRASS_PRECIP', () => {
    const engine = new VegetationEngine(makeBus(), makeLog(), 42, {
      minTickInterval: 1,
      gridSize: 4,
    });
    const views = makeViews();

    for (let i = 0; i < 16; i++) {
      views.heightMap[i] = 500;
      views.temperatureMap[i] = 20;
      views.precipitationMap[i] = MIN_GRASS_PRECIP - 1; // just below threshold
      views.soilDepthMap[i] = 1.0;
      views.biomassMap[i] = 0;
    }

    engine.initialize(views);
    const result = engine.tick(-4500, 1);

    expect(result!.cellsWithVegetation).toBe(0);
  });
});
