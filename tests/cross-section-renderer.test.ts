// ─── Cross-Section Renderer Tests ───────────────────────────────────────────
// Unit tests for the Phase 5 cross-section renderer, covering:
// • Rock type colour mapping
// • Rock type name mapping
// • Soil order name mapping
// • Vertical scale (split linear/logarithmic)
// • Label building
// • Export utility
//
// @vitest-environment jsdom

import {
  getRockColor,
  getRockName,
  getSoilName,
  depthToY,
  buildLayerLabel,
  exportCrossSectionPNG,
  renderCrossSection,
} from '../src/render/cross-section-renderer';
import {
  RockType,
  SoilOrder,
  DeformationType,
} from '../src/shared/types';
import type {
  StratigraphicLayer,
  CrossSectionProfile,
  CrossSectionSample,
} from '../src/shared/types';
import { getDeepEarthZones } from '../src/geo/cross-section-engine';

// ─── Helpers ────────────────────────────────────────────────────────────────

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

function makeSample(overrides?: Partial<CrossSectionSample>): CrossSectionSample {
  return {
    distanceKm: 0,
    surfaceElevation: 500,
    crustThicknessKm: 30,
    soilType: SoilOrder.NONE,
    soilDepthM: 0,
    layers: [makeLayer()],
    ...overrides,
  };
}

function makeProfile(numSamples: number = 5): CrossSectionProfile {
  const samples: CrossSectionSample[] = [];
  for (let i = 0; i < numSamples; i++) {
    samples.push(makeSample({
      distanceKm: i * 100,
      layers: [
        makeLayer({ rockType: RockType.MET_GNEISS, thickness: 15000 }),
        makeLayer({ rockType: RockType.IGN_GRANITE, thickness: 20000 }),
        makeLayer({ rockType: RockType.SED_SANDSTONE, thickness: 200 }),
      ],
    }));
  }
  return {
    samples,
    totalDistanceKm: (numSamples - 1) * 100,
    pathPoints: [{ lat: 0, lon: 0 }, { lat: 0, lon: numSamples * 10 }],
    deepEarthZones: getDeepEarthZones(),
  };
}

// ─── Rock Color Tests ───────────────────────────────────────────────────────

