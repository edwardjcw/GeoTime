// ─── Weather Engine ─────────────────────────────────────────────────────────
// Models frontal systems, cyclones, tropical cyclones, orographic precipitation,
// and cloud generation for Phase 4.

import type { StateBufferViews } from '../shared/types';
import { GRID_SIZE, CloudGenus } from '../shared/types';
import type { Xoshiro256ss } from '../proc/prng';

// ─── Constants ──────────────────────────────────────────────────────────────

/** SST threshold for tropical cyclone genesis (°C). */
const CYCLONE_SST_THRESHOLD = 26;
/** Latitude range for tropical cyclone spawning (degrees from equator). */
const CYCLONE_LAT_MIN = 5;
const CYCLONE_LAT_MAX = 20;
/** Base precipitation from frontal lifting (mm/Ma equivalent). */
const FRONTAL_PRECIP_RATE = 500;
/** Orographic multiplier on windward side. */
const OROGRAPHIC_WINDWARD_MULT = 2.0;
/** Orographic multiplier on leeward side. */
const OROGRAPHIC_LEEWARD_MULT = 0.5;
/** Height threshold for orographic lifting (m). */
const OROGRAPHIC_HEIGHT_THRESHOLD = 500;

// ─── Types ──────────────────────────────────────────────────────────────────

export interface TropicalCyclone {
  lat: number;
  lon: number;
  intensity: number; // 1-5
}

export interface WeatherResult {
  frontCount: number;
  cycloneCount: number;
  tropicalCyclones: TropicalCyclone[];
  precipCells: number;
}

// ─── WeatherEngine ──────────────────────────────────────────────────────────

export class WeatherEngine {
  private readonly gridSize: number;

  constructor(gridSize: number = GRID_SIZE) {
    this.gridSize = gridSize;
  }

