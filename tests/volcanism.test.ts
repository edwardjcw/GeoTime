import { VolcanismEngine, VolcanoType } from '../src/geo/volcanism';
import { BoundaryType, type BoundaryCell } from '../src/geo/boundary-classifier';
import { StratigraphyStack } from '../src/geo/stratigraphy';
import { Xoshiro256ss } from '../src/proc/prng';
import {
  GRID_SIZE,
  TOTAL_BUFFER_SIZE,
  createStateBufferLayout,
  RockType,
} from '../src/shared/types';
import type { PlateInfo, HotspotInfo, StateBufferViews } from '../src/shared/types';

function makeViews(): StateBufferViews {
  const buf = new ArrayBuffer(TOTAL_BUFFER_SIZE) as unknown as SharedArrayBuffer;
  return createStateBufferLayout(buf);
}

function makePlate(id: number, isOceanic: boolean): PlateInfo {
  return {
    id,
    centerLat: 0,
    centerLon: 0,
    angularVelocity: { lat: 0, lon: 0, rate: 1 },
    isOceanic,
    area: 0.1,
  };
}

function makeConvergentBoundary(plate1: number, plate2: number): BoundaryCell {
  return {
    cellIndex: (GRID_SIZE / 2) * GRID_SIZE + GRID_SIZE / 2,
    type: BoundaryType.CONVERGENT,
    plate1,
    plate2,
    relativeSpeed: 2,
  };
}

function makeDivergentBoundary(plate1: number, plate2: number): BoundaryCell {
  return {
    cellIndex: (GRID_SIZE / 2) * GRID_SIZE + GRID_SIZE / 4,
    type: BoundaryType.DIVERGENT,
    plate1,
    plate2,
    relativeSpeed: 1.5,
  };
}

