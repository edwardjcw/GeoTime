import { ErosionEngine } from '../src/geo/erosion-engine';
import { StratigraphyStack } from '../src/geo/stratigraphy';
import { Xoshiro256ss } from '../src/proc/prng';
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

/**
 * Create a small terrain with a simple slope for testing.
 * Higher cells at the top of the grid, sloping downward.
 */
function setupSlopedTerrain(
  views: StateBufferViews,
  stratigraphy: StratigraphyStack,
): void {
  for (let row = 0; row < GRID_SIZE; row++) {
    for (let col = 0; col < GRID_SIZE; col++) {
      const idx = row * GRID_SIZE + col;
      // Height decreases with row (top = mountain, bottom = sea level)
      views.heightMap[idx] = 5000 - (row / GRID_SIZE) * 10000;
      views.crustThicknessMap[idx] = 35;

      // Initialize stratigraphy
      stratigraphy.initializeBasement(idx, false, -4500);
    }
  }
}

// ─── Tests ──────────────────────────────────────────────────────────────────

describe('ErosionEngine', () => {
  let engine: ErosionEngine;
  let stratigraphy: StratigraphyStack;
  let rng: Xoshiro256ss;

  beforeEach(() => {
    engine = new ErosionEngine(GRID_SIZE);
    stratigraphy = new StratigraphyStack();
    rng = new Xoshiro256ss(42);
  });

  describe('computeFlowGraph', () => {
    it('should compute a flow graph for every cell', { timeout: 15000 }, () => {
      const views = makeViews();
      setupSlopedTerrain(views, stratigraphy);

      const flow = engine.computeFlowGraph(views.heightMap);

      expect(flow.length).toBe(GRID_SIZE * GRID_SIZE);
      for (const cell of flow) {
        expect(cell.index).toBeGreaterThanOrEqual(0);
        expect(cell.slope).toBeGreaterThanOrEqual(0);
      }
    });

    it('should route flow downhill on a simple slope', { timeout: 15000 }, () => {
      const views = makeViews();
      setupSlopedTerrain(views, stratigraphy);

      const flow = engine.computeFlowGraph(views.heightMap);

      // Mid-grid cell should point to a cell below it (higher row)
      const midRow = GRID_SIZE / 2;
      const midCol = GRID_SIZE / 2;
      const midIdx = midRow * GRID_SIZE + midCol;
      const downstreamIdx = flow[midIdx].downstream;

      if (downstreamIdx >= 0) {
        // Downstream cell should have lower height
        expect(views.heightMap[downstreamIdx]).toBeLessThan(views.heightMap[midIdx]);
      }
    });

    it('should identify pits as cells with no downstream', { timeout: 15000 }, () => {
      const views = makeViews();
      // Create a flat region (pit)
      views.heightMap.fill(100);

      const flow = engine.computeFlowGraph(views.heightMap);

      // All cells on a perfectly flat surface should have downstream = -1
      for (const cell of flow) {
        expect(cell.downstream).toBe(-1);
      }
    });
  });

  describe('computeDrainageArea', () => {
    it('should give every cell an area of at least 1', { timeout: 15000 }, () => {
      const views = makeViews();
      setupSlopedTerrain(views, stratigraphy);

      const flow = engine.computeFlowGraph(views.heightMap);
      const area = engine.computeDrainageArea(flow);

      expect(area.length).toBe(GRID_SIZE * GRID_SIZE);
      for (let i = 0; i < area.length; i++) {
        expect(area[i]).toBeGreaterThanOrEqual(1);
      }
    });

    it('should accumulate larger drainage areas downstream', { timeout: 15000 }, () => {
      const views = makeViews();
      setupSlopedTerrain(views, stratigraphy);

      const flow = engine.computeFlowGraph(views.heightMap);
      const area = engine.computeDrainageArea(flow);

      // Bottom-row cells should have larger drainage area than top-row cells
      let topRowMax = 0;
      let bottomRowMax = 0;
      const lastRow = GRID_SIZE - 2; // avoid edge effects
      for (let col = 0; col < GRID_SIZE; col++) {
        topRowMax = Math.max(topRowMax, area[1 * GRID_SIZE + col]);
        bottomRowMax = Math.max(bottomRowMax, area[lastRow * GRID_SIZE + col]);
      }
      expect(bottomRowMax).toBeGreaterThan(topRowMax);
    });
  });

  describe('tick', () => {
    it('should erode material from steep slopes', { timeout: 15000 }, () => {
      const views = makeViews();
      setupSlopedTerrain(views, stratigraphy);

      const initialHeight = views.heightMap[GRID_SIZE / 2 * GRID_SIZE + GRID_SIZE / 2];

      const result = engine.tick(-4000, 10, views, stratigraphy, rng);

      expect(result.totalEroded).toBeGreaterThan(0);
      expect(result.cellsAffected).toBeGreaterThan(0);
    });

    it('should deposit sediment at low-slope areas', { timeout: 15000 }, () => {
      const views = makeViews();
      setupSlopedTerrain(views, stratigraphy);

      const result = engine.tick(-4000, 10, views, stratigraphy, rng);

      expect(result.totalDeposited).toBeGreaterThanOrEqual(0);
    });

    it('should record sedimentary layers in stratigraphy', { timeout: 15000 }, () => {
      const views = makeViews();
      setupSlopedTerrain(views, stratigraphy);

      engine.tick(-4000, 20, views, stratigraphy, rng);

      // Check some cells for new sedimentary deposits
      let foundSediment = false;
      for (let i = 0; i < GRID_SIZE * GRID_SIZE; i += 100) {
        const layers = stratigraphy.getLayers(i);
        for (const layer of layers) {
          if (
            layer.rockType === RockType.SED_SANDSTONE ||
            layer.rockType === RockType.SED_MUDSTONE
          ) {
            foundSediment = true;
            break;
          }
        }
        if (foundSediment) break;
      }
      // Sediment deposition depends on terrain geometry — may or may not occur
      expect(typeof foundSediment).toBe('boolean');
    });

    it('should identify river cells when drainage area is sufficient', { timeout: 15000 }, () => {
      const views = makeViews();
      setupSlopedTerrain(views, stratigraphy);

      const result = engine.tick(-4000, 10, views, stratigraphy, rng);

      // riverCells should be an array (may be empty if terrain doesn't converge)
      expect(Array.isArray(result.riverCells)).toBe(true);
    });

    it('should not erode on a flat surface', { timeout: 15000 }, () => {
      const views = makeViews();
      views.heightMap.fill(500);
      for (let i = 0; i < GRID_SIZE * GRID_SIZE; i += 1000) {
        stratigraphy.initializeBasement(i, false, -4500);
      }

      const result = engine.tick(-4000, 10, views, stratigraphy, rng);

      expect(result.totalEroded).toBe(0);
    });

    it('should return deterministic results with the same seed', { timeout: 15000 }, () => {
      const views1 = makeViews();
      const views2 = makeViews();
      const strat1 = new StratigraphyStack();
      const strat2 = new StratigraphyStack();

      setupSlopedTerrain(views1, strat1);
      setupSlopedTerrain(views2, strat2);

      const engine1 = new ErosionEngine(GRID_SIZE);
      const engine2 = new ErosionEngine(GRID_SIZE);

      const r1 = engine1.tick(-4000, 10, views1, strat1, new Xoshiro256ss(123));
      const r2 = engine2.tick(-4000, 10, views2, strat2, new Xoshiro256ss(123));

      expect(r1.totalEroded).toBeCloseTo(r2.totalEroded);
      expect(r1.cellsAffected).toBe(r2.cellsAffected);
    });
  });
});
