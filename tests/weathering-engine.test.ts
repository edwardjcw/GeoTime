import { WeatheringEngine } from '../src/geo/weathering-engine';
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
 * Set up terrain with varied climate zones for weathering tests.
 */
function setupWeatheringTerrain(
  views: StateBufferViews,
  stratigraphy: StratigraphyStack,
): void {
  for (let row = 0; row < GRID_SIZE; row++) {
    for (let col = 0; col < GRID_SIZE; col++) {
      const idx = row * GRID_SIZE + col;

      // Land above sea level
      views.heightMap[idx] = 500;
      views.crustThicknessMap[idx] = 35;

      // Latitude-dependent temperature: equator warm, poles cold
      const latFrac = Math.abs(row - GRID_SIZE / 2) / (GRID_SIZE / 2);
      views.temperatureMap[idx] = 30 - latFrac * 60;

      // Precipitation: moderate default
      views.precipitationMap[idx] = 800;

      // Wind: default moderate
      views.windUMap[idx] = 1;
      views.windVMap[idx] = 0;

      stratigraphy.initializeBasement(idx, false, -4500);
    }
  }
}

// ─── Tests ──────────────────────────────────────────────────────────────────

describe('WeatheringEngine', () => {
  let engine: WeatheringEngine;
  let stratigraphy: StratigraphyStack;
  let rng: Xoshiro256ss;

  beforeEach(() => {
    engine = new WeatheringEngine(GRID_SIZE);
    stratigraphy = new StratigraphyStack();
    rng = new Xoshiro256ss(42);
  });

  describe('getWeatheringProduct', () => {
    it('should produce laterite in tropical wet conditions', () => {
      const product = engine.getWeatheringProduct(RockType.IGN_GRANITE, 25, 1500);
      expect(product).toBe(RockType.SED_LATERITE);
    });

    it('should produce caliche in arid conditions', () => {
      const product = engine.getWeatheringProduct(RockType.IGN_GRANITE, 30, 100);
      expect(product).toBe(RockType.SED_CALICHE);
    });

    it('should produce regolith from carbonate dissolution', () => {
      const product = engine.getWeatheringProduct(RockType.SED_LIMESTONE, 15, 800);
      expect(product).toBe(RockType.SED_REGOLITH);
    });

    it('should produce regolith as default for non-tropical non-arid conditions', () => {
      const product = engine.getWeatheringProduct(RockType.IGN_GRANITE, 15, 600);
      expect(product).toBe(RockType.SED_REGOLITH);
    });

    it('should produce regolith from dolostone dissolution', () => {
      const product = engine.getWeatheringProduct(RockType.SED_DOLOSTONE, 15, 800);
      expect(product).toBe(RockType.SED_REGOLITH);
    });

    it('should produce regolith from chalk dissolution', () => {
      const product = engine.getWeatheringProduct(RockType.SED_CHALK, 15, 800);
      expect(product).toBe(RockType.SED_REGOLITH);
    });
  });

  describe('chemicalWeatheringRate', () => {
    it('should return 0 below minimum temperature', () => {
      const rate = engine.chemicalWeatheringRate(-15, 500);
      expect(rate).toBe(0);
    });

    it('should increase with temperature', () => {
      const coldRate = engine.chemicalWeatheringRate(5, 500);
      const warmRate = engine.chemicalWeatheringRate(30, 500);
      expect(warmRate).toBeGreaterThan(coldRate);
    });

    it('should increase with precipitation', () => {
      const dryRate = engine.chemicalWeatheringRate(20, 100);
      const wetRate = engine.chemicalWeatheringRate(20, 1500);
      expect(wetRate).toBeGreaterThan(dryRate);
    });

    it('should return a positive value for warm wet conditions', () => {
      const rate = engine.chemicalWeatheringRate(25, 1000);
      expect(rate).toBeGreaterThan(0);
    });
  });

  describe('tick', () => {
    it('should weather land cells', () => {
      const views = makeViews();
      setupWeatheringTerrain(views, stratigraphy);

      const result = engine.tick(-4000, 10, views, stratigraphy, rng);

      expect(result.chemicalWeathered).toBeGreaterThan(0);
      expect(result.cellsAffected).toBeGreaterThan(0);
    });

    it('should not weather underwater cells', () => {
      const views = makeViews();
      views.heightMap.fill(-1000); // all underwater
      views.temperatureMap.fill(20);
      views.precipitationMap.fill(1000);

      const result = engine.tick(-4000, 10, views, stratigraphy, rng);

      expect(result.chemicalWeathered).toBe(0);
      expect(result.cellsAffected).toBe(0);
    });

    it('should perform aeolian erosion in arid windy areas', () => {
      const views = makeViews();
      setupWeatheringTerrain(views, stratigraphy);

      // Create arid windy conditions
      for (let row = 0; row < GRID_SIZE; row++) {
        for (let col = 0; col < GRID_SIZE; col++) {
          const idx = row * GRID_SIZE + col;
          views.precipitationMap[idx] = 50;  // very dry
          views.windUMap[idx] = 5;           // strong wind
          views.windVMap[idx] = 0;
        }
      }

      const result = engine.tick(-4000, 10, views, stratigraphy, rng);

      expect(result.aeolianEroded).toBeGreaterThan(0);
    });

    it('should deposit loess downwind of aeolian erosion', () => {
      const views = makeViews();
      setupWeatheringTerrain(views, stratigraphy);

      // Arid + strong wind
      for (let row = 0; row < GRID_SIZE; row++) {
        for (let col = 0; col < GRID_SIZE; col++) {
          const idx = row * GRID_SIZE + col;
          views.precipitationMap[idx] = 50;
          views.windUMap[idx] = 5;
          views.windVMap[idx] = 0;
        }
      }

      const result = engine.tick(-4000, 10, views, stratigraphy, rng);

      expect(result.totalDeposited).toBeGreaterThan(0);

      // Check for loess layers
      let foundLoess = false;
      for (let i = 0; i < GRID_SIZE * GRID_SIZE; i += 100) {
        const layers = stratigraphy.getLayers(i);
        for (const layer of layers) {
          if (layer.rockType === RockType.SED_LOESS) {
            foundLoess = true;
            break;
          }
        }
        if (foundLoess) break;
      }
      expect(foundLoess).toBe(true);
    });

    it('should record weathering products in stratigraphy', () => {
      const views = makeViews();
      setupWeatheringTerrain(views, stratigraphy);

      engine.tick(-4000, 50, views, stratigraphy, rng);

      // Check for regolith, laterite, or caliche layers
      let foundWeathered = false;
      for (let i = 0; i < GRID_SIZE * GRID_SIZE; i += 100) {
        const layers = stratigraphy.getLayers(i);
        for (const layer of layers) {
          if (
            layer.rockType === RockType.SED_REGOLITH ||
            layer.rockType === RockType.SED_LATERITE ||
            layer.rockType === RockType.SED_CALICHE
          ) {
            foundWeathered = true;
            break;
          }
        }
        if (foundWeathered) break;
      }
      expect(foundWeathered).toBe(true);
    });

    it('should produce deterministic results from the same seed', () => {
      const views1 = makeViews();
      const views2 = makeViews();
      const strat1 = new StratigraphyStack();
      const strat2 = new StratigraphyStack();
      setupWeatheringTerrain(views1, strat1);
      setupWeatheringTerrain(views2, strat2);

      const e1 = new WeatheringEngine(GRID_SIZE);
      const e2 = new WeatheringEngine(GRID_SIZE);

      const r1 = e1.tick(-4000, 10, views1, strat1, new Xoshiro256ss(123));
      const r2 = e2.tick(-4000, 10, views2, strat2, new Xoshiro256ss(123));

      expect(r1.chemicalWeathered).toBeCloseTo(r2.chemicalWeathered);
      expect(r1.aeolianEroded).toBeCloseTo(r2.aeolianEroded);
      expect(r1.cellsAffected).toBe(r2.cellsAffected);
    });
  });
});
