import { PedogenesisEngine } from '../src/geo/pedogenesis';
import { StratigraphyStack } from '../src/geo/stratigraphy';
import {
  GRID_SIZE,
  TOTAL_BUFFER_SIZE,
  createStateBufferLayout,
  RockType,
  SoilOrder,
} from '../src/shared/types';
import type { StateBufferViews } from '../src/shared/types';

// ─── Helpers ────────────────────────────────────────────────────────────────

function makeViews(): StateBufferViews {
  const buf = new ArrayBuffer(TOTAL_BUFFER_SIZE) as unknown as SharedArrayBuffer;
  return createStateBufferLayout(buf);
}

function setupLandTerrain(
  views: StateBufferViews,
  stratigraphy: StratigraphyStack,
): void {
  for (let row = 0; row < GRID_SIZE; row++) {
    for (let col = 0; col < GRID_SIZE; col++) {
      const idx = row * GRID_SIZE + col;
      views.heightMap[idx] = 500;
      views.crustThicknessMap[idx] = 35;
      views.temperatureMap[idx] = 20;
      views.precipitationMap[idx] = 800;
      views.soilTypeMap[idx] = SoilOrder.NONE;
      views.soilDepthMap[idx] = 0;

      stratigraphy.initializeBasement(idx, false, -4500);
    }
  }
}

// ─── Tests ──────────────────────────────────────────────────────────────────

