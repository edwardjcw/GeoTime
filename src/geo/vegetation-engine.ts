// ─── Vegetation Engine ──────────────────────────────────────────────────────
// Phase 6 vegetation module (feature-flagged).  Computes net primary
// productivity via the Miami Model, accumulates biomass, models forest fires,
// and feeds albedo feedback back into the climate system.

import type { StateBufferViews } from '../shared/types';
import { GRID_SIZE } from '../shared/types';
import type { EventBus } from '../kernel/event-bus';
import type { EventLog } from '../kernel/event-log';
import { Xoshiro256ss } from '../proc/prng';

// ─── Constants ──────────────────────────────────────────────────────────────

/** Maximum biomass per cell in kg/m². */
export const MAX_BIOMASS = 40;

/** Minimum precipitation (mm/yr) for grass coverage. */
export const MIN_GRASS_PRECIP = 250;

/** Minimum soil depth (m) for grass. */
export const MIN_GRASS_SOIL_DEPTH = 0.1;

/** Base fire probability per Ma per cell when conditions are met. */
export const BASE_FIRE_PROBABILITY = 0.05;

/** Precipitation threshold (mm/yr) below which fire risk increases. */
export const DRY_SEASON_PRECIP_THRESHOLD = 400;

/** Biomass threshold (kg/m²) above which fire risk increases. */
export const FIRE_BIOMASS_THRESHOLD = 10;

/** Fraction of biomass consumed in a fire. */
export const FIRE_BURN_FRACTION = 0.6;

/** Forest albedo modifier (darker than grassland → warming). */
export const FOREST_ALBEDO_MODIFIER = -0.05;

/** Grassland albedo modifier. */
export const GRASSLAND_ALBEDO_MODIFIER = 0.02;

/** Sea level threshold — cells at or below 0 m are underwater. */
const SEA_LEVEL = 0;

/** Temperature threshold (°C) below which glaciation clears biomass. */
const GLACIATION_TEMP = -10;

/** Precipitation threshold (mm/yr) below which desertification clears biomass. */
const DESERT_PRECIP = 50;

// ─── Types ──────────────────────────────────────────────────────────────────

export interface VegetationTickResult {
  /** Total biomass across all cells (kg/m²). */
  totalBiomass: number;
  /** Mean NPP across vegetated cells (g C/m²/yr). */
  meanNpp: number;
  /** Number of cells with non-zero biomass. */
  cellsWithVegetation: number;
  /** Number of fire events this tick. */
  fireCount: number;
}

export interface VegetationEngineConfig {
  /** Minimum tick interval in Ma (default 1.0). */
  minTickInterval?: number;
  /** Grid size (default GRID_SIZE). */
  gridSize?: number;
  /** Enable/disable the vegetation module (default true). */
  enabled?: boolean;
}

// ─── Miami Model NPP ───────────────────────────────────────────────────────

/**
 * Compute net primary productivity using the Miami Model approximation.
 * NPP = min(NPP_temp, NPP_precip)
 * where:
 *   NPP_temp  = 3000 / (1 + exp(1.315 - 0.119 * T))
 *   NPP_precip = 3000 * (1 - exp(-0.000664 * P))
 *
 * @param tempC  Mean annual temperature in °C
 * @param precipMm  Annual precipitation in mm/yr
 * @returns NPP in g C/m²/yr (0–3000 range)
 */
export function computeNPP(tempC: number, precipMm: number): number {
  if (precipMm <= 0) return 0;
  const nppTemp = 3000 / (1 + Math.exp(1.315 - 0.119 * tempC));
  const nppPrecip = 3000 * (1 - Math.exp(-0.000664 * precipMm));
  return Math.max(0, Math.min(nppTemp, nppPrecip));
}

/**
 * Convert NPP (g C/m²/yr) to biomass accumulation rate (kg/m²/Ma).
 * Assumes ~2x C-to-dry-biomass ratio and steady-state turnover.
 * Result is scaled so that high NPP → reasonable biomass growth over Ma.
 */
export function nppToBiomassRate(npp: number): number {
  // Scale: 1000 g C/m²/yr → 0.5 kg/m²/Ma accumulation
  return (npp / 1000) * 0.5;
}

/**
 * Compute fire probability for a cell.
 * Fire is more likely when precipitation is low (dry season) and biomass is high.
 * @returns probability in [0, 1] per Ma
 */
export function computeFireProbability(
  precipMm: number,
  biomass: number,
): number {
  if (biomass < FIRE_BIOMASS_THRESHOLD) return 0;

  let prob = BASE_FIRE_PROBABILITY;

  // Dryness factor: increases fire chance when precip < threshold
  if (precipMm < DRY_SEASON_PRECIP_THRESHOLD) {
    const dryFactor = 1 + (DRY_SEASON_PRECIP_THRESHOLD - precipMm) / DRY_SEASON_PRECIP_THRESHOLD;
    prob *= dryFactor;
  }

  // Biomass factor: more fuel = higher risk
  const biomassFactor = Math.min(biomass / MAX_BIOMASS, 1);
  prob *= (0.5 + 0.5 * biomassFactor);

  return Math.min(prob, 1);
}

