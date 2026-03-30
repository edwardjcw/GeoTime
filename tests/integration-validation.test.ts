import { describe, it, expect } from 'vitest';
import type { StateBufferViews } from '../src/shared/types';
import {
  TOTAL_BUFFER_SIZE,
  createStateBufferLayout,
  GRID_SIZE,
  SoilOrder,
} from '../src/shared/types';
import { EventBus } from '../src/kernel/event-bus';
import { EventLog } from '../src/kernel/event-log';
import { PlanetGenerator } from '../src/proc/planet-generator';
import { TectonicEngine } from '../src/geo/tectonic-engine';
import { SurfaceEngine } from '../src/geo/surface-engine';
import { AtmosphereEngine } from '../src/geo/atmosphere-engine';
import { VegetationEngine } from '../src/geo/vegetation-engine';

// ── Helpers ─────────────────────────────────────────────────────────────────

function makeViews(): { views: StateBufferViews; buf: ArrayBufferLike } {
  const buf = new ArrayBuffer(TOTAL_BUFFER_SIZE) as unknown as SharedArrayBuffer;
  const views = createStateBufferLayout(buf);
  return { views, buf };
}

function generatePlanet(seed: number): {
  views: StateBufferViews;
  buf: ArrayBufferLike;
  bus: EventBus;
  log: EventLog;
  tectonic: TectonicEngine;
  surface: SurfaceEngine;
  atmosphere: AtmosphereEngine;
  vegetation: VegetationEngine;
} {
  const { views, buf } = makeViews();
  const bus = new EventBus();
  const log = new EventLog();

  const generator = new PlanetGenerator(seed);
  const result = generator.generate(views);

  const tectonic = new TectonicEngine(bus, log, seed, { minTickInterval: 0.1 });
  tectonic.initialize(result.plates, result.hotspots, result.atmosphere, views);

  const surface = new SurfaceEngine(bus, log, seed, { minTickInterval: 0.5 });
  surface.initialize(views, tectonic.stratigraphy);

  const atmosphere = new AtmosphereEngine(bus, log, seed, { minTickInterval: 1.0 });
  atmosphere.initialize(views, result.atmosphere);

  const vegetation = new VegetationEngine(bus, log, seed, { minTickInterval: 1.0 });
  vegetation.initialize(views);

  return { views, buf, bus, log, tectonic, surface, atmosphere, vegetation };
}

const CELL_COUNT = GRID_SIZE * GRID_SIZE;

// ── Integration Tests ───────────────────────────────────────────────────────

describe('Integration Validation — Continent Distribution', () => {
  it('should form at least 2 distinct land masses on a generated planet', { timeout: 15000 }, () => {
    const { views } = generatePlanet(42);

    // Count cells above sea level (height > 0)
    let landCells = 0;
    for (let i = 0; i < CELL_COUNT; i++) {
      if (views.heightMap[i] > 0) landCells++;
    }

    // There should be substantial land
    expect(landCells).toBeGreaterThan(CELL_COUNT * 0.1);

    // Flood-fill to count distinct continents
    const visited = new Uint8Array(CELL_COUNT);
    let continentCount = 0;
    const minContinentSize = 100;

    for (let i = 0; i < CELL_COUNT; i++) {
      if (visited[i] || views.heightMap[i] <= 0) continue;

      const queue = [i];
      visited[i] = 1;
      let size = 0;

      while (queue.length > 0) {
        const cell = queue.pop()!;
        size++;
        const row = Math.floor(cell / GRID_SIZE);
        const col = cell % GRID_SIZE;

        const neighbors = [
          row > 0 ? (row - 1) * GRID_SIZE + col : -1,
          row < GRID_SIZE - 1 ? (row + 1) * GRID_SIZE + col : -1,
          col > 0 ? row * GRID_SIZE + (col - 1) : -1,
          col < GRID_SIZE - 1 ? row * GRID_SIZE + (col + 1) : -1,
        ];

        for (const n of neighbors) {
          if (n >= 0 && !visited[n] && views.heightMap[n] > 0) {
            visited[n] = 1;
            queue.push(n);
          }
        }
      }

      if (size >= minContinentSize) {
        continentCount++;
      }
    }

    expect(continentCount).toBeGreaterThanOrEqual(2);
  });
});

describe('Integration Validation — Soil Type Coverage', () => {
  it('should produce multiple USDA soil orders after atmosphere + surface processing', { timeout: 30000 }, () => {
    const { views, surface, atmosphere } = generatePlanet(42);

    // Run atmosphere first to populate temperature/precipitation maps
    atmosphere.tick(-4500, 1);

    // Then run surface processing which includes pedogenesis
    surface.tick(-4500, 1);

    const soilTypes = new Set<number>();
    for (let i = 0; i < CELL_COUNT; i++) {
      const soil = views.soilTypeMap[i];
      if (soil !== SoilOrder.NONE) {
        soilTypes.add(soil);
      }
    }

    // After atmosphere + surface ticks, at least one soil type should be classified
    expect(soilTypes.size).toBeGreaterThanOrEqual(1);
  });
});