describe('PedogenesisEngine', () => {
  let engine: PedogenesisEngine;
  let stratigraphy: StratigraphyStack;

  beforeEach(() => {
    engine = new PedogenesisEngine(GRID_SIZE);
    stratigraphy = new StratigraphyStack();
  });

  describe('classifySoil', () => {
    it('should classify GELISOL in permafrost conditions', () => {
      const order = engine.classifySoil(-5, 300, RockType.IGN_GRANITE, 1, 500);
      expect(order).toBe(SoilOrder.GELISOL);
    });

    it('should classify ANDISOL on volcanic parent material', () => {
      const order = engine.classifySoil(15, 800, RockType.IGN_BASALT, 1, 500);
      expect(order).toBe(SoilOrder.ANDISOL);
    });

    it('should classify ENTISOL for very shallow soil', () => {
      const order = engine.classifySoil(15, 800, RockType.IGN_GRANITE, 0.1, 500);
      expect(order).toBe(SoilOrder.ENTISOL);
    });

    it('should classify ARIDISOL in dry conditions', () => {
      const order = engine.classifySoil(25, 100, RockType.IGN_GRANITE, 1, 500);
      expect(order).toBe(SoilOrder.ARIDISOL);
    });

    it('should classify INCEPTISOL for slightly developed soil', () => {
      const order = engine.classifySoil(15, 500, RockType.IGN_GRANITE, 0.3, 500);
      expect(order).toBe(SoilOrder.INCEPTISOL);
    });

    it('should classify OXISOL in tropical extreme conditions', () => {
      const order = engine.classifySoil(25, 2000, RockType.IGN_GRANITE, 3, 500);
      expect(order).toBe(SoilOrder.OXISOL);
    });

    it('should classify ULTISOL in warm humid conditions', () => {
      const order = engine.classifySoil(18, 1200, RockType.IGN_GRANITE, 2, 500);
      expect(order).toBe(SoilOrder.ULTISOL);
    });

    it('should classify SPODOSOL in cool humid conditions', () => {
      const order = engine.classifySoil(5, 600, RockType.IGN_GRANITE, 1, 500);
      expect(order).toBe(SoilOrder.SPODOSOL);
    });

    it('should classify HISTOSOL in wet low-lying areas', () => {
      const order = engine.classifySoil(12, 1000, RockType.IGN_GRANITE, 1, 100);
      expect(order).toBe(SoilOrder.HISTOSOL);
    });

    it('should classify VERTISOL in clay-rich warm areas', () => {
      const order = engine.classifySoil(20, 700, RockType.SED_SHALE, 1, 500);
      expect(order).toBe(SoilOrder.VERTISOL);
    });

    it('should classify ALFISOL in deciduous forest climate', () => {
      const order = engine.classifySoil(12, 900, RockType.IGN_GRANITE, 1, 500);
      expect(order).toBe(SoilOrder.ALFISOL);
    });

    it('should classify MOLLISOL in temperate grasslands', () => {
      const order = engine.classifySoil(12, 500, RockType.IGN_GRANITE, 1, 500);
      expect(order).toBe(SoilOrder.MOLLISOL);
    });
  });

  describe('soilFormationRate', () => {
    it('should return 0 below minimum temperature', () => {
      const rate = engine.soilFormationRate(-25, 500, RockType.IGN_GRANITE);
      expect(rate).toBe(0);
    });

    it('should return 0 below minimum precipitation', () => {
      const rate = engine.soilFormationRate(20, 5, RockType.IGN_GRANITE);
      expect(rate).toBe(0);
    });

    it('should increase with temperature', () => {
      const coldRate = engine.soilFormationRate(5, 500, RockType.IGN_GRANITE);
      const warmRate = engine.soilFormationRate(25, 500, RockType.IGN_GRANITE);
      expect(warmRate).toBeGreaterThan(coldRate);
    });

    it('should increase with precipitation', () => {
      const dryRate = engine.soilFormationRate(20, 100, RockType.IGN_GRANITE);
      const wetRate = engine.soilFormationRate(20, 1500, RockType.IGN_GRANITE);
      expect(wetRate).toBeGreaterThan(dryRate);
    });

    it('should be slower for igneous rocks than sedimentary', () => {
      const igRate = engine.soilFormationRate(20, 800, RockType.IGN_GRANITE);
      const sedRate = engine.soilFormationRate(20, 800, RockType.SED_SANDSTONE);
      expect(igRate).toBeLessThan(sedRate);
    });

    it('should be slower for metamorphic rocks', () => {
      const metRate = engine.soilFormationRate(20, 800, RockType.MET_GNEISS);
      const sedRate = engine.soilFormationRate(20, 800, RockType.SED_SANDSTONE);
      expect(metRate).toBeLessThan(sedRate);
    });
  });

  describe('tick', () => {
    it('should form soil on land cells', () => {
      const views = makeViews();
      setupLandTerrain(views, stratigraphy);

      const result = engine.tick(-4000, 50, views, stratigraphy);

      expect(result.cellsFormed).toBeGreaterThan(0);
      expect(result.classifiedCells).toBeGreaterThan(0);
    });

    it('should not form soil underwater', () => {
      const views = makeViews();
      views.heightMap.fill(-1000);
      views.temperatureMap.fill(20);
      views.precipitationMap.fill(800);

      const result = engine.tick(-4000, 50, views, stratigraphy);

      expect(result.cellsFormed).toBe(0);
    });

    it('should increase soil depth over multiple ticks', () => {
      const views = makeViews();
      setupLandTerrain(views, stratigraphy);

      engine.tick(-4000, 100, views, stratigraphy);

      const midIdx = (GRID_SIZE / 2) * GRID_SIZE + GRID_SIZE / 2;
      const depth1 = views.soilDepthMap[midIdx];

      engine.tick(-3900, 100, views, stratigraphy);

      const depth2 = views.soilDepthMap[midIdx];
      expect(depth2).toBeGreaterThanOrEqual(depth1);
    });

    it('should cap soil depth at maximum', () => {
      const views = makeViews();
      setupLandTerrain(views, stratigraphy);

      // Run enough ticks to saturate soil depth (fewer iterations, larger deltaMa)
      for (let i = 0; i < 10; i++) {
        engine.tick(-4000 + i * 1000, 1000, views, stratigraphy);
      }

      // No cell should exceed MAX_SOIL_DEPTH (5m)
      for (let i = 0; i < GRID_SIZE * GRID_SIZE; i += 1000) {
        if (views.heightMap[i] > 0) {
          expect(views.soilDepthMap[i]).toBeLessThanOrEqual(5);
        }
      }
    });

    it('should assign soil type to soilTypeMap', () => {
      const views = makeViews();
      setupLandTerrain(views, stratigraphy);

      engine.tick(-4000, 50, views, stratigraphy);

      // Some cells should have a non-NONE soil type
      let foundSoil = false;
      for (let i = 0; i < GRID_SIZE * GRID_SIZE; i += 100) {
        if (views.soilTypeMap[i] !== SoilOrder.NONE) {
          foundSoil = true;
          break;
        }
      }
      expect(foundSoil).toBe(true);
    });

    it('should write soil horizon to top stratigraphic layer', () => {
      const views = makeViews();
      setupLandTerrain(views, stratigraphy);

      engine.tick(-4000, 50, views, stratigraphy);

      const midIdx = (GRID_SIZE / 2) * GRID_SIZE + GRID_SIZE / 2;
      const topLayer = stratigraphy.getTopLayer(midIdx);
      if (topLayer) {
        // Should have a non-NONE soil horizon after pedogenesis
        expect(topLayer.soilHorizon).not.toBe(SoilOrder.NONE);
      }
    });

    it('should classify different soil orders in different climate zones', () => {
      const views = makeViews();
      setupLandTerrain(views, stratigraphy);

      // Vary temperature across rows
      for (let row = 0; row < GRID_SIZE; row++) {
        const temp = 30 - (row / GRID_SIZE) * 60; // 30°C to -30°C
        for (let col = 0; col < GRID_SIZE; col++) {
          views.temperatureMap[row * GRID_SIZE + col] = temp;
        }
      }

      engine.tick(-4000, 50, views, stratigraphy);

      // Collect unique soil orders
      const soilOrders = new Set<number>();
      for (let i = 0; i < GRID_SIZE * GRID_SIZE; i += 100) {
        if (views.soilTypeMap[i] !== SoilOrder.NONE) {
          soilOrders.add(views.soilTypeMap[i]);
        }
      }

      // Should produce multiple distinct soil orders
      expect(soilOrders.size).toBeGreaterThan(1);
    });
  });
});
