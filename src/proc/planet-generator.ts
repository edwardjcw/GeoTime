// ─── Procedural Planet Generator ────────────────────────────────────────────
// Generates initial conditions for a terrestrial planet: tectonic plates,
// heightfield, crust properties, mantle plume hotspots, and baseline
// atmosphere.  All results are written directly into the shared-memory
// StateBufferViews so every worker can read them immediately.

import type {
  StateBufferViews,
  PlateInfo,
  HotspotInfo,
  AtmosphericComposition,
} from '../shared/types';
import { GRID_SIZE, RockType } from '../shared/types';
import { Xoshiro256ss } from './prng';
import { SimplexNoise } from './simplex-noise';

// ─── Helpers ────────────────────────────────────────────────────────────────

const TWO_PI = 2 * Math.PI;
const DEG2RAD = Math.PI / 180;

/** Map a grid row to latitude in radians (−π/2 … π/2). */
function rowToLat(row: number): number {
  return (Math.PI / 2) - (row / GRID_SIZE) * Math.PI;
}

/** Map a grid column to longitude in radians (−π … π). */
function colToLon(col: number): number {
  return (col / GRID_SIZE) * TWO_PI - Math.PI;
}

/** Convert (lat, lon) in radians to a unit-sphere point. */
function latLonToXYZ(lat: number, lon: number): [number, number, number] {
  const cosLat = Math.cos(lat);
  return [cosLat * Math.cos(lon), cosLat * Math.sin(lon), Math.sin(lat)];
}

/** Great-circle distance on a unit sphere (radians). */
function greatCircleDist(
  lat1: number, lon1: number,
  lat2: number, lon2: number,
): number {
  const dLat = lat2 - lat1;
  const dLon = lon2 - lon1;
  const a =
    Math.sin(dLat / 2) ** 2 +
    Math.cos(lat1) * Math.cos(lat2) * Math.sin(dLon / 2) ** 2;
  return 2 * Math.asin(Math.min(1, Math.sqrt(a)));
}

// ─── Result type ────────────────────────────────────────────────────────────

export interface PlanetGeneratorResult {
  plates: PlateInfo[];
  hotspots: HotspotInfo[];
  atmosphere: AtmosphericComposition;
  seed: number;
}

// ─── Generator ──────────────────────────────────────────────────────────────

export class PlanetGenerator {
  private readonly seed: number;

  constructor(seed: number) {
    this.seed = seed;
  }

  generate(stateViews: StateBufferViews): PlanetGeneratorResult {
    const rng = new Xoshiro256ss(this.seed);
    const noise = new SimplexNoise(rng);

    // 1 — Generate tectonic plates via Voronoi on the sphere.
    const numPlates = rng.nextInt(10, 16);
    const plates = this.generatePlates(rng, numPlates, stateViews);

    // 2 — Generate heightfield from simplex FBM on the sphere.
    this.generateHeightMap(noise, stateViews);

    // 3 — Adjust sea level to target ~70 % ocean coverage.
    const seaLevel = this.findSeaLevel(stateViews.heightMap, 0.70);
    this.normaliseHeightMap(stateViews.heightMap, seaLevel);

    // 4 — Classify plates as oceanic / continental and set crust properties.
    this.classifyPlatesAndCrust(plates, stateViews, rng);

    // 5 — Seed mantle-plume hotspots.
    const hotspots = this.generateHotspots(rng);

    // 6 — Baseline atmospheric composition.
    const atmosphere: AtmosphericComposition = {
      n2: 0.78,
      o2: 0.21,
      co2: 0.0004,
      h2o: 0.01,
    };

    return { plates, hotspots, atmosphere, seed: this.seed };
  }

  // ── Plate generation (Voronoi + Lloyd relaxation) ───────────────────────

