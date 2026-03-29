// ─── Cross-Section Engine Tests ─────────────────────────────────────────────
// Extensive unit tests for the Phase 5 cross-section engine, covering:
// • Great-circle interpolation and path sampling
// • Coordinate mapping (lat/lon → grid index)
// • Stratigraphy retrieval along cross-section paths
// • Profile construction with deep-earth zones
// • Edge cases (single point, antipodal, polar paths)

import {
  CrossSectionEngine,
  centralAngle,
  greatCircleInterpolate,
  samplePathPoints,
  computePathDistanceKm,
  latToRow,
  lonToCol,
  latLonToIndex,
  getDeepEarthZones,
  EARTH_RADIUS_KM,
  DEFAULT_SAMPLE_COUNT,
} from '../src/geo/cross-section-engine';
import { StratigraphyStack } from '../src/geo/stratigraphy';
import { EventBus } from '../src/kernel/event-bus';
import {
  GRID_SIZE,
  TOTAL_BUFFER_SIZE,
  createStateBufferLayout,
  RockType,
  SoilOrder,
  DeformationType,
} from '../src/shared/types';
import type {
  StateBufferViews,
  LatLon,
  CrossSectionProfile,
  StratigraphicLayer,
} from '../src/shared/types';

// ─── Helpers ────────────────────────────────────────────────────────────────

function makeViews(): StateBufferViews {
  const buf = new ArrayBuffer(TOTAL_BUFFER_SIZE) as unknown as SharedArrayBuffer;
  return createStateBufferLayout(buf);
}

function makeLayer(overrides?: Partial<StratigraphicLayer>): StratigraphicLayer {
  return {
    rockType: RockType.IGN_BASALT,
    ageDeposited: -4000,
    thickness: 1000,
    dipAngle: 0,
    dipDirection: 0,
    deformation: DeformationType.UNDEFORMED,
    unconformity: false,
    soilHorizon: SoilOrder.NONE,
    formationName: 0,
    ...overrides,
  };
}

// ─── Coordinate Helpers Tests ───────────────────────────────────────────────

describe('Coordinate Helpers', () => {
  it('latToRow: north pole → row 0', () => {
    expect(latToRow(90)).toBe(0);
  });

  it('latToRow: south pole → row GRID_SIZE-1', () => {
    expect(latToRow(-90)).toBe(GRID_SIZE - 1);
  });

  it('latToRow: equator → middle row', () => {
    const row = latToRow(0);
    expect(row).toBeCloseTo(GRID_SIZE / 2, -1);
  });

  it('lonToCol: -180° → col 0', () => {
    expect(lonToCol(-180)).toBe(0);
  });

  it('lonToCol: 180° → col GRID_SIZE-1', () => {
    expect(lonToCol(180)).toBe(GRID_SIZE - 1);
  });

  it('lonToCol: 0° → middle col', () => {
    const col = lonToCol(0);
    expect(col).toBeCloseTo(GRID_SIZE / 2, -1);
  });

  it('latLonToIndex: north pole, date line', () => {
    const idx = latLonToIndex(90, -180);
    expect(idx).toBe(0); // row 0, col 0
  });

  it('latLonToIndex: south pole returns valid index', () => {
    const idx = latLonToIndex(-90, 0);
    expect(idx).toBeGreaterThanOrEqual(0);
    expect(idx).toBeLessThan(GRID_SIZE * GRID_SIZE);
  });

  it('latToRow clamps out-of-range values', () => {
    expect(latToRow(100)).toBe(0);   // clamped to 90
    expect(latToRow(-100)).toBe(GRID_SIZE - 1); // clamped to -90
  });

  it('lonToCol wraps around', () => {
    const c1 = lonToCol(0);
    const c2 = lonToCol(360);
    expect(c1).toBe(c2);
  });
});

// ─── Central Angle Tests ────────────────────────────────────────────────────

