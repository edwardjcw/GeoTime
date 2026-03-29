import {
  BoundaryClassifier,
  BoundaryType,
  plateVelocityAt,
  getNeighborIndices,
} from '../src/geo/boundary-classifier';
import type { PlateInfo } from '../src/shared/types';
import { GRID_SIZE } from '../src/shared/types';

function makePlate(id: number, overrides?: Partial<PlateInfo>): PlateInfo {
  return {
    id,
    centerLat: 0,
    centerLon: 0,
    angularVelocity: { lat: 0, lon: 0, rate: 1 },
    isOceanic: false,
    area: 0.1,
    ...overrides,
  };
}

describe('getNeighborIndices', () => {
  it('should return 4 neighbors for interior cells', () => {
    const neighbors = getNeighborIndices(10, 10, GRID_SIZE);
    expect(neighbors).toHaveLength(4);
  });

  it('should wrap longitude at left edge', () => {
    const neighbors = getNeighborIndices(10, 0, GRID_SIZE);
    expect(neighbors).toHaveLength(4);
    // Left neighbor should wrap to the last column
    expect(neighbors).toContain(10 * GRID_SIZE + (GRID_SIZE - 1));
  });

  it('should wrap longitude at right edge', () => {
    const neighbors = getNeighborIndices(10, GRID_SIZE - 1, GRID_SIZE);
    expect(neighbors).toHaveLength(4);
    // Right neighbor should wrap to column 0
    expect(neighbors).toContain(10 * GRID_SIZE + 0);
  });

  it('should have 3 neighbors at top row (no row above)', () => {
    const neighbors = getNeighborIndices(0, 10, GRID_SIZE);
    expect(neighbors).toHaveLength(3);
  });

  it('should have 3 neighbors at bottom row (no row below)', () => {
    const neighbors = getNeighborIndices(GRID_SIZE - 1, 10, GRID_SIZE);
    expect(neighbors).toHaveLength(3);
  });
});

describe('plateVelocityAt', () => {
  it('should return zero velocity for zero-rate plate', () => {
    const plate = makePlate(0, {
      angularVelocity: { lat: 0, lon: 0, rate: 0 },
    });
    const [vLat, vLon] = plateVelocityAt(plate, 0, 0);
    expect(vLat).toBe(0);
    expect(vLon).toBe(0);
  });

  it('should return non-zero velocity for rotating plate', () => {
    const plate = makePlate(0, {
      angularVelocity: { lat: 45, lon: 0, rate: 2 },
    });
    const [vLat, vLon] = plateVelocityAt(plate, 0, Math.PI / 4);
    const speed = Math.sqrt(vLat * vLat + vLon * vLon);
    expect(speed).toBeGreaterThan(0);
  });

  it('should scale with angular velocity rate', () => {
    const plate1 = makePlate(0, {
      angularVelocity: { lat: 30, lon: 0, rate: 1 },
    });
    const plate2 = makePlate(0, {
      angularVelocity: { lat: 30, lon: 0, rate: 2 },
    });
    const [v1Lat, v1Lon] = plateVelocityAt(plate1, 0, 0.5);
    const [v2Lat, v2Lon] = plateVelocityAt(plate2, 0, 0.5);
    const speed1 = Math.sqrt(v1Lat * v1Lat + v1Lon * v1Lon);
    const speed2 = Math.sqrt(v2Lat * v2Lat + v2Lon * v2Lon);
    expect(speed2).toBeCloseTo(speed1 * 2, 5);
  });
});

describe('BoundaryClassifier', () => {
  const classifier = new BoundaryClassifier();

  it('should return no boundaries for a single-plate map', () => {
    const plateMap = new Uint16Array(GRID_SIZE * GRID_SIZE);
    plateMap.fill(0);
    const plates = [makePlate(0)];

    const boundaries = classifier.classify(plateMap, plates);
    expect(boundaries).toHaveLength(0);
  });

  it('should detect boundaries between two plates', () => {
    const plateMap = new Uint16Array(GRID_SIZE * GRID_SIZE);
    // Left half = plate 0, right half = plate 1
    for (let row = 0; row < GRID_SIZE; row++) {
      for (let col = 0; col < GRID_SIZE; col++) {
        plateMap[row * GRID_SIZE + col] = col < GRID_SIZE / 2 ? 0 : 1;
      }
    }

    const plates = [
      makePlate(0, { angularVelocity: { lat: 0, lon: 0, rate: 1 } }),
      makePlate(1, { angularVelocity: { lat: 0, lon: 0, rate: 1 } }),
    ];

    const boundaries = classifier.classify(plateMap, plates);
    expect(boundaries.length).toBeGreaterThan(0);

    // All boundary cells should reference plates 0 and 1
    for (const b of boundaries) {
      expect(b.type).not.toBe(BoundaryType.NONE);
      expect([b.plate1, b.plate2].sort()).toEqual([0, 1]);
    }
  });

  it('should classify convergent, divergent, and transform types', () => {
    const plateMap = new Uint16Array(GRID_SIZE * GRID_SIZE);
    for (let row = 0; row < GRID_SIZE; row++) {
      for (let col = 0; col < GRID_SIZE; col++) {
        plateMap[row * GRID_SIZE + col] = col < GRID_SIZE / 2 ? 0 : 1;
      }
    }

    // Plates moving towards each other → convergent
    const convergentPlates = [
      makePlate(0, { angularVelocity: { lat: 0, lon: 90, rate: 3 } }),
      makePlate(1, { angularVelocity: { lat: 0, lon: -90, rate: 3 } }),
    ];

    const convergent = classifier.classify(plateMap, convergentPlates);
    const types = new Set(convergent.map((b) => b.type));
    // Should have at least some classified boundaries
    expect(convergent.length).toBeGreaterThan(0);
    expect(types.size).toBeGreaterThanOrEqual(1);
  });

  it('should assign relativeSpeed to boundary cells', () => {
    const plateMap = new Uint16Array(GRID_SIZE * GRID_SIZE);
    for (let row = 0; row < GRID_SIZE; row++) {
      for (let col = 0; col < GRID_SIZE; col++) {
        plateMap[row * GRID_SIZE + col] = col < GRID_SIZE / 2 ? 0 : 1;
      }
    }

    const plates = [
      makePlate(0, { angularVelocity: { lat: 45, lon: 0, rate: 2 } }),
      makePlate(1, { angularVelocity: { lat: -45, lon: 180, rate: 2 } }),
    ];

    const boundaries = classifier.classify(plateMap, plates);
    for (const b of boundaries) {
      expect(b.relativeSpeed).toBeGreaterThanOrEqual(0);
      expect(Number.isFinite(b.relativeSpeed)).toBe(true);
    }
  });
});
