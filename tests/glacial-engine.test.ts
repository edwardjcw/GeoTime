import { GlacialEngine } from '../src/geo/glacial-engine';
import { StratigraphyStack } from '../src/geo/stratigraphy';
import { Xoshiro256ss } from '../src/proc/prng';
import {
  GRID_SIZE,
  TOTAL_BUFFER_SIZE,
  createStateBufferLayout,
  RockType,
} from '../src/shared/types';
import type { StateBufferViews } from '../src/shared/types';

// ─── Helpers ────────────────────────────────────────────────────────────────

function makeViews(): StateBufferViews {
  const buf = new ArrayBuffer(TOTAL_BUFFER_SIZE) as unknown as SharedArrayBuffer;
  return createStateBufferLayout(buf);
}

/**
 * Set up a terrain with polar cold regions and high mountains for glaciation.
 */
function setupGlacialTerrain(views: StateBufferViews, stratigraphy: StratigraphyStack): void {
  for (let row = 0; row < GRID_SIZE; row++) {
    for (let col = 0; col < GRID_SIZE; col++) {
      const idx = row * GRID_SIZE + col;

      // Mountains at poles (rows near 0 and GRID_SIZE-1)
      const distFromPole = Math.min(row, GRID_SIZE - 1 - row) / GRID_SIZE;
      views.heightMap[idx] = distFromPole < 0.15 ? 4000 : 500;
      views.crustThicknessMap[idx] = 35;

      // Temperature: cold at poles, warm at equator
      views.temperatureMap[idx] = distFromPole < 0.15 ? -15 : 20;
      views.precipitationMap[idx] = 500;

      stratigraphy.initializeBasement(idx, false, -4500);
    }
  }
}

// ─── Tests ──────────────────────────────────────────────────────────────────

