// ─── Climate Engine ─────────────────────────────────────────────────────────
// General circulation model + ice-age forcing for Phase 4.
// Computes solar insolation, 3-cell circulation (Hadley/Ferrel/Polar),
// Milankovitch cycles, greenhouse forcing, and ice-albedo feedback.

import type { StateBufferViews, AtmosphericComposition } from '../shared/types';
import { GRID_SIZE } from '../shared/types';
import type { Xoshiro256ss } from '../proc/prng';

// ─── Physical Constants ──────────────────────────────────────────────────────

/** Solar constant W/m². */
const S0 = 1361;
/** Climate sensitivity (°C per CO₂ doubling). */
const LAMBDA = 3.0;
/** Pre-industrial reference CO₂ (ppm). */
const CO2_REF = 280;
/** Lapse rate °C per km. */
const LAPSE_RATE = 6.5;

const ALBEDO_OCEAN = 0.06;
const ALBEDO_LAND = 0.30;
const ALBEDO_ICE = 0.85;

/** Equatorial temperature threshold for Snowball Earth trigger (°C). */
const SNOWBALL_THRESHOLD = -10;

// ─── Types ──────────────────────────────────────────────────────────────────

export interface ClimateResult {
  meanTemperature: number;
  equatorialTemperature: number;
  co2Ppm: number;
  iceAlbedoFeedback: number;
  snowballTriggered: boolean;
  iceCells: number;
}

// ─── ClimateEngine ──────────────────────────────────────────────────────────

export class ClimateEngine {
  private readonly gridSize: number;

  constructor(gridSize: number = GRID_SIZE) {
    this.gridSize = gridSize;
  }

  /**
   * Run one climate tick.
   * Updates temperatureMap, windUMap, windVMap based on solar forcing,
   * Milankovitch cycles, greenhouse gas concentration, and ice-albedo feedback.
   */
  tick(
    timeMa: number,
    deltaMa: number,
    stateViews: StateBufferViews,
    atmosphere: AtmosphericComposition,
    _rng: Xoshiro256ss,
  ): ClimateResult {
    const { heightMap, temperatureMap, windUMap, windVMap } = stateViews;
    const gs = this.gridSize;
    const cellCount = gs * gs;

    // Convert CO₂ fraction → ppm
    const co2Ppm = atmosphere.co2 * 1_000_000;

    // Greenhouse forcing: ΔT = λ · ln(co2 / co2_ref) / ln(2)
    const dT_ghg = co2Ppm > 0
      ? LAMBDA * Math.log(co2Ppm / CO2_REF) / Math.LN2
      : 0;

    // Milankovitch eccentricity cycle (simplified, 100 kyr period)
    const dT_milan = 2 * Math.sin((timeMa * 2 * Math.PI) / 100);

    let tempSum = 0;
    let equatorialTempSum = 0;
    let equatorialCount = 0;
    let iceCells = 0;

    // First pass: compute base temperature at each cell
    for (let row = 0; row < gs; row++) {
      // Latitude: row 0 = north pole (+90°), row gs-1 = south pole (-90°)
      const latDeg = 90 - (row / (gs - 1)) * 180;
      const latRad = (latDeg * Math.PI) / 180;

      for (let col = 0; col < gs; col++) {
        const i = row * gs + col;
        const h = heightMap[i];

        // Determine surface albedo
        const isIce = temperatureMap[i] < -5;
        const albedo = isIce ? ALBEDO_ICE : (h < 0 ? ALBEDO_OCEAN : ALBEDO_LAND);
        if (isIce) iceCells++;

        // Solar insolation: I = S0 · cos(lat) · (1 - albedo)
        const insolation = S0 * Math.max(0, Math.cos(latRad)) * (1 - albedo);

        // Base temperature from latitude
        const T_base = 30 * Math.cos(latRad);

        // Altitude lapse rate correction (height in km)
        const height_km = Math.max(0, h / 1000);
        const T_alt = T_base - height_km * LAPSE_RATE;

        // Final temperature with all forcings
        const T_final = T_alt + dT_ghg + dT_milan;

        // Smooth toward new value (relaxation to avoid discontinuities)
        const alpha = Math.min(1, deltaMa * 0.5);
        temperatureMap[i] = temperatureMap[i] * (1 - alpha) + T_final * alpha;

        tempSum += temperatureMap[i];

        // Track equatorial cells (|lat| < 10°)
        if (Math.abs(latDeg) < 10) {
          equatorialTempSum += temperatureMap[i];
          equatorialCount++;
        }
      }
    }

    // Second pass: compute 3-cell circulation winds
    for (let row = 0; row < gs; row++) {
      const latDeg = 90 - (row / (gs - 1)) * 180;
      const absLat = Math.abs(latDeg);
      const signLat = latDeg >= 0 ? 1 : -1;

      for (let col = 0; col < gs; col++) {
        const i = row * gs + col;

        let u: number;
        let v: number;

        if (absLat <= 30) {
          // Hadley cell: surface trade winds (easterlies, westward = negative u)
          u = -Math.cos((absLat * Math.PI) / 30);
          // Meridional component: converging toward ITCZ
          v = signLat * -0.3 * Math.sin((absLat * Math.PI) / 30);
        } else if (absLat <= 60) {
          // Ferrel cell: surface westerlies (positive u)
          u = Math.cos(((absLat - 45) * Math.PI) / 30);
          v = signLat * 0.1 * Math.cos(((absLat - 45) * Math.PI) / 30);
        } else {
          // Polar cell: surface easterlies (negative u)
          u = -Math.cos(((absLat - 75) * Math.PI) / 15);
          v = signLat * -0.2 * Math.sin(((absLat - 75) * Math.PI) / 15);
        }

        windUMap[i] = u;
        windVMap[i] = v;
      }
    }

    const meanTemperature = tempSum / cellCount;
    const equatorialTemperature = equatorialCount > 0
      ? equatorialTempSum / equatorialCount
      : meanTemperature;

    // Ice-albedo feedback factor: fraction of cells that are ice
    const iceAlbedoFeedback = iceCells / cellCount;

    // Snowball Earth: equatorial temp below threshold
    const snowballTriggered = equatorialTemperature < SNOWBALL_THRESHOLD;

    return {
      meanTemperature,
      equatorialTemperature,
      co2Ppm,
      iceAlbedoFeedback,
      snowballTriggered,
      iceCells,
    };
  }
}