/**
 * Compute albedo feedback from vegetation.
 * Forests are darker (lower albedo) than grassland/bare soil.
 * @param biomass Current biomass in kg/m²
 * @returns Albedo modifier (negative = darker = warming, positive = lighter = cooling)
 */
export function computeVegetationAlbedo(biomass: number): number {
  if (biomass <= 0) return 0;
  // Forests (high biomass) → darker, grasslands (low biomass) → slightly lighter
  const forestFraction = Math.min(biomass / 20, 1);
  return GRASSLAND_ALBEDO_MODIFIER * (1 - forestFraction) +
         FOREST_ALBEDO_MODIFIER * forestFraction;
}

// ─── VegetationEngine ───────────────────────────────────────────────────────

export class VegetationEngine {
  private readonly bus: EventBus;
  private readonly eventLog: EventLog;
  private readonly rng: Xoshiro256ss;
  private readonly gridSize: number;

  private stateViews: StateBufferViews | null = null;

  /** Whether the vegetation module is enabled (feature flag). */
  readonly enabled: boolean;

  /** Sub-tick accumulator. */
  private accumulator = 0;
  private readonly minTickInterval: number;

  constructor(
    bus: EventBus,
    eventLog: EventLog,
    seed: number,
    config?: VegetationEngineConfig,
  ) {
    this.bus = bus;
    this.eventLog = eventLog;
    this.rng = new Xoshiro256ss(seed);
    this.gridSize = config?.gridSize ?? GRID_SIZE;
    this.minTickInterval = config?.minTickInterval ?? 1.0;
    this.enabled = config?.enabled ?? true;
  }

  /**
   * Initialize with shared state views.
   */
  initialize(stateViews: StateBufferViews): void {
    this.stateViews = stateViews;
    this.accumulator = 0;
  }

  /**
   * Process one simulation tick.
   * Returns null if not enabled or not initialized.
   */
  tick(timeMa: number, deltaMa: number): VegetationTickResult | null {
    if (!this.enabled || !this.stateViews || deltaMa <= 0) return null;

    this.accumulator += deltaMa;

    let lastResult: VegetationTickResult | null = null;

    while (this.accumulator >= this.minTickInterval) {
      const subDelta = this.minTickInterval;
      this.accumulator -= subDelta;
      const subTime = timeMa - this.accumulator;
      lastResult = this.processVegetationTick(subTime, subDelta);
    }

    return lastResult;
  }

  // ── Core tick processing ──────────────────────────────────────────────

  private processVegetationTick(timeMa: number, deltaMa: number): VegetationTickResult {
    const sv = this.stateViews!;
    const cellCount = this.gridSize * this.gridSize;

    let totalBiomass = 0;
    let totalNpp = 0;
    let cellsWithVegetation = 0;
    let fireCount = 0;

    for (let i = 0; i < cellCount; i++) {
      const height = sv.heightMap[i];
      const temp = sv.temperatureMap[i];
      const precip = sv.precipitationMap[i];
      const soilDepth = sv.soilDepthMap[i];
      let biomass = sv.biomassMap[i];

      // Skip underwater cells
      if (height <= SEA_LEVEL) {
        sv.biomassMap[i] = 0;
        continue;
      }

      // Glaciation / desertification clears biomass
      if (temp < GLACIATION_TEMP || precip < DESERT_PRECIP) {
        sv.biomassMap[i] = 0;
        continue;
      }

      // Compute NPP
      const npp = computeNPP(temp, precip);

      // Grass coverage requires minimum soil depth and precipitation
      const canGrow = precip >= MIN_GRASS_PRECIP && soilDepth >= MIN_GRASS_SOIL_DEPTH;

      if (canGrow && npp > 0) {
        // Accumulate biomass
        const rate = nppToBiomassRate(npp);
        biomass = Math.min(MAX_BIOMASS, biomass + rate * deltaMa);

        totalNpp += npp;
        cellsWithVegetation++;
      }

      // Forest fire stochastic model
      const fireProb = computeFireProbability(precip, biomass);
      if (fireProb > 0 && this.rng.next() < fireProb * deltaMa) {
        const burned = biomass * FIRE_BURN_FRACTION;
        biomass -= burned;
        fireCount++;

        this.bus.emit('FOREST_FIRE', {
          cellIndex: i,
          biomassBurned: burned,
        });
      }

      sv.biomassMap[i] = biomass;
      totalBiomass += biomass;
    }

    const meanNpp = cellsWithVegetation > 0 ? totalNpp / cellsWithVegetation : 0;

    // Emit vegetation update event
    this.bus.emit('VEGETATION_UPDATE', {
      totalBiomass,
      meanNpp,
      cellsWithVegetation,
    });

    // Log significant fires
    if (fireCount > 0) {
      this.eventLog.record({
        timeMa,
        type: 'FOREST_FIRE',
        description: `${fireCount} forest fire(s) burned biomass`,
      });
    }

    return { totalBiomass, meanNpp, cellsWithVegetation, fireCount };
  }
}