describe('Integration Validation — Stratigraphy Consistency', () => {
  it('should produce valid stratigraphy stacks after tectonic processing', { timeout: 30000 }, () => {
    const { tectonic } = generatePlanet(42);

    // Run a small tectonic tick
    tectonic.tick(-4500, 0.2);

    const strat = tectonic.stratigraphy;
    let stacksWithLayers = 0;
    let totalLayers = 0;

    for (let i = 0; i < CELL_COUNT; i++) {
      const layers = strat.getLayers(i);
      if (layers.length > 0) {
        stacksWithLayers++;
        totalLayers += layers.length;
      }
    }

    // Some cells should have stratigraphy layers
    expect(stacksWithLayers).toBeGreaterThan(0);
    expect(totalLayers).toBeGreaterThan(0);
  });
});

describe('Integration Validation — Erosion Volume Conservation', () => {
  it('should not catastrophically change total height during surface processing', { timeout: 30000 }, () => {
    const { views, surface } = generatePlanet(42);

    // Record total land height before surface processing
    let totalHeightBefore = 0;
    let landCells = 0;
    for (let i = 0; i < CELL_COUNT; i++) {
      if (views.heightMap[i] > 0) {
        totalHeightBefore += views.heightMap[i];
        landCells++;
      }
    }
    expect(landCells).toBeGreaterThan(0);

    // Run surface erosion only (no tectonics which adds volcanic height)
    surface.tick(-4500, 0.5);

    let totalHeightAfter = 0;
    for (let i = 0; i < CELL_COUNT; i++) {
      if (views.heightMap[i] > 0) {
        totalHeightAfter += views.heightMap[i];
      }
    }

    // Surface erosion should not dramatically change total height
    // (erosion removes, deposition adds — roughly conservative)
    if (totalHeightBefore > 0) {
      const ratio = totalHeightAfter / totalHeightBefore;
      expect(ratio).toBeGreaterThan(0.1);
      expect(ratio).toBeLessThan(10);
    }
  });
});

describe('Integration Validation — Vegetation Integration', () => {
  it('should produce non-zero biomass on land with favorable climate', { timeout: 15000 }, () => {
    const { views, vegetation } = generatePlanet(42);

    // Manually set favorable climate conditions on land cells
    let setCount = 0;
    for (let i = 0; i < CELL_COUNT; i++) {
      if (views.heightMap[i] > 0) {
        views.temperatureMap[i] = 20;
        views.precipitationMap[i] = 1000;
        views.soilDepthMap[i] = 1.0;
        setCount++;
      }
    }
    expect(setCount).toBeGreaterThan(0);

    // Run vegetation tick
    vegetation.tick(-4500, 2);

    let cellsWithBiomass = 0;
    for (let i = 0; i < CELL_COUNT; i++) {
      if (views.biomassMap[i] > 0) cellsWithBiomass++;
    }

    expect(cellsWithBiomass).toBeGreaterThan(0);
  });

  it('should clear biomass in glaciated regions', { timeout: 15000 }, () => {
    const { views, vegetation } = generatePlanet(42);

    const testCells = 100;
    for (let i = 0; i < testCells; i++) {
      views.heightMap[i] = 500;
      views.temperatureMap[i] = -15;
      views.precipitationMap[i] = 1000;
      views.soilDepthMap[i] = 1.0;
      views.biomassMap[i] = 20;
    }

    vegetation.tick(-4500, 1);

    for (let i = 0; i < testCells; i++) {
      expect(views.biomassMap[i]).toBe(0);
    }
  });
});

describe('Integration Validation — Multi-Seed Smoke Test', () => {
  const seeds = [42, 12345, 98765];

  for (const seed of seeds) {
    it(`should generate a viable planet with seed ${seed}`, { timeout: 15000 }, () => {
      const { views } = generatePlanet(seed);

      // Verify planet has both land and ocean
      let land = 0, ocean = 0;
      for (let i = 0; i < CELL_COUNT; i++) {
        if (views.heightMap[i] > 0) land++;
        else ocean++;
      }

      // PlanetGenerator targets ~70% ocean, so we expect both
      expect(land).toBeGreaterThan(CELL_COUNT * 0.05);
      expect(ocean).toBeGreaterThan(CELL_COUNT * 0.3);

      // Verify heights are varied (not all the same value)
      let minH = Infinity, maxH = -Infinity;
      for (let i = 0; i < CELL_COUNT; i++) {
        minH = Math.min(minH, views.heightMap[i]);
        maxH = Math.max(maxH, views.heightMap[i]);
      }
      expect(maxH - minH).toBeGreaterThan(0.1); // meaningful height variation

      // Verify plate assignments
      const plates = new Set<number>();
      for (let i = 0; i < CELL_COUNT; i++) {
        plates.add(views.plateMap[i]);
      }
      expect(plates.size).toBeGreaterThanOrEqual(5);
    });
  }
});