describe('centralAngle', () => {
  it('same point → 0 radians', () => {
    expect(centralAngle(45, 90, 45, 90)).toBeCloseTo(0, 10);
  });

  it('equator 0° to equator 180° → π radians (antipodal)', () => {
    const angle = centralAngle(0, 0, 0, 180);
    expect(angle).toBeCloseTo(Math.PI, 5);
  });

  it('equator 0° to equator 90° → π/2 radians', () => {
    const angle = centralAngle(0, 0, 0, 90);
    expect(angle).toBeCloseTo(Math.PI / 2, 5);
  });

  it('north pole to south pole → π radians', () => {
    const angle = centralAngle(90, 0, -90, 0);
    expect(angle).toBeCloseTo(Math.PI, 5);
  });

  it('symmetric: distance A→B == distance B→A', () => {
    const ab = centralAngle(30, 40, -20, 100);
    const ba = centralAngle(-20, 100, 30, 40);
    expect(ab).toBeCloseTo(ba, 10);
  });

  it('small distance is approximately linear', () => {
    // ~1 degree of latitude ≈ 111 km
    const angle = centralAngle(0, 0, 1, 0);
    const distKm = angle * EARTH_RADIUS_KM;
    expect(distKm).toBeCloseTo(111.2, 0);
  });
});

// ─── Great-Circle Interpolation Tests ───────────────────────────────────────

describe('greatCircleInterpolate', () => {
  it('t=0 returns start point', () => {
    const p = greatCircleInterpolate(30, 40, -20, 100, 0);
    expect(p.lat).toBeCloseTo(30, 5);
    expect(p.lon).toBeCloseTo(40, 5);
  });

  it('t=1 returns end point', () => {
    const p = greatCircleInterpolate(30, 40, -20, 100, 1);
    expect(p.lat).toBeCloseTo(-20, 5);
    expect(p.lon).toBeCloseTo(100, 5);
  });

  it('t=0.5 returns midpoint on great circle', () => {
    const p = greatCircleInterpolate(0, 0, 0, 90, 0.5);
    // Midpoint along equator from 0° to 90° should be ~45°
    expect(p.lat).toBeCloseTo(0, 1);
    expect(p.lon).toBeCloseTo(45, 1);
  });

  it('degenerate: same start and end → returns that point', () => {
    const p = greatCircleInterpolate(45, 90, 45, 90, 0.5);
    expect(p.lat).toBeCloseTo(45, 5);
    expect(p.lon).toBeCloseTo(90, 5);
  });

  it('interpolation stays on the sphere (lat in [-90, 90])', () => {
    for (let t = 0; t <= 1; t += 0.1) {
      const p = greatCircleInterpolate(60, -30, -40, 150, t);
      expect(p.lat).toBeGreaterThanOrEqual(-90);
      expect(p.lat).toBeLessThanOrEqual(90);
      expect(p.lon).toBeGreaterThanOrEqual(-180);
      expect(p.lon).toBeLessThanOrEqual(180);
    }
  });
});

// ─── Path Sampling Tests ────────────────────────────────────────────────────

describe('samplePathPoints', () => {
  it('empty path → empty samples', () => {
    expect(samplePathPoints([])).toEqual([]);
  });

  it('single point → single sample', () => {
    const samples = samplePathPoints([{ lat: 10, lon: 20 }]);
    expect(samples).toHaveLength(1);
    expect(samples[0].lat).toBe(10);
    expect(samples[0].lon).toBe(20);
  });

  it('two points → N equally spaced samples', () => {
    const samples = samplePathPoints(
      [{ lat: 0, lon: 0 }, { lat: 0, lon: 90 }],
      10,
    );
    expect(samples).toHaveLength(10);
    expect(samples[0].lat).toBeCloseTo(0, 1);
    expect(samples[0].lon).toBeCloseTo(0, 1);
    expect(samples[9].lat).toBeCloseTo(0, 1);
    expect(samples[9].lon).toBeCloseTo(90, 1);
  });

  it('samples are evenly spaced in longitude for equatorial path', () => {
    const samples = samplePathPoints(
      [{ lat: 0, lon: 0 }, { lat: 0, lon: 90 }],
      5,
    );
    for (let i = 0; i < 5; i++) {
      expect(samples[i].lon).toBeCloseTo(i * (90 / 4), 0);
    }
  });

  it('multi-segment path samples across all segments', () => {
    const samples = samplePathPoints(
      [
        { lat: 0, lon: 0 },
        { lat: 0, lon: 45 },
        { lat: 0, lon: 90 },
      ],
      9,
    );
    expect(samples).toHaveLength(9);
    expect(samples[0].lon).toBeCloseTo(0, 0);
    expect(samples[8].lon).toBeCloseTo(90, 0);
  });

  it('default sample count is DEFAULT_SAMPLE_COUNT', () => {
    const samples = samplePathPoints([{ lat: 0, lon: 0 }, { lat: 0, lon: 1 }]);
    expect(samples).toHaveLength(DEFAULT_SAMPLE_COUNT);
  });
});