describe('GlacialEngine', () => {
  let engine: GlacialEngine;
  let stratigraphy: StratigraphyStack;
  let rng: Xoshiro256ss;

  beforeEach(() => {
    engine = new GlacialEngine(GRID_SIZE);
    stratigraphy = new StratigraphyStack();
    rng = new Xoshiro256ss(42);
  });

  describe('computeELA', () => {
    it('should return higher ELA in warmer conditions', () => {
      const views = makeViews();
      // All warm
      views.temperatureMap.fill(20);
      const warmEla = engine.computeELA(views.temperatureMap);

      // All cold
      views.temperatureMap.fill(-20);
      const coldEla = engine.computeELA(views.temperatureMap);

      expect(warmEla).toBeGreaterThan(coldEla);
    });

    it('should return ELA >= 0', () => {
      const views = makeViews();
      views.temperatureMap.fill(-50);
      const ela = engine.computeELA(views.temperatureMap);
      expect(ela).toBeGreaterThanOrEqual(0);
    });

    it('should compute ELA based on polar regions', () => {
      const views = makeViews();
      // Warm everywhere
      views.temperatureMap.fill(20);
      // But cold at poles
      const polarBand = Math.floor(GRID_SIZE * 0.15);
      for (let row = 0; row < polarBand; row++) {
        for (let col = 0; col < GRID_SIZE; col++) {
          views.temperatureMap[row * GRID_SIZE + col] = -25;
        }
      }
      for (let row = GRID_SIZE - polarBand; row < GRID_SIZE; row++) {
        for (let col = 0; col < GRID_SIZE; col++) {
          views.temperatureMap[row * GRID_SIZE + col] = -25;
        }
      }

      const ela = engine.computeELA(views.temperatureMap);
      // Should be affected by cold poles
      expect(ela).toBeLessThan(5000);
    });
  });

  describe('tick', () => {
    it('should glaciate cells in cold, high-altitude regions', () => {
      const views = makeViews();
      setupGlacialTerrain(views, stratigraphy);

      const result = engine.tick(-4000, 10, views, stratigraphy, rng);

      expect(result.glaciatedCells).toBeGreaterThan(0);
    });

    it('should not glaciate cells in warm regions', () => {
      const views = makeViews();
      // All warm, low terrain
      views.temperatureMap.fill(25);
      views.heightMap.fill(100);
      for (let i = 0; i < GRID_SIZE * GRID_SIZE; i += 1000) {
        stratigraphy.initializeBasement(i, false, -4500);
      }

      const result = engine.tick(-4000, 10, views, stratigraphy, rng);

      expect(result.glaciatedCells).toBe(0);
    });

    it('should erode under glaciers', () => {
      const views = makeViews();
      setupGlacialTerrain(views, stratigraphy);

      // Run multiple ticks to build up ice and start eroding
      let totalEroded = 0;
      for (let i = 0; i < 5; i++) {
        const result = engine.tick(-4000 + i * 10, 10, views, stratigraphy, rng);
        totalEroded += result.totalEroded;
      }

      expect(totalEroded).toBeGreaterThan(0);
    });

    it('should deposit moraines at glacier margins', () => {
      const views = makeViews();
      setupGlacialTerrain(views, stratigraphy);

      let totalDeposited = 0;
      for (let i = 0; i < 5; i++) {
        const result = engine.tick(-4000 + i * 10, 10, views, stratigraphy, rng);
        totalDeposited += result.totalDeposited;
      }

      expect(totalDeposited).toBeGreaterThan(0);
    });

    it('should record tillite layers in stratigraphy at moraine locations', () => {
      const views = makeViews();
      setupGlacialTerrain(views, stratigraphy);

      // Run several ticks
      for (let i = 0; i < 10; i++) {
        engine.tick(-4000 + i * 10, 10, views, stratigraphy, rng);
      }

      // Check for tillite deposits
      let foundTillite = false;
      for (let i = 0; i < GRID_SIZE * GRID_SIZE; i += 100) {
        const layers = stratigraphy.getLayers(i);
        for (const layer of layers) {
          if (layer.rockType === RockType.SED_TILLITE) {
            foundTillite = true;
            expect(layer.unconformity).toBe(true);
            break;
          }
        }
        if (foundTillite) break;
      }
      expect(foundTillite).toBe(true);
    });

    it('should compute equilibrium line altitude', () => {
      const views = makeViews();
      setupGlacialTerrain(views, stratigraphy);

      const result = engine.tick(-4000, 10, views, stratigraphy, rng);

      expect(Number.isFinite(result.equilibriumLineAltitude)).toBe(true);
      expect(result.equilibriumLineAltitude).toBeGreaterThanOrEqual(0);
    });

    it('should ablate ice when temperatures rise', () => {
      const views = makeViews();
      setupGlacialTerrain(views, stratigraphy);

      // Build up ice
      for (let i = 0; i < 5; i++) {
        engine.tick(-4000 + i * 10, 10, views, stratigraphy, rng);
      }

      const iceAfterAccumulation = engine.getIceThickness().slice();
      let totalIce1 = 0;
      for (let i = 0; i < iceAfterAccumulation.length; i++) totalIce1 += iceAfterAccumulation[i];

      // Now warm it up
      views.temperatureMap.fill(25);
      for (let i = 0; i < 5; i++) {
        engine.tick(-3950 + i * 10, 10, views, stratigraphy, rng);
      }

      let totalIce2 = 0;
      const iceFinal = engine.getIceThickness();
      for (let i = 0; i < iceFinal.length; i++) totalIce2 += iceFinal[i];

      expect(totalIce2).toBeLessThan(totalIce1);
    });

    it('should clear ice on reset', () => {
      const views = makeViews();
      setupGlacialTerrain(views, stratigraphy);
      engine.tick(-4000, 10, views, stratigraphy, rng);

      engine.clear();

      const ice = engine.getIceThickness();
      let total = 0;
      for (let i = 0; i < ice.length; i++) total += ice[i];
      expect(total).toBe(0);
    });
  });
});
