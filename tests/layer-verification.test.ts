import { describe, it, expect } from 'vitest';
import type { StateBufferViews } from '../src/shared/types';
import {
  TOTAL_BUFFER_SIZE,
  createStateBufferLayout,
  GRID_SIZE,
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

function generatePlanet(seed: number) {
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

// ── Color mapping functions (mirrored from main.ts for testability) ────────

function temperatureColor(t: number): [number, number, number] {
  if (t <= -40) return [0, 0, 200];
  if (t < 0) {
    const f = (t + 40) / 40;
    return [Math.round(f * 255), Math.round(f * 255), 200 + Math.round(f * 55)];
  }
  const f = Math.min(1, t / 45);
  return [200 + Math.round(f * 55), Math.round((1 - f) * 200), Math.round((1 - f) * 200)];
}

function precipitationColor(p: number): [number, number, number] {
  const f = Math.min(1, p / 2500);
  return [
    Math.round(200 - f * 180),
    Math.round(160 + f * 60),
    Math.round(60 + f * 180),
  ];
}

function cloudColor(p: number): [number, number, number] {
  const f = Math.min(1, p / 2500);
  const v = Math.round(60 + f * 195);
  return [v, v, v];
}

function biomassColor(b: number): [number, number, number] {
  const f = Math.min(1, b / 12);
  return [Math.round(10 + f * 20), Math.round(40 + f * 190), Math.round(10 + f * 20)];
}

function topoColor(h: number): [number, number, number] {
  if (h < -5000) return [0, 20, 140];
  if (h < -200) {
    const f = (h + 5000) / 4800;
    return [Math.round(f * 40), Math.round(f * 100 + 60), Math.round(120 + f * 100)];
  }
  if (h < 0)   return [100, 200, 240];
  if (h < 500) return [50,  210,  70];
  if (h < 1500) return [210, 190, 50];
  if (h < 3000) return [190, 110, 35];
  if (h < 5000) return [160, 110, 90];
  return [255, 255, 255];
}

function soilOrderColor(order: number): [number, number, number] {
  switch (order) {
    case 0:  return [180, 180, 180]; // None
    case 1:  return [160, 120, 60];  // Alfisols
    case 2:  return [100, 80,  40];  // Andisols
    case 3:  return [230, 200, 120]; // Aridisols
    case 4:  return [200, 180, 140]; // Entisols
    case 5:  return [220, 240, 255]; // Gelisols
    case 6:  return [60,  100, 60];  // Histosols
    case 7:  return [140, 170, 100]; // Inceptisols
    case 8:  return [180, 140, 80];  // Mollisols
    case 9:  return [80,  50,  20];  // Oxisols
    case 10: return [100, 130, 160]; // Spodosols
    case 11: return [120, 80,  40];  // Ultisols
    case 12: return [160, 100, 140]; // Vertisols
    default: return [180, 180, 180];
  }
}

/** Convert a lat/lon point to a cell index. */
function latLonToCell(lat: number, lon: number): number {
  const normLon = (lon + 180) / 360;
  const normLat = (90 - lat) / 180;
  const col = Math.min(Math.floor(normLon * GRID_SIZE), GRID_SIZE - 1);
  const row = Math.min(Math.floor(normLat * GRID_SIZE), GRID_SIZE - 1);
  return row * GRID_SIZE + col;
}

// ── Layer Color Mapping Tests ───────────────────────────────────────────────
// Verify that each color function produces correct RGB for known data values,
// ensuring the graphic representation matches the underlying cell information.

describe('Layer Verification — Temperature Color Mapping', () => {
  it('should return deep blue for extreme cold (−50°C)', () => {
    const [r, g, b] = temperatureColor(-50);
    expect(r).toBe(0);
    expect(g).toBe(0);
    expect(b).toBe(200);
  });

  it('should return white-ish for freezing point (0°C)', () => {
    const [r, g, b] = temperatureColor(0);
    // At 0°C: f=(0+40)/40=1 → [255,255,255]
    expect(r).toBe(200 + Math.round(0)); // f=0 in second branch
    expect(g).toBe(200);
    expect(b).toBe(200);
  });

  it('should return reddish for hot temperatures (45°C)', () => {
    const [r, g, b] = temperatureColor(45);
    expect(r).toBe(255);
    expect(g).toBe(0);
    expect(b).toBe(0);
  });

  it('should produce gradient in the cold range (−20°C)', () => {
    const [r, g, b] = temperatureColor(-20);
    // f = (-20+40)/40 = 0.5 → [128, 128, 228]
    expect(r).toBe(Math.round(0.5 * 255));
    expect(g).toBe(Math.round(0.5 * 255));
    expect(b).toBe(200 + Math.round(0.5 * 55));
  });
});

describe('Layer Verification — Precipitation Color Mapping', () => {
  it('should return tan for 0 mm/yr (arid)', () => {
    const [r, g, b] = precipitationColor(0);
    expect(r).toBe(200);
    expect(g).toBe(160);
    expect(b).toBe(60);
  });

  it('should return deep blue-green for 2500+ mm/yr (wet)', () => {
    const [r, g, b] = precipitationColor(2500);
    expect(r).toBe(20);
    expect(g).toBe(220);
    expect(b).toBe(240);
  });

  it('should produce middle gradient for 1250 mm/yr', () => {
    const [r, g, b] = precipitationColor(1250);
    const f = 0.5;
    expect(r).toBe(Math.round(200 - f * 180));
    expect(g).toBe(Math.round(160 + f * 60));
    expect(b).toBe(Math.round(60 + f * 180));
  });
});

describe('Layer Verification — Cloud Cover Color Mapping', () => {
  it('should return dark grey for dry conditions (0 mm/yr)', () => {
    const [r, g, b] = cloudColor(0);
    expect(r).toBe(60);
    expect(g).toBe(60);
    expect(b).toBe(60);
  });

  it('should return white for wet conditions (2500+ mm/yr)', () => {
    const [r, g, b] = cloudColor(2500);
    expect(r).toBe(255);
    expect(g).toBe(255);
    expect(b).toBe(255);
  });
});

describe('Layer Verification — Biomass Color Mapping', () => {
  it('should return near-black for barren (0 kg/m²)', () => {
    const [r, g, b] = biomassColor(0);
    expect(r).toBe(10);
    expect(g).toBe(40);
    expect(b).toBe(10);
  });

  it('should return vivid green for lush (12+ kg/m²)', () => {
    const [r, g, b] = biomassColor(12);
    expect(r).toBe(30);
    expect(g).toBe(230);
    expect(b).toBe(30);
  });
});

describe('Layer Verification — Topography Color Mapping', () => {
  it('should return deep blue for deep ocean (−6000m)', () => {
    const [r, g, b] = topoColor(-6000);
    expect(r).toBe(0);
    expect(g).toBe(20);
    expect(b).toBe(140);
  });

  it('should return coastal blue for shallow ocean (−50m)', () => {
    const [r, g, b] = topoColor(-50);
    expect(r).toBe(100);
    expect(g).toBe(200);
    expect(b).toBe(240);
  });

  it('should return green for lowland (200m)', () => {
    const [r, g, b] = topoColor(200);
    expect(r).toBe(50);
    expect(g).toBe(210);
    expect(b).toBe(70);
  });

  it('should return yellow for highland (1000m)', () => {
    const [r, g, b] = topoColor(1000);
    expect(r).toBe(210);
    expect(g).toBe(190);
    expect(b).toBe(50);
  });

  it('should return brown for mountains (2000m)', () => {
    const [r, g, b] = topoColor(2000);
    expect(r).toBe(190);
    expect(g).toBe(110);
    expect(b).toBe(35);
  });

  it('should return white for peaks (6000m+)', () => {
    const [r, g, b] = topoColor(6000);
    expect(r).toBe(255);
    expect(g).toBe(255);
    expect(b).toBe(255);
  });
});

describe('Layer Verification — Soil Order Color Mapping', () => {
  it('should return grey for None (0)', () => {
    const [r, g, b] = soilOrderColor(0);
    expect(r).toBe(180);
    expect(g).toBe(180);
    expect(b).toBe(180);
  });

  it('should return distinct colors for all 12 USDA orders', () => {
    const seen = new Set<string>();
    for (let i = 1; i <= 12; i++) {
      const [r, g, b] = soilOrderColor(i);
      const key = `${r},${g},${b}`;
      expect(seen.has(key)).toBe(false);
      seen.add(key);
    }
    expect(seen.size).toBe(12);
  });

  it('should return grey for unknown order (99)', () => {
    const [r, g, b] = soilOrderColor(99);
    expect(r).toBe(180);
    expect(g).toBe(180);
    expect(b).toBe(180);
  });
});

// ── Random Point Layer Verification ─────────────────────────────────────────
// Generate a planet, pick random geographic points, and verify that the
// layer colour accurately represents the underlying cell data.

describe('Layer Verification — Random Point Temperature', () => {
  it('should colour cold cells blue and warm cells red at random points', { timeout: 15000 }, () => {
    const { views, atmosphere } = generatePlanet(42);

    // Run atmosphere to populate temperature
    atmosphere.tick(-4499, 1);

    // Test random geographic points
    const testPoints = [
      { lat: 0, lon: 0, desc: 'equator' },
      { lat: 80, lon: 45, desc: 'arctic' },
      { lat: -70, lon: -120, desc: 'antarctic' },
      { lat: 30, lon: 90, desc: 'mid-latitude' },
      { lat: -15, lon: -60, desc: 'tropical south' },
    ];

    for (const pt of testPoints) {
      const cellIdx = latLonToCell(pt.lat, pt.lon);
      const temp = views.temperatureMap[cellIdx];
      const [r, , b] = temperatureColor(temp);

      if (temp < -10) {
        // Cold cells: blue channel should dominate
        expect(b).toBeGreaterThanOrEqual(r);
      } else if (temp > 20) {
        // Warm cells: red channel should dominate
        expect(r).toBeGreaterThanOrEqual(b);
      }
      // In-between is a valid gradient, no strict assertion needed
    }
  });
});

describe('Layer Verification — Random Point Precipitation', () => {
  it('should colour dry cells tan and wet cells blue-green at random points', { timeout: 15000 }, () => {
    const { views, atmosphere } = generatePlanet(42);

    atmosphere.tick(-4499, 1);

    const testPoints = [
      { lat: 5, lon: 30, desc: 'tropical' },
      { lat: 60, lon: -10, desc: 'polar front' },
      { lat: 25, lon: 40, desc: 'subtropical' },
      { lat: -40, lon: 170, desc: 'southern ocean' },
    ];

    for (const pt of testPoints) {
      const cellIdx = latLonToCell(pt.lat, pt.lon);
      const precip = views.precipitationMap[cellIdx];
      const [r, , b] = precipitationColor(precip);

      if (precip < 200) {
        // Dry cells: R dominant (tan)
        expect(r).toBeGreaterThan(b);
      } else if (precip > 1500) {
        // Wet cells: B dominant (blue-green)
        expect(b).toBeGreaterThan(r);
      }
    }
  });
});

describe('Layer Verification — Random Point Topography', () => {
  it('should colour ocean cells blue and land cells green/brown at random points', { timeout: 15000 }, () => {
    const { views } = generatePlanet(42);

    const testPoints = [
      { lat: 0, lon: 0 },
      { lat: 45, lon: 90 },
      { lat: -30, lon: -60 },
      { lat: 70, lon: 150 },
      { lat: -60, lon: 0 },
      { lat: 10, lon: -170 },
      { lat: 35, lon: 35 },
      { lat: -45, lon: 120 },
      { lat: 55, lon: -90 },
      { lat: -20, lon: 80 },
    ];

    for (const pt of testPoints) {
      const cellIdx = latLonToCell(pt.lat, pt.lon);
      const h = views.heightMap[cellIdx];

      // Apply the same scaling from fetchLayerRgba for topo
      let scaled: number;
      if (h < 0) {
        const t = Math.max(0, Math.min(1, (h - (-6000)) / ((-100) - (-6000))));
        scaled = t * (-200 - (-7000)) + (-7000);
      } else {
        scaled = Math.min(h, 4000);
      }
      const [r, g, b] = topoColor(scaled);

      if (h < -200) {
        // Ocean cells: blue channel should be dominant
        expect(b).toBeGreaterThanOrEqual(r);
        expect(b).toBeGreaterThanOrEqual(g);
      } else if (h > 500) {
        // Land cells above 500m: not blue
        expect(r + g).toBeGreaterThan(b);
      }
    }
  });
});

describe('Layer Verification — Random Point Biomass', () => {
  it('should colour barren cells dark and vegetated cells green at random points', { timeout: 15000 }, () => {
    const { views, atmosphere, vegetation } = generatePlanet(42);

    // Run atmosphere then vegetation to populate biomass
    atmosphere.tick(-4499, 1);
    vegetation.tick(-4499, 1);

    const testPoints = [
      { lat: 0, lon: 0 },
      { lat: 45, lon: 30 },
      { lat: -20, lon: -50 },
      { lat: 70, lon: 100 },
      { lat: -80, lon: 0 },
    ];

    for (const pt of testPoints) {
      const cellIdx = latLonToCell(pt.lat, pt.lon);
      const biomass = views.biomassMap[cellIdx];
      const [, g] = biomassColor(biomass);

      if (biomass < 1) {
        // Low biomass: green should be close to 40 (min)
        expect(g).toBeLessThan(100);
      } else if (biomass > 5) {
        // Significant biomass: green should be visible
        expect(g).toBeGreaterThan(100);
      }
    }
  });
});

describe('Layer Verification — Plates Layer', () => {
  it('should assign every cell to a valid plate at random points', { timeout: 15000 }, () => {
    const { views, tectonic } = generatePlanet(42);
    const plateCount = tectonic.getPlates().length;

    const testPoints = [
      { lat: 0, lon: 0 },
      { lat: 45, lon: -90 },
      { lat: -30, lon: 60 },
      { lat: 80, lon: 170 },
      { lat: -70, lon: -30 },
    ];

    for (const pt of testPoints) {
      const cellIdx = latLonToCell(pt.lat, pt.lon);
      const plateId = views.plateMap[cellIdx];
      expect(plateId).toBeGreaterThanOrEqual(0);
      expect(plateId).toBeLessThan(plateCount);
    }
  });
});

describe('Layer Verification — Soil Order Layer', () => {
  it('should produce valid soil orders (0-12) after processing', { timeout: 30000 }, () => {
    const { views, atmosphere, surface } = generatePlanet(42);

    atmosphere.tick(-4499, 1);
    surface.tick(-4499, 1);

    const testPoints = [
      { lat: 0, lon: 0 },
      { lat: 45, lon: -90 },
      { lat: -30, lon: 60 },
    ];

    for (const pt of testPoints) {
      const cellIdx = latLonToCell(pt.lat, pt.lon);
      const soilType = views.soilTypeMap[cellIdx];
      expect(soilType).toBeGreaterThanOrEqual(0);
      expect(soilType).toBeLessThanOrEqual(12);

      // Verify the colour mapping returns valid RGB
      const [r, g, b] = soilOrderColor(soilType);
      expect(r).toBeGreaterThanOrEqual(0);
      expect(r).toBeLessThanOrEqual(255);
      expect(g).toBeGreaterThanOrEqual(0);
      expect(g).toBeLessThanOrEqual(255);
      expect(b).toBeGreaterThanOrEqual(0);
      expect(b).toBeLessThanOrEqual(255);
    }
  });
});

describe('Layer Verification — Cloud Layer (precip proxy)', () => {
  it('should produce grey-to-white clouds that match precipitation intensity', { timeout: 15000 }, () => {
    const { views, atmosphere } = generatePlanet(42);

    atmosphere.tick(-4499, 1);

    const testPoints = [
      { lat: 5, lon: 0 },    // equatorial ITCZ
      { lat: 25, lon: 30 },  // subtropical
      { lat: -60, lon: 0 },  // polar front
    ];

    for (const pt of testPoints) {
      const cellIdx = latLonToCell(pt.lat, pt.lon);
      const precip = views.precipitationMap[cellIdx];
      const [r, g, b] = cloudColor(precip);

      // Cloud colour is greyscale: R == G == B
      expect(r).toBe(g);
      expect(g).toBe(b);
      // Cloudiness increases with precipitation
      expect(r).toBeGreaterThanOrEqual(60);
      expect(r).toBeLessThanOrEqual(255);
    }
  });
});

describe('Layer Verification — All Layers Consistency', () => {
  it('should produce consistent layer data for the same cell across layers', { timeout: 15000 }, () => {
    const { views, atmosphere, vegetation } = generatePlanet(42);

    atmosphere.tick(-4499, 1);
    vegetation.tick(-4499, 1);

    // Use a single random point and verify multiple layers are consistent
    const cellIdx = latLonToCell(20, 40);
    const h = views.heightMap[cellIdx];
    const temp = views.temperatureMap[cellIdx];
    const precip = views.precipitationMap[cellIdx];
    const biomass = views.biomassMap[cellIdx];

    // Height and temperature should be correlated (higher = colder via lapse rate)
    // This is a soft check; the exact relationship depends on latitude
    expect(typeof h).toBe('number');
    expect(typeof temp).toBe('number');
    expect(typeof precip).toBe('number');
    expect(typeof biomass).toBe('number');
    expect(Number.isFinite(h)).toBe(true);
    expect(Number.isFinite(temp)).toBe(true);
    expect(Number.isFinite(precip)).toBe(true);
    expect(Number.isFinite(biomass)).toBe(true);

    // Ocean cells should have near-zero biomass
    if (h < 0) {
      expect(biomass).toBeLessThanOrEqual(0.01);
    }

    // Verify all colour functions produce valid RGB for these values
    for (const [r, g, b] of [
      temperatureColor(temp),
      precipitationColor(precip),
      cloudColor(precip),
      biomassColor(biomass),
    ]) {
      expect(r).toBeGreaterThanOrEqual(0);
      expect(r).toBeLessThanOrEqual(255);
      expect(g).toBeGreaterThanOrEqual(0);
      expect(g).toBeLessThanOrEqual(255);
      expect(b).toBeGreaterThanOrEqual(0);
      expect(b).toBeLessThanOrEqual(255);
    }
  });
});
