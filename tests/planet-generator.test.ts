import { PlanetGenerator } from '../src/proc/planet-generator';
import {
  GRID_SIZE,
  TOTAL_BUFFER_SIZE,
  createStateBufferLayout,
} from '../src/shared/types';
import type { StateBufferViews } from '../src/shared/types';

function makeViews(): StateBufferViews {
  const buf = new ArrayBuffer(TOTAL_BUFFER_SIZE) as unknown as SharedArrayBuffer;
  return createStateBufferLayout(buf);
}

describe('PlanetGenerator', () => {
  it('should generate deterministic results from the same seed', () => {
    const views1 = makeViews();
    const views2 = makeViews();
    const r1 = new PlanetGenerator(42).generate(views1);
    const r2 = new PlanetGenerator(42).generate(views2);

    expect(r1.plates.length).toBe(r2.plates.length);
    expect(r1.hotspots.length).toBe(r2.hotspots.length);
    expect(r1.atmosphere).toEqual(r2.atmosphere);

    const cellCount = GRID_SIZE * GRID_SIZE;
    for (let i = 0; i < cellCount; i += 1000) {
      expect(views1.heightMap[i]).toBe(views2.heightMap[i]);
      expect(views1.plateMap[i]).toBe(views2.plateMap[i]);
    }
  });

  it('should produce different results for different seeds', () => {
    const views1 = makeViews();
    const views2 = makeViews();
    const r1 = new PlanetGenerator(1).generate(views1);
    const r2 = new PlanetGenerator(2).generate(views2);

    let different = false;
    const cellCount = GRID_SIZE * GRID_SIZE;
    for (let i = 0; i < cellCount; i += 1000) {
      if (views1.heightMap[i] !== views2.heightMap[i]) {
        different = true;
        break;
      }
    }
    expect(different).toBe(true);
  });

  it('should populate heightMap with non-zero values', () => {
    const views = makeViews();
    new PlanetGenerator(42).generate(views);
    let hasNonZero = false;
    const cellCount = GRID_SIZE * GRID_SIZE;
    for (let i = 0; i < cellCount; i++) {
      if (views.heightMap[i] !== 0) {
        hasNonZero = true;
        break;
      }
    }
    expect(hasNonZero).toBe(true);
  });

  it('should have valid plate assignments (plateMap values > 0 exist)', () => {
    const views = makeViews();
    new PlanetGenerator(42).generate(views);
    let hasPositive = false;
    const cellCount = GRID_SIZE * GRID_SIZE;
    for (let i = 0; i < cellCount; i++) {
      if (views.plateMap[i] > 0) {
        hasPositive = true;
        break;
      }
    }
    expect(hasPositive).toBe(true);
  });

  it('should achieve approximately 70% ocean coverage (within 15% tolerance)', () => {
    const views = makeViews();
    new PlanetGenerator(42).generate(views);
    const cellCount = GRID_SIZE * GRID_SIZE;
    let oceanCount = 0;
    for (let i = 0; i < cellCount; i++) {
      if (views.heightMap[i] <= 0) oceanCount++;
    }
    const fraction = oceanCount / cellCount;
    expect(fraction).toBeGreaterThan(0.55);
    expect(fraction).toBeLessThan(0.85);
  });

  it('should set crust thickness to appropriate ranges (5-10km oceanic, 25-45km continental)', () => {
    const views = makeViews();
    const result = new PlanetGenerator(42).generate(views);
    const cellCount = GRID_SIZE * GRID_SIZE;

    for (let i = 0; i < cellCount; i++) {
      const t = views.crustThicknessMap[i];
      const plate = result.plates[views.plateMap[i]];
      if (plate.isOceanic) {
        expect(t).toBeGreaterThanOrEqual(5);
        expect(t).toBeLessThanOrEqual(10);
      } else {
        expect(t).toBeGreaterThanOrEqual(25);
        expect(t).toBeLessThanOrEqual(45);
      }
    }
    // At least one plate type should exist
    const hasOceanic = result.plates.some((p) => p.isOceanic);
    const hasContinental = result.plates.some((p) => !p.isOceanic);
    expect(hasOceanic || hasContinental).toBe(true);
  });

  it('should return plates array with 10-16 plates', () => {
    const views = makeViews();
    const result = new PlanetGenerator(42).generate(views);
    expect(result.plates.length).toBeGreaterThanOrEqual(10);
    expect(result.plates.length).toBeLessThanOrEqual(16);
  });

  it('should return 2-5 hotspots', () => {
    const views = makeViews();
    const result = new PlanetGenerator(42).generate(views);
    expect(result.hotspots.length).toBeGreaterThanOrEqual(2);
    expect(result.hotspots.length).toBeLessThanOrEqual(5);
  });
});