describe('getRockColor', () => {
  it('returns a hex colour string for basalt', () => {
    const c = getRockColor(RockType.IGN_BASALT);
    expect(c).toMatch(/^#[0-9a-fA-F]{6}$/);
  });

  it('returns different colours for different rock types', () => {
    const basalt = getRockColor(RockType.IGN_BASALT);
    const granite = getRockColor(RockType.IGN_GRANITE);
    const sandstone = getRockColor(RockType.SED_SANDSTONE);
    expect(basalt).not.toBe(granite);
    expect(basalt).not.toBe(sandstone);
  });

  it('returns a colour for all standard igneous types', () => {
    const igneous = [
      RockType.IGN_BASALT, RockType.IGN_GABBRO, RockType.IGN_RHYOLITE,
      RockType.IGN_GRANITE, RockType.IGN_ANDESITE, RockType.IGN_DACITE,
      RockType.IGN_OBSIDIAN, RockType.IGN_PUMICE, RockType.IGN_PERIDOTITE,
      RockType.IGN_KOMATIITE, RockType.IGN_SYENITE, RockType.IGN_DIORITE,
      RockType.IGN_PYROCLASTIC, RockType.IGN_TUFF, RockType.IGN_PILLOW_BASALT,
    ];
    for (const rt of igneous) {
      expect(getRockColor(rt)).toMatch(/^#[0-9a-fA-F]{6}$/);
    }
  });

  it('returns a colour for all sedimentary types', () => {
    const sedimentary = [
      RockType.SED_SANDSTONE, RockType.SED_SHALE, RockType.SED_LIMESTONE,
      RockType.SED_DOLOSTONE, RockType.SED_CONGLOMERATE, RockType.SED_COAL,
      RockType.SED_CHALK, RockType.SED_CHERT, RockType.SED_EVAPORITE,
    ];
    for (const rt of sedimentary) {
      expect(getRockColor(rt)).toMatch(/^#[0-9a-fA-F]{6}$/);
    }
  });

  it('returns a colour for all metamorphic types', () => {
    const metamorphic = [
      RockType.MET_SLATE, RockType.MET_PHYLLITE, RockType.MET_SCHIST,
      RockType.MET_GNEISS, RockType.MET_QUARTZITE, RockType.MET_MARBLE,
      RockType.MET_AMPHIBOLITE, RockType.MET_ECLOGITE, RockType.MET_BLUESCHIST,
    ];
    for (const rt of metamorphic) {
      expect(getRockColor(rt)).toMatch(/^#[0-9a-fA-F]{6}$/);
    }
  });

  it('returns a colour for deep earth types', () => {
    const deep = [
      RockType.DEEP_LITHMAN, RockType.DEEP_ASTHEN, RockType.DEEP_TRANS,
      RockType.DEEP_LOWMAN, RockType.DEEP_CMB, RockType.DEEP_OUTCORE,
      RockType.DEEP_INCORE,
    ];
    for (const rt of deep) {
      expect(getRockColor(rt)).toMatch(/^#[0-9a-fA-F]{6}$/);
    }
  });

  it('returns fallback colour for unknown rock type', () => {
    const c = getRockColor(999 as RockType);
    expect(c).toMatch(/^#[0-9a-fA-F]{6}$/);
  });
});

// ─── Rock Name Tests ────────────────────────────────────────────────────────

describe('getRockName', () => {
  it('returns "Basalt" for IGN_BASALT', () => {
    expect(getRockName(RockType.IGN_BASALT)).toBe('Basalt');
  });

  it('returns "Granite" for IGN_GRANITE', () => {
    expect(getRockName(RockType.IGN_GRANITE)).toBe('Granite');
  });

  it('returns "Sandstone" for SED_SANDSTONE', () => {
    expect(getRockName(RockType.SED_SANDSTONE)).toBe('Sandstone');
  });

  it('returns "Gneiss" for MET_GNEISS', () => {
    expect(getRockName(RockType.MET_GNEISS)).toBe('Gneiss');
  });

  it('returns "Inner Core" for DEEP_INCORE', () => {
    expect(getRockName(RockType.DEEP_INCORE)).toBe('Inner Core');
  });

  it('returns fallback for unknown rock type', () => {
    expect(getRockName(999 as RockType)).toContain('Rock #');
  });
});

// ─── Soil Name Tests ────────────────────────────────────────────────────────

describe('getSoilName', () => {
  it('returns empty string for NONE', () => {
    expect(getSoilName(SoilOrder.NONE)).toBe('');
  });

  it('returns "Mollisol" for MOLLISOL', () => {
    expect(getSoilName(SoilOrder.MOLLISOL)).toBe('Mollisol');
  });

  it('returns "Oxisol" for OXISOL', () => {
    expect(getSoilName(SoilOrder.OXISOL)).toBe('Oxisol');
  });

  it('returns "Gelisol" for GELISOL', () => {
    expect(getSoilName(SoilOrder.GELISOL)).toBe('Gelisol');
  });

  it('returns names for all 12 USDA orders', () => {
    const orders = [
      SoilOrder.ENTISOL, SoilOrder.INCEPTISOL, SoilOrder.MOLLISOL,
      SoilOrder.ALFISOL, SoilOrder.ULTISOL, SoilOrder.OXISOL,
      SoilOrder.SPODOSOL, SoilOrder.HISTOSOL, SoilOrder.ARIDISOL,
      SoilOrder.VERTISOL, SoilOrder.ANDISOL, SoilOrder.GELISOL,
    ];
    for (const o of orders) {
      expect(getSoilName(o).length).toBeGreaterThan(0);
    }
  });
});

// ─── Vertical Scale Tests ───────────────────────────────────────────────────

describe('depthToY', () => {
  const H = 600;
  const TOP = 30;

  it('depth 0 → topMargin', () => {
    expect(depthToY(0, H, TOP)).toBe(TOP);
  });

  it('depth increases → Y increases (lower on screen)', () => {
    const y10 = depthToY(10, H, TOP);
    const y50 = depthToY(50, H, TOP);
    const y100 = depthToY(100, H, TOP);
    const y1000 = depthToY(1000, H, TOP);
    expect(y10).toBeGreaterThan(TOP);
    expect(y50).toBeGreaterThan(y10);
    expect(y100).toBeGreaterThan(y50);
    expect(y1000).toBeGreaterThan(y100);
  });

  it('linear section: 50 km is roughly half of 100 km', () => {
    const y50 = depthToY(50, H, TOP);
    const y100 = depthToY(100, H, TOP);
    const linearRange = y100 - TOP;
    const midExpected = TOP + linearRange / 2;
    expect(y50).toBeCloseTo(midExpected, 0);
  });

  it('logarithmic section: 6371 km is at the bottom of the canvas', () => {
    const y = depthToY(6371, H, TOP);
    expect(y).toBeCloseTo(H, 0);
  });

  it('scale break at 100 km', () => {
    const yJustBelow = depthToY(99, H, TOP);
    const yAt = depthToY(100, H, TOP);
    const yJustAbove = depthToY(101, H, TOP);
    expect(yJustAbove).toBeGreaterThan(yAt);
    expect(yAt).toBeGreaterThan(yJustBelow);
  });

  it('negative depth returns topMargin', () => {
    expect(depthToY(-10, H, TOP)).toBe(TOP);
  });

  it('very large depth clamps to total depth', () => {
    const yMax = depthToY(6371, H, TOP);
    const yOver = depthToY(10000, H, TOP);
    expect(yOver).toBeCloseTo(yMax, 0);
  });
});

// ─── Label Building Tests ───────────────────────────────────────────────────

describe('buildLayerLabel', () => {
  it('basic label has rock name and age', () => {
    const label = buildLayerLabel(makeLayer());
    expect(label).toContain('Basalt');
    expect(label).toContain('4000 Ma');
  });

  it('includes soil name when provided', () => {
    const label = buildLayerLabel(makeLayer(), SoilOrder.MOLLISOL);
    expect(label).toContain('Mollisol');
  });

  it('no soil when NONE', () => {
    const label = buildLayerLabel(makeLayer(), SoilOrder.NONE);
    expect(label).not.toContain('Mollisol');
    expect(label).not.toContain('Entisol');
  });

  it('no soil when undefined', () => {
    const label = buildLayerLabel(makeLayer());
    expect(label.split(' · ')).toHaveLength(2); // rockName + age
  });

  it('label parts separated by " · "', () => {
    const label = buildLayerLabel(makeLayer(), SoilOrder.OXISOL);
    const parts = label.split(' · ');
    expect(parts).toHaveLength(3);
    expect(parts[0]).toBe('Basalt');
    expect(parts[1]).toBe('4000 Ma');
    expect(parts[2]).toBe('Oxisol');
  });
});

// ─── Render Function Tests ──────────────────────────────────────────────────
// In jsdom, Canvas 2D context is not available.  These tests verify the
// function handles the null context gracefully and returns a canvas element
// with correct dimensions.

describe('renderCrossSection', () => {
  it('should not throw for empty profile', () => {
    const emptyProfile: CrossSectionProfile = {
      samples: [],
      totalDistanceKm: 0,
      pathPoints: [],
      deepEarthZones: getDeepEarthZones(),
    };
    expect(() => {
      renderCrossSection(emptyProfile, {
        width: 800,
        height: 400,
        showLabels: false,
        showLegend: false,
      });
    }).not.toThrow();
  });

  it('should not throw for a populated profile', () => {
    const profile = makeProfile(10);
    expect(() => {
      renderCrossSection(profile, {
        width: 960,
        height: 400,
        showLabels: true,
        showLegend: true,
      });
    }).not.toThrow();
  });

  it('should not throw with labels off', () => {
    const profile = makeProfile(5);
    expect(() => {
      renderCrossSection(profile, {
        width: 800,
        height: 300,
        showLabels: false,
        showLegend: false,
      });
    }).not.toThrow();
  });

  it('should accept a pre-existing canvas and set dimensions', () => {
    const canvas = document.createElement('canvas');
    const profile = makeProfile(3);
    const result = renderCrossSection(profile, {
      width: 640,
      height: 320,
      showLabels: false,
      showLegend: false,
    }, canvas);
    expect(result).toBe(canvas);
    expect(result.width).toBe(640);
    expect(result.height).toBe(320);
  });

  it('should handle profile with unconformities', () => {
    const profile = makeProfile(3);
    profile.samples[1].layers[1].unconformity = true;
    expect(() => {
      renderCrossSection(profile, {
        width: 800,
        height: 400,
        showLabels: true,
        showLegend: true,
      });
    }).not.toThrow();
  });

  it('should return a canvas with correct dimensions', () => {
    const profile = makeProfile(3);
    const canvas = renderCrossSection(profile, {
      width: 1024,
      height: 512,
      showLabels: false,
      showLegend: false,
    });
    expect(canvas.width).toBe(1024);
    expect(canvas.height).toBe(512);
  });
});

// ─── Export PNG Tests ───────────────────────────────────────────────────────

describe('exportCrossSectionPNG', () => {
  it('returns a value from toDataURL', () => {
    const canvas = document.createElement('canvas');
    canvas.width = 100;
    canvas.height = 100;
    // In jsdom without native canvas, toDataURL returns 'data:,' — just verify it doesn't throw
    expect(() => exportCrossSectionPNG(canvas)).not.toThrow();
  });
});
