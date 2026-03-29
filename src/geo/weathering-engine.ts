// ─── Weathering Engine ──────────────────────────────────────────────────────
// Models aeolian processes (wind erosion & loess deposition) and chemical
// weathering (dissolution, karst, laterite, regolith production).
// Each weathered product is appended as a new StratigraphicLayer.

import type { StateBufferViews } from '../shared/types';
import { GRID_SIZE, RockType, DeformationType, SoilOrder } from '../shared/types';
import type { StratigraphyStack } from './stratigraphy';
import type { Xoshiro256ss } from '../proc/prng';

// ─── Constants ──────────────────────────────────────────────────────────────

/** Base chemical weathering rate (meters per Ma). */
const CHEM_WEATHERING_BASE = 0.005;

/** Temperature coefficient — weathering doubles per 10°C above 15°C. */
const CHEM_TEMP_FACTOR = 0.07;

/** Precipitation coefficient — wetter → faster dissolution. */
const CHEM_PRECIP_FACTOR = 0.001;

/** Minimum temperature for significant chemical weathering (°C). */
const CHEM_MIN_TEMP = -10;

/** Wind erosion threshold — minimum wind speed² for aeolian transport. */
const WIND_EROSION_THRESHOLD = 4;

/** Aeolian erosion rate (meters per Ma per unit of excess wind). */
const AEOLIAN_EROSION_RATE = 0.01;

/** Loess deposition rate (meters per Ma) downwind of eroding areas. */
const LOESS_DEPOSITION_RATE = 0.005;

/** Maximum weathering depth per tick (meters). */
const MAX_WEATHERING_PER_TICK = 10;

/** Precipitation threshold for arid classification (mm/yr). */
const ARID_PRECIP_THRESHOLD = 250;

/** Tropical temperature threshold (°C). */
const TROPICAL_TEMP_THRESHOLD = 20;

// ─── Types ──────────────────────────────────────────────────────────────────

export interface WeatheringResult {
  /** Total chemical weathering (meters). */
  chemicalWeathered: number;
  /** Total aeolian erosion (meters). */
  aeolianEroded: number;
  /** Total deposition — loess, laterite, regolith (meters). */
  totalDeposited: number;
  /** Number of cells affected. */
  cellsAffected: number;
}

// ─── WeatheringEngine ───────────────────────────────────────────────────────

export class WeatheringEngine {
  private readonly gridSize: number;

  constructor(gridSize: number = GRID_SIZE) {
    this.gridSize = gridSize;
  }

  /**
   * Determine the weathering product based on parent rock and climate.
   */
  getWeatheringProduct(
    parentRock: RockType,
    temperature: number,
    precipitation: number,
  ): RockType {
    // Tropical + wet → laterite
    if (temperature > TROPICAL_TEMP_THRESHOLD && precipitation > 1000) {
      return RockType.SED_LATERITE;
    }

    // Arid → caliche (calcium carbonate crust)
    if (precipitation < ARID_PRECIP_THRESHOLD) {
      return RockType.SED_CALICHE;
    }

    // Carbonate rock in wet climate → dissolved (karst) — produces regolith residue
    if (
      parentRock === RockType.SED_LIMESTONE ||
      parentRock === RockType.SED_DOLOSTONE ||
      parentRock === RockType.SED_CHALK
    ) {
      return RockType.SED_REGOLITH;
    }

    // General case — regolith
    return RockType.SED_REGOLITH;
  }

  /**
   * Compute chemical weathering rate for a cell (meters / Ma).
   */
  chemicalWeatheringRate(temperature: number, precipitation: number): number {
    if (temperature < CHEM_MIN_TEMP) return 0;

    // Arrhenius-inspired: rate increases with temperature and moisture
    const tempFactor = Math.exp(CHEM_TEMP_FACTOR * (temperature - 15));
    const precipFactor = 1 + precipitation * CHEM_PRECIP_FACTOR;
    return CHEM_WEATHERING_BASE * tempFactor * precipFactor;
  }