// ─── Path Distance Tests ────────────────────────────────────────────────────

describe('computePathDistanceKm', () => {
  it('single point → 0 km', () => {
    expect(computePathDistanceKm([{ lat: 0, lon: 0 }])).toBe(0);
  });

  it('equator 0° to 90° ≈ 10,000 km', () => {
    const dist = computePathDistanceKm([{ lat: 0, lon: 0 }, { lat: 0, lon: 90 }]);
    // Quarter of Earth circumference ≈ 10,008 km
    expect(dist).toBeCloseTo(10008, -2);
  });

  it('north pole to south pole ≈ 20,000 km', () => {
    const dist = computePathDistanceKm([{ lat: 90, lon: 0 }, { lat: -90, lon: 0 }]);
    expect(dist).toBeCloseTo(20015, -2);
  });

  it('multi-segment equals sum of segments', () => {
    const d1 = computePathDistanceKm([{ lat: 0, lon: 0 }, { lat: 0, lon: 45 }]);
    const d2 = computePathDistanceKm([{ lat: 0, lon: 45 }, { lat: 0, lon: 90 }]);
    const dTotal = computePathDistanceKm([
      { lat: 0, lon: 0 },
      { lat: 0, lon: 45 },
      { lat: 0, lon: 90 },
    ]);
    expect(dTotal).toBeCloseTo(d1 + d2, 1);
  });
});

// ─── Deep Earth Zones Tests ─────────────────────────────────────────────────

describe('getDeepEarthZones', () => {
  it('returns 7 zones', () => {
    const zones = getDeepEarthZones();
    expect(zones).toHaveLength(7);
  });

  it('first zone starts at 30 km (lithospheric mantle)', () => {
    expect(getDeepEarthZones()[0].topKm).toBe(30);
    expect(getDeepEarthZones()[0].name).toBe('Lithospheric Mantle');
  });

  it('last zone ends at 6371 km (inner core)', () => {
    const zones = getDeepEarthZones();
    const last = zones[zones.length - 1];
    expect(last.bottomKm).toBe(6371);
    expect(last.name).toBe('Inner Core');
  });

  it('zones cover continuous depth range', () => {
    const zones = getDeepEarthZones();
    for (let i = 1; i < zones.length; i++) {
      expect(zones[i].topKm).toBeLessThanOrEqual(zones[i - 1].bottomKm + 1);
    }
  });

  it('each zone has a valid deep earth rock type', () => {
    const zones = getDeepEarthZones();
    for (const zone of zones) {
      expect(zone.rockType).toBeGreaterThanOrEqual(RockType.DEEP_LITHMAN);
      expect(zone.rockType).toBeLessThanOrEqual(RockType.DEEP_INCORE);
    }
  });
});

// ─── CrossSectionEngine Integration Tests ───────────────────────────────────