  /**
   * Run one weather tick.
   * Updates cloudTypeMap, cloudCoverMap, precipitationMap.
   */
  tick(
    _timeMa: number,
    deltaMa: number,
    stateViews: StateBufferViews,
    rng: Xoshiro256ss,
  ): WeatherResult {
    const {
      heightMap,
      temperatureMap,
      precipitationMap,
      windUMap,
      cloudTypeMap,
      cloudCoverMap,
    } = stateViews;
    const gs = this.gridSize;
    const cellCount = gs * gs;

    let frontCount = 0;
    let cycloneCount = 0;
    const tropicalCyclones: TropicalCyclone[] = [];
    let precipCells = 0;

    for (let row = 0; row < gs; row++) {
      const latDeg = 90 - (row / (gs - 1)) * 180;
      const absLat = Math.abs(latDeg);

      for (let col = 0; col < gs; col++) {
        const i = row * gs + col;
        const h = heightMap[i];
        const temp = temperatureMap[i];
        const windU = windUMap[i];

        // ── Moisture availability ─────────────────────────────────────────
        // Higher near ocean surfaces and warm temperatures
        const isOcean = h < 0;
        const moistureBase = isOcean ? 1.0 : 0.4;
        const tempFactor = Math.max(0, (temp + 10) / 50); // 0 at -10°C, 1 at 40°C
        const moisture = moistureBase * tempFactor;

        // ── Orographic precipitation ──────────────────────────────────────
        let orographicMult = 1.0;
        if (h > OROGRAPHIC_HEIGHT_THRESHOLD) {
          // Determine if windward or leeward based on wind direction and slope
          const neighborCol = col + (windU > 0 ? -1 : 1);
          const wrappedCol = (neighborCol + gs) % gs;
          const neighborH = heightMap[row * gs + wrappedCol];
          if (h > neighborH) {
            // Windward slope: upslope lifting enhances precipitation
            orographicMult = OROGRAPHIC_WINDWARD_MULT * (1 + (h - neighborH) / 2000);
          } else {
            // Leeward (rain shadow)
            orographicMult = OROGRAPHIC_LEEWARD_MULT;
          }
        }

        // ── Frontal precipitation ─────────────────────────────────────────
        // Fronts form at polar front (~60° lat) and along ITCZ
        const atPolarFront = absLat > 55 && absLat < 65;
        const atITCZ = absLat < 10;
        let frontalPrecip = 0;

        if (atPolarFront) {
          frontalPrecip = FRONTAL_PRECIP_RATE * moisture * rng.nextFloat(0.5, 1.5);
          frontCount++;
        } else if (atITCZ) {
          frontalPrecip = FRONTAL_PRECIP_RATE * moisture * 1.5 * rng.nextFloat(0.7, 1.3);
          frontCount++;
        }

        // ── Cyclonic precipitation ────────────────────────────────────────
        // Stochastic low-pressure systems in mid-latitudes
        let cyclonicPrecip = 0;
        if (absLat > 30 && absLat < 60 && rng.nextFloat(0, 1) < 0.002 * deltaMa) {
          cyclonicPrecip = FRONTAL_PRECIP_RATE * moisture * rng.nextFloat(1, 3);
          cycloneCount++;
        }

        // ── Tropical cyclone genesis ──────────────────────────────────────
        if (
          isOcean &&
          temp > CYCLONE_SST_THRESHOLD &&
          absLat > CYCLONE_LAT_MIN &&
          absLat < CYCLONE_LAT_MAX &&
          rng.nextFloat(0, 1) < 0.0005 * deltaMa
        ) {
          const lon = (col / gs) * 360 - 180;
          const intensity = Math.min(5, Math.max(1, Math.floor((temp - CYCLONE_SST_THRESHOLD) / 2) + 1));
          tropicalCyclones.push({ lat: latDeg, lon, intensity });

          // Tropical cyclones bring intense precipitation
          cyclonicPrecip += FRONTAL_PRECIP_RATE * 4 * moisture;
        }

        // ── Total precipitation ───────────────────────────────────────────
        const totalPrecip = (frontalPrecip + cyclonicPrecip) * orographicMult * deltaMa;
        precipitationMap[i] = Math.max(0, precipitationMap[i] * 0.9 + totalPrecip * 0.1);

        if (precipitationMap[i] > 10) precipCells++;

        // ── Cloud type assignment ─────────────────────────────────────────
        cloudTypeMap[i] = this.assignCloudType(temp, precipitationMap[i], moisture, absLat);
        cloudCoverMap[i] = Math.min(1, Math.max(0, moisture * 0.7 + precipitationMap[i] / 5000));
      }
    }

    return {
      frontCount,
      cycloneCount: cycloneCount + tropicalCyclones.length,
      tropicalCyclones,
      precipCells,
    };
  }

  /**
   * Determine dominant cloud genus for a cell.
   */
  private assignCloudType(
    temp: number,
    precip: number,
    moisture: number,
    absLat: number,
  ): number {
    if (moisture < 0.1 && precip < 50) return CloudGenus.NONE;

    // High altitude cirrus in tropical upper troposphere
    if (absLat < 20 && temp > 20) return CloudGenus.CIRRUS;

    // Deep convection in ITCZ and frontal zones
    if (precip > 400 && temp > 10) return CloudGenus.CUMULONIMBUS;

    // Stratiform precipitation (nimbostratus) in frontal bands
    if (precip > 200) return CloudGenus.NIMBOSTRATUS;

    // Cumulus from surface convection
    if (temp > 15 && moisture > 0.5) return CloudGenus.CUMULUS;

    // Low stratus over cold ocean
    if (temp < 10 && moisture > 0.4) return CloudGenus.STRATUS;

    // Stratocumulus in mid-latitudes
    if (absLat > 30 && absLat < 60 && moisture > 0.3) return CloudGenus.STRATOCUMULUS;

    // Altostratus in spreading frontal systems
    if (precip > 50) return CloudGenus.ALTOSTRATUS;

    return CloudGenus.CIRROSTRATUS;
  }
}