  /**
   * Run one weathering tick.
   *
   * 1. Chemical weathering on exposed land surfaces.
   * 2. Aeolian erosion in arid, windy areas.
   * 3. Loess deposition downwind of aeolian erosion.
   * 4. Record weathered products as new stratigraphic layers.
   */
  tick(
    timeMa: number,
    deltaMa: number,
    stateViews: StateBufferViews,
    stratigraphy: StratigraphyStack,
    rng: Xoshiro256ss,
  ): WeatheringResult {
    const { heightMap, temperatureMap, precipitationMap, windUMap, windVMap } = stateViews;
    const gs = this.gridSize;
    const cellCount = gs * gs;

    let chemicalWeathered = 0;
    let aeolianEroded = 0;
    let totalDeposited = 0;
    let cellsAffected = 0;

    for (let i = 0; i < cellCount; i++) {
      // Only process land cells above sea level
      if (heightMap[i] <= 0) continue;

      const temp = temperatureMap[i];
      const precip = precipitationMap[i];
      const windU = windUMap[i];
      const windV = windVMap[i];
      const windSpeed2 = windU * windU + windV * windV;

      // ── Chemical weathering ───────────────────────────────────────────
      const chemRate = this.chemicalWeatheringRate(temp, precip);
      const chemAmount = Math.min(chemRate * deltaMa, MAX_WEATHERING_PER_TICK);

      if (chemAmount > 0.001) {
        const topLayer = stratigraphy.getTopLayer(i);
        const parentRock = topLayer?.rockType ?? RockType.IGN_GRANITE;
        const product = this.getWeatheringProduct(parentRock, temp, precip);

        // Erode the parent rock
        const eroded = stratigraphy.erodeTop(i, chemAmount);
        if (eroded > 0) {
          // Deposit weathering product
          stratigraphy.pushLayer(i, {
            rockType: product,
            ageDeposited: timeMa,
            thickness: eroded * 0.3, // weathering products are less dense
            dipAngle: 0,
            dipDirection: 0,
            deformation: DeformationType.UNDEFORMED,
            unconformity: false,
            soilHorizon: SoilOrder.NONE,
            formationName: 0,
          });

          // Net height change (weathering removes more than it deposits)
          heightMap[i] -= eroded * 0.7;
          chemicalWeathered += eroded;
          totalDeposited += eroded * 0.3;
          cellsAffected++;
        }
      }

      // ── Aeolian erosion ───────────────────────────────────────────────
      if (precip < ARID_PRECIP_THRESHOLD && windSpeed2 > WIND_EROSION_THRESHOLD) {
        const excessWind = Math.sqrt(windSpeed2) - Math.sqrt(WIND_EROSION_THRESHOLD);
        const aeolianAmount = Math.min(
          AEOLIAN_EROSION_RATE * excessWind * deltaMa,
          MAX_WEATHERING_PER_TICK,
        );

        if (aeolianAmount > 0.001) {
          const eroded = stratigraphy.erodeTop(i, aeolianAmount);
          if (eroded > 0) {
            heightMap[i] -= eroded;
            aeolianEroded += eroded;
            cellsAffected++;

            // Deposit loess downwind
            const row = Math.floor(i / gs);
            const col = i % gs;
            // Wind direction → offset
            const windMag = Math.sqrt(windSpeed2);
            const dCol = windMag > 0 ? Math.round(windU / windMag) : 0;
            const dRow = windMag > 0 ? Math.round(windV / windMag) : 0;
            const destRow = Math.max(0, Math.min(gs - 1, row + dRow));
            const destCol = (col + dCol + gs) % gs;
            const destIdx = destRow * gs + destCol;

            if (destIdx !== i) {
              const loess = Math.min(eroded * 0.5, LOESS_DEPOSITION_RATE * deltaMa);
              if (loess > 0.001) {
                heightMap[destIdx] += loess;
                totalDeposited += loess;

                stratigraphy.pushLayer(destIdx, {
                  rockType: RockType.SED_LOESS,
                  ageDeposited: timeMa,
                  thickness: loess,
                  dipAngle: 0,
                  dipDirection: 0,
                  deformation: DeformationType.UNDEFORMED,
                  unconformity: false,
                  soilHorizon: SoilOrder.NONE,
                  formationName: 0,
                });
              }
            }
          }
        }
      }
    }

    return { chemicalWeathered, aeolianEroded, totalDeposited, cellsAffected };
  }
}