describe('CrossSectionEngine', () => {
  let bus: EventBus;
  let views: StateBufferViews;
  let stratigraphy: StratigraphyStack;

  beforeEach(() => {
    bus = new EventBus();
    views = makeViews();
    stratigraphy = new StratigraphyStack();
  });

  it('should construct without error', () => {
    expect(() => new CrossSectionEngine(bus)).not.toThrow();
  });

  it('should return null profile before initialization', () => {
    const engine = new CrossSectionEngine(bus);
    expect(engine.getProfile()).toBeNull();
  });

  it('should return null if path has fewer than 2 points', () => {
    const engine = new CrossSectionEngine(bus);
    engine.initialize(views, stratigraphy);
    const result = engine.processPath([{ lat: 0, lon: 0 }]);
    expect(result).toBeNull();
  });

  it('should build a profile for a valid 2-point path', () => {
    const engine = new CrossSectionEngine(bus, { sampleCount: 10 });
    engine.initialize(views, stratigraphy);

    const profile = engine.processPath([
      { lat: 0, lon: 0 },
      { lat: 0, lon: 10 },
    ]);

    expect(profile).not.toBeNull();
    expect(profile!.samples).toHaveLength(10);
    expect(profile!.totalDistanceKm).toBeGreaterThan(0);
    expect(profile!.pathPoints).toHaveLength(2);
    expect(profile!.deepEarthZones).toHaveLength(7);
  });

  it('should store profile as current after processPath', () => {
    const engine = new CrossSectionEngine(bus, { sampleCount: 5 });
    engine.initialize(views, stratigraphy);

    engine.processPath([
      { lat: 10, lon: 20 },
      { lat: -10, lon: 40 },
    ]);

    expect(engine.getProfile()).not.toBeNull();
    expect(engine.getProfile()!.samples).toHaveLength(5);
  });

  it('should clear the current profile', () => {
    const engine = new CrossSectionEngine(bus, { sampleCount: 3 });
    engine.initialize(views, stratigraphy);

    engine.processPath([{ lat: 0, lon: 0 }, { lat: 0, lon: 10 }]);
    expect(engine.getProfile()).not.toBeNull();

    engine.clear();
    expect(engine.getProfile()).toBeNull();
  });

  it('should emit CROSS_SECTION_READY event on processPath', () => {
    const engine = new CrossSectionEngine(bus, { sampleCount: 3 });
    engine.initialize(views, stratigraphy);

    let received = false;
    bus.on('CROSS_SECTION_READY', () => {
      received = true;
    });

    engine.processPath([{ lat: 0, lon: 0 }, { lat: 0, lon: 10 }]);
    expect(received).toBe(true);
  });

  it('should respond to CROSS_SECTION_PATH event', () => {
    const engine = new CrossSectionEngine(bus, { sampleCount: 5 });
    engine.initialize(views, stratigraphy);

    bus.emit('CROSS_SECTION_PATH', {
      points: [{ lat: 0, lon: 0 }, { lat: 0, lon: 20 }],
    });

    expect(engine.getProfile()).not.toBeNull();
    expect(engine.getProfile()!.samples).toHaveLength(5);
  });

  it('should retrieve stratigraphy layers at sample points', () => {
    // Set up some stratigraphy data at a known cell
    const idx = latLonToIndex(0, 0);
    stratigraphy.initializeBasement(idx, false, -4500);
    stratigraphy.pushLayer(idx, makeLayer({
      rockType: RockType.SED_SANDSTONE,
      thickness: 500,
    }));

    const engine = new CrossSectionEngine(bus, { sampleCount: 3 });
    engine.initialize(views, stratigraphy);

    const profile = engine.processPath([
      { lat: 0, lon: -1 },
      { lat: 0, lon: 1 },
    ]);

    expect(profile).not.toBeNull();
    // The middle sample should be near (0, 0) and should have layers
    const midSample = profile!.samples[1];
    expect(midSample.layers.length).toBeGreaterThan(0);
  });

  it('should include surface elevation from heightMap', () => {
    const idx = latLonToIndex(0, 0);
    views.heightMap[idx] = 1500;

    const engine = new CrossSectionEngine(bus, { sampleCount: 3 });
    engine.initialize(views, stratigraphy);

    const profile = engine.processPath([
      { lat: 0, lon: -1 },
      { lat: 0, lon: 1 },
    ]);

    // The middle sample should have the set elevation
    const midSample = profile!.samples[1];
    expect(midSample.surfaceElevation).toBe(1500);
  });

  it('should include crust thickness from crustThicknessMap', () => {
    const idx = latLonToIndex(45, 90);
    views.crustThicknessMap[idx] = 35000; // 35 km in meters

    const engine = new CrossSectionEngine(bus, { sampleCount: 3 });
    engine.initialize(views, stratigraphy);

    const profile = engine.processPath([
      { lat: 45, lon: 89 },
      { lat: 45, lon: 91 },
    ]);

    const midSample = profile!.samples[1];
    expect(midSample.crustThicknessKm).toBeCloseTo(35, 0);
  });

  it('should include soil type and depth', () => {
    const idx = latLonToIndex(30, 60);
    views.soilTypeMap[idx] = SoilOrder.MOLLISOL;
    views.soilDepthMap[idx] = 2.5;

    const engine = new CrossSectionEngine(bus, { sampleCount: 3 });
    engine.initialize(views, stratigraphy);

    const profile = engine.processPath([
      { lat: 30, lon: 59 },
      { lat: 30, lon: 61 },
    ]);

    const midSample = profile!.samples[1];
    expect(midSample.soilType).toBe(SoilOrder.MOLLISOL);
    expect(midSample.soilDepthM).toBeCloseTo(2.5, 1);
  });

  it('should toggle labels visibility via LABEL_TOGGLE event', () => {
    const engine = new CrossSectionEngine(bus);
    expect(engine.getLabelsVisible()).toBe(true);

    bus.emit('LABEL_TOGGLE', { visible: false });
    expect(engine.getLabelsVisible()).toBe(false);

    bus.emit('LABEL_TOGGLE', { visible: true });
    expect(engine.getLabelsVisible()).toBe(true);
  });

  it('buildProfile: samples have monotonically increasing distanceKm', () => {
    const engine = new CrossSectionEngine(bus, { sampleCount: 20 });
    engine.initialize(views, stratigraphy);

    const profile = engine.buildProfile(
      [{ lat: 0, lon: 0 }, { lat: 45, lon: 90 }],
      views,
      stratigraphy,
    );

    for (let i = 1; i < profile.samples.length; i++) {
      expect(profile.samples[i].distanceKm)
        .toBeGreaterThanOrEqual(profile.samples[i - 1].distanceKm);
    }
  });

  it('buildProfile: first sample at 0 km, last at totalDistanceKm', () => {
    const engine = new CrossSectionEngine(bus, { sampleCount: 10 });
    engine.initialize(views, stratigraphy);

    const profile = engine.buildProfile(
      [{ lat: 0, lon: 0 }, { lat: 0, lon: 90 }],
      views,
      stratigraphy,
    );

    expect(profile.samples[0].distanceKm).toBeCloseTo(0, 5);
    expect(profile.samples[profile.samples.length - 1].distanceKm)
      .toBeCloseTo(profile.totalDistanceKm, 1);
  });

  it('should handle polar cross-section path', () => {
    const engine = new CrossSectionEngine(bus, { sampleCount: 5 });
    engine.initialize(views, stratigraphy);

    const profile = engine.processPath([
      { lat: 90, lon: 0 },
      { lat: -90, lon: 0 },
    ]);

    expect(profile).not.toBeNull();
    expect(profile!.samples).toHaveLength(5);
    expect(profile!.totalDistanceKm).toBeCloseTo(20015, -2);
  });

  it('should handle cross-section with multiple waypoints', () => {
    const engine = new CrossSectionEngine(bus, { sampleCount: 20 });
    engine.initialize(views, stratigraphy);

    const profile = engine.processPath([
      { lat: 0, lon: 0 },
      { lat: 30, lon: 30 },
      { lat: 60, lon: 60 },
      { lat: 30, lon: 90 },
    ]);

    expect(profile).not.toBeNull();
    expect(profile!.samples).toHaveLength(20);
    expect(profile!.pathPoints).toHaveLength(4);
  });

  it('should not modify stratigraphy during profile build', () => {
    const idx = latLonToIndex(0, 0);
    stratigraphy.initializeBasement(idx, false, -4500);
    const layersBefore = stratigraphy.getLayers(idx).length;

    const engine = new CrossSectionEngine(bus, { sampleCount: 5 });
    engine.initialize(views, stratigraphy);

    engine.processPath([{ lat: 0, lon: -1 }, { lat: 0, lon: 1 }]);

    expect(stratigraphy.getLayers(idx).length).toBe(layersBefore);
  });
});