describe('VolcanismEngine', () => {
  let engine: VolcanismEngine;
  let stratigraphy: StratigraphyStack;
  let rng: Xoshiro256ss;

  beforeEach(() => {
    engine = new VolcanismEngine();
    stratigraphy = new StratigraphyStack();
    rng = new Xoshiro256ss(42);
  });

  it('should produce eruptions at convergent oceanic-continental boundaries', () => {
    const views = makeViews();
    const plates = [
      makePlate(0, true),   // oceanic
      makePlate(1, false),  // continental
    ];

    // Create many convergent boundary cells to increase probability
    const boundaries: BoundaryCell[] = [];
    for (let i = 0; i < 100; i++) {
      boundaries.push({
        cellIndex: i * GRID_SIZE + GRID_SIZE / 2,
        type: BoundaryType.CONVERGENT,
        plate1: 0,
        plate2: 1,
        relativeSpeed: 2,
      });
    }

    // Use a large deltaMa to increase eruption probability
    const eruptions = engine.tick(
      -4000, 10, boundaries, [], plates, views, stratigraphy, rng,
    );

    // With 100 cells and high deltaMa, we should get at least some eruptions
    expect(eruptions.length).toBeGreaterThan(0);
    for (const e of eruptions) {
      expect(e.volcanoType).toBe(VolcanoType.STRATOVOLCANO);
      expect(e.intensity).toBeGreaterThan(0);
      expect(e.heightAdded).toBeGreaterThan(0);
      expect(e.co2Degassed).toBeGreaterThan(0);
    }
  });

  it('should produce eruptions at divergent oceanic boundaries (mid-ocean ridges)', () => {
    const views = makeViews();
    const plates = [
      makePlate(0, true),  // oceanic
      makePlate(1, true),  // oceanic
    ];

    const boundaries: BoundaryCell[] = [];
    for (let i = 0; i < 100; i++) {
      boundaries.push({
        cellIndex: i * GRID_SIZE + GRID_SIZE / 4,
        type: BoundaryType.DIVERGENT,
        plate1: 0,
        plate2: 1,
        relativeSpeed: 1.5,
      });
    }

    const eruptions = engine.tick(
      -4000, 10, boundaries, [], plates, views, stratigraphy, rng,
    );

    expect(eruptions.length).toBeGreaterThan(0);
    for (const e of eruptions) {
      expect(e.volcanoType).toBe(VolcanoType.SUBMARINE_RIDGE);
      expect(e.rockType).toBe(RockType.IGN_PILLOW_BASALT);
    }
  });

  it('should produce eruptions at hotspots', () => {
    const views = makeViews();
    const plates = [makePlate(0, false)];
    views.plateMap.fill(0);

    const hotspots: HotspotInfo[] = [
      { lat: 20, lon: -155, strength: 1.0 },
      { lat: -15, lon: 30, strength: 0.8 },
    ];

    // Run several ticks to increase probability
    let allEruptions: ReturnType<typeof engine.tick> = [];
    for (let i = 0; i < 10; i++) {
      const eruptions = engine.tick(
        -4000 + i, 1, [], hotspots, plates, views, stratigraphy, rng,
      );
      allEruptions.push(...eruptions);
    }

    expect(allEruptions.length).toBeGreaterThan(0);
    for (const e of allEruptions) {
      expect(e.rockType).toBe(RockType.IGN_BASALT);
    }
  });

  it('should add height to terrain at eruption sites', () => {
    const views = makeViews();
    const cellIndex = (GRID_SIZE / 2) * GRID_SIZE + GRID_SIZE / 2;
    views.heightMap[cellIndex] = 1000;

    const plates = [makePlate(0, true), makePlate(1, false)];
    const boundaries: BoundaryCell[] = [{
      cellIndex,
      type: BoundaryType.CONVERGENT,
      plate1: 0,
      plate2: 1,
      relativeSpeed: 3,
    }];

    // Keep ticking until we get an eruption at our target cell
    let eruptedAtTarget = false;
    for (let i = 0; i < 50 && !eruptedAtTarget; i++) {
      const eruptions = engine.tick(
        -4000 + i, 5, boundaries, [], plates, views, stratigraphy,
        new Xoshiro256ss(i),
      );
      eruptedAtTarget = eruptions.some((e) => e.cellIndex === cellIndex);
    }

    // Height should have increased
    expect(views.heightMap[cellIndex]).toBeGreaterThan(1000);
  });

  it('should add stratigraphic layers at eruption sites', () => {
    const views = makeViews();
    const plates = [makePlate(0, true), makePlate(1, false)];

    const cellIndex = 100 * GRID_SIZE + 50;
    const boundaries: BoundaryCell[] = [{
      cellIndex,
      type: BoundaryType.CONVERGENT,
      plate1: 0,
      plate2: 1,
      relativeSpeed: 2,
    }];

    // Tick repeatedly until we get an eruption
    let hadEruption = false;
    for (let i = 0; i < 50 && !hadEruption; i++) {
      const eruptions = engine.tick(
        -4000 + i, 5, boundaries, [], plates, views, stratigraphy,
        new Xoshiro256ss(i),
      );
      hadEruption = eruptions.some((e) => e.cellIndex === cellIndex);
    }

    if (hadEruption) {
      const layers = stratigraphy.getLayers(cellIndex);
      expect(layers.length).toBeGreaterThan(0);
    }
  });

  it('should compute total degassing from eruption records', () => {
    const eruptions = [
      {
        cellIndex: 0, volcanoType: VolcanoType.STRATOVOLCANO,
        lat: 0, lon: 0, intensity: 0.8, heightAdded: 100,
        rockType: RockType.IGN_ANDESITE, co2Degassed: 0.5, so2Degassed: 0.3,
      },
      {
        cellIndex: 1, volcanoType: VolcanoType.SHIELD,
        lat: 20, lon: -155, intensity: 0.6, heightAdded: 50,
        rockType: RockType.IGN_BASALT, co2Degassed: 0.2, so2Degassed: 0.1,
      },
    ];

    const { co2, so2 } = VolcanismEngine.totalDegassing(eruptions);
    expect(co2).toBeCloseTo(0.7);
    expect(so2).toBeCloseTo(0.4);
  });

  it('should produce deterministic results from the same seed', () => {
    const views1 = makeViews();
    const views2 = makeViews();
    const plates = [makePlate(0, true), makePlate(1, false)];
    const boundaries: BoundaryCell[] = [makeConvergentBoundary(0, 1)];
    const hotspots: HotspotInfo[] = [{ lat: 20, lon: -155, strength: 1.0 }];

    const engine1 = new VolcanismEngine();
    const engine2 = new VolcanismEngine();
    const strat1 = new StratigraphyStack();
    const strat2 = new StratigraphyStack();

    const e1 = engine1.tick(-4000, 5, boundaries, hotspots, plates, views1, strat1, new Xoshiro256ss(42));
    const e2 = engine2.tick(-4000, 5, boundaries, hotspots, plates, views2, strat2, new Xoshiro256ss(42));

    expect(e1.length).toBe(e2.length);
    for (let i = 0; i < e1.length; i++) {
      expect(e1[i].cellIndex).toBe(e2[i].cellIndex);
      expect(e1[i].volcanoType).toBe(e2[i].volcanoType);
      expect(e1[i].intensity).toBeCloseTo(e2[i].intensity);
    }
  });
});