  private generatePlates(
    rng: Xoshiro256ss,
    numPlates: number,
    stateViews: StateBufferViews,
  ): PlateInfo[] {
    // Random initial plate centres on the sphere.
    const centersLat = new Float64Array(numPlates);
    const centersLon = new Float64Array(numPlates);
    for (let p = 0; p < numPlates; p++) {
      centersLat[p] = Math.asin(rng.nextFloat(-1, 1));
      centersLon[p] = rng.nextFloat(-Math.PI, Math.PI);
    }

    const { plateMap } = stateViews;
    const cellCount = GRID_SIZE * GRID_SIZE;

    // Lloyd relaxation — 3 iterations.
    for (let iter = 0; iter < 3; iter++) {
      // Assign each grid cell to the nearest plate centre.
      for (let row = 0; row < GRID_SIZE; row++) {
        const lat = rowToLat(row);
        for (let col = 0; col < GRID_SIZE; col++) {
          const lon = colToLon(col);
          let bestPlate = 0;
          let bestDist = Infinity;
          for (let p = 0; p < numPlates; p++) {
            const d = greatCircleDist(lat, lon, centersLat[p], centersLon[p]);
            if (d < bestDist) { bestDist = d; bestPlate = p; }
          }
          plateMap[row * GRID_SIZE + col] = bestPlate;
        }
      }

      // Recompute centroids (mean of Cartesian positions, reprojected).
      const sumX = new Float64Array(numPlates);
      const sumY = new Float64Array(numPlates);
      const sumZ = new Float64Array(numPlates);
      const count = new Float64Array(numPlates);

      for (let row = 0; row < GRID_SIZE; row++) {
        const lat = rowToLat(row);
        for (let col = 0; col < GRID_SIZE; col++) {
          const lon = colToLon(col);
          const p = plateMap[row * GRID_SIZE + col];
          const [cx, cy, cz] = latLonToXYZ(lat, lon);
          sumX[p] += cx;
          sumY[p] += cy;
          sumZ[p] += cz;
          count[p]++;
        }
      }

      for (let p = 0; p < numPlates; p++) {
        if (count[p] === 0) continue;
        const mx = sumX[p] / count[p];
        const my = sumY[p] / count[p];
        const mz = sumZ[p] / count[p];
        const r = Math.sqrt(mx * mx + my * my + mz * mz);
        if (r < 1e-12) continue; // degenerate — keep previous centre
        centersLat[p] = Math.asin(Math.max(-1, Math.min(1, mz / r)));
        centersLon[p] = Math.atan2(my, mx);
      }
    }

    // Final plate assignment (after last relaxation).
    for (let row = 0; row < GRID_SIZE; row++) {
      const lat = rowToLat(row);
      for (let col = 0; col < GRID_SIZE; col++) {
        const lon = colToLon(col);
        let bestPlate = 0;
        let bestDist = Infinity;
        for (let p = 0; p < numPlates; p++) {
          const d = greatCircleDist(lat, lon, centersLat[p], centersLon[p]);
          if (d < bestDist) { bestDist = d; bestPlate = p; }
        }
        plateMap[row * GRID_SIZE + col] = bestPlate;
      }
    }

    // Compute plate areas and build PlateInfo array.
    const areaCount = new Float64Array(numPlates);
    for (let i = 0; i < cellCount; i++) areaCount[plateMap[i]]++;

    const plates: PlateInfo[] = [];
    for (let p = 0; p < numPlates; p++) {
      plates.push({
        id: p,
        centerLat: centersLat[p] / DEG2RAD, // store in degrees
        centerLon: centersLon[p] / DEG2RAD,
        angularVelocity: {
          lat: rng.nextFloat(-1, 1),
          lon: rng.nextFloat(-1, 1),
          rate: rng.nextFloat(0.5, 4), // cm/yr equivalent
        },
        isOceanic: false, // will be classified later
        area: areaCount[p] / cellCount,
      });
    }

    return plates;
  }

  // ── Height map generation ─────────────────────────────────────────────

  private generateHeightMap(
    noise: SimplexNoise,
    stateViews: StateBufferViews,
  ): void {
    const { heightMap } = stateViews;
    const scale = 3.0; // noise frequency

    for (let row = 0; row < GRID_SIZE; row++) {
      const lat = rowToLat(row);
      for (let col = 0; col < GRID_SIZE; col++) {
        const lon = colToLon(col);
        const [nx, ny, nz] = latLonToXYZ(lat, lon);
        heightMap[row * GRID_SIZE + col] = noise.fbm(
          nx * scale,
          ny * scale,
          nz * scale,
          4, // octaves
        );
      }
    }
  }

  // ── Sea-level finder ──────────────────────────────────────────────────

  /**
   * Binary-search for a height threshold so that `targetFraction` of cells
   * are at or below it (ocean).
   */
  private findSeaLevel(heightMap: Float32Array, targetFraction: number): number {
    let lo = -1;
    let hi = 1;
    const total = heightMap.length;

    for (let i = 0; i < 32; i++) {
      const mid = (lo + hi) / 2;
      let belowCount = 0;
      for (let j = 0; j < total; j++) {
        if (heightMap[j] <= mid) belowCount++;
      }
      if (belowCount / total < targetFraction) lo = mid;
      else hi = mid;
    }

    return (lo + hi) / 2;
  }

  /**
   * Shift and rescale the heightmap so that sea-level maps to 0.
   * Ocean cells get negative values, land cells positive.
   */
  private normaliseHeightMap(heightMap: Float32Array, seaLevel: number): void {
    for (let i = 0; i < heightMap.length; i++) {
      heightMap[i] -= seaLevel;
    }
  }

  // ── Plate classification & crust properties ───────────────────────────

  private classifyPlatesAndCrust(
    plates: PlateInfo[],
    stateViews: StateBufferViews,
    rng: Xoshiro256ss,
  ): void {
    const {
      heightMap, plateMap, crustThicknessMap,
      rockTypeMap, rockAgeMap,
    } = stateViews;
    const cellCount = GRID_SIZE * GRID_SIZE;

    // Determine each plate's mean height to decide if it's oceanic.
    const sumH = new Float64Array(plates.length);
    const countH = new Float64Array(plates.length);
    for (let i = 0; i < cellCount; i++) {
      sumH[plateMap[i]] += heightMap[i];
      countH[plateMap[i]]++;
    }
    for (const plate of plates) {
      plate.isOceanic =
        countH[plate.id] > 0 && sumH[plate.id] / countH[plate.id] < 0;
    }

    // Assign crust thickness, rock type, and rock age per cell.
    for (let i = 0; i < cellCount; i++) {
      const plate = plates[plateMap[i]];
      if (plate.isOceanic) {
        crustThicknessMap[i] = 7;
        rockTypeMap[i] = RockType.IGN_BASALT;
      } else {
        crustThicknessMap[i] = 35;
        rockTypeMap[i] = RockType.IGN_GRANITE;
      }
      rockAgeMap[i] = rng.nextFloat(100, 4000); // Ma
    }
  }

  // ── Hotspot generation ────────────────────────────────────────────────

  private generateHotspots(rng: Xoshiro256ss): HotspotInfo[] {
    const count = rng.nextInt(2, 5);
    const hotspots: HotspotInfo[] = [];
    for (let i = 0; i < count; i++) {
      hotspots.push({
        lat: Math.asin(rng.nextFloat(-1, 1)) / DEG2RAD,
        lon: rng.nextFloat(-180, 180),
        strength: rng.nextFloat(0.5, 1),
      });
    }
    return hotspots;
  }
}
