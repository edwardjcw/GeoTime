// ─── Glacial Erosion Engine ─────────────────────────────────────────────────
// Models glacial extent based on temperature (equilibrium line altitude),
// ice accumulation / ablation, glacial erosion (quarrying + abrasion), and
// moraine deposition.

import type { StateBufferViews } from '../shared/types';
import { GRID_SIZE, RockType, DeformationType, SoilOrder } from '../shared/types';
import type { StratigraphyStack } from './stratigraphy';
import type { Xoshiro256ss } from '../proc/prng';

// ─── Constants ──────────────────────────────────────────────────────────────

/** Temperature threshold (°C) below which glaciers can form. */
const GLACIATION_TEMP_THRESHOLD = -5;

/** Glacial erosion rate coefficient (meters per Ma per km of ice). */
const GLACIAL_EROSION_RATE = 0.02;

/** Maximum glacial erosion per tick (meters). */
const MAX_GLACIAL_EROSION = 30;

/** Moraine deposition thickness per tick at glacier margin (meters). */
const MORAINE_DEPOSITION_RATE = 5;

/** Ice accumulation rate (arbitrary thickness units per Ma per °C below ELA). */
const ICE_ACCUMULATION_RATE = 0.5;

/** Ice ablation rate (thickness units per Ma per °C above threshold). */
const ICE_ABLATION_RATE = 1.0;

/** Fraction of glacial erosion deposited as moraine at glacier margin. */
const MORAINE_FRACTION_OF_EROSION = 0.5;

// ─── Types ──────────────────────────────────────────────────────────────────

export interface GlacialResult {
  /** Number of cells currently glaciated. */
  glaciatedCells: number;
  /** Equilibrium line altitude (meters). */
  equilibriumLineAltitude: number;
  /** Total glacial erosion this tick (meters). */
  totalEroded: number;
  /** Total moraine deposited this tick (meters). */
  totalDeposited: number;
}

// ─── GlacialEngine ─────────────────────────────────────────────────────────

export class GlacialEngine {
  private readonly gridSize: number;

  /**
   * Per-cell ice thickness tracker (meters).
   * Positive values indicate glaciated cells.
   */
  private iceThickness: Float32Array;

  constructor(gridSize: number = GRID_SIZE) {
    this.gridSize = gridSize;
    this.iceThickness = new Float32Array(gridSize * gridSize);
  }

  /** Get current ice thickness array (read-only reference). */
  getIceThickness(): Float32Array {
    return this.iceThickness;
  }

  /** Reset ice coverage (e.g., on new planet). */
  clear(): void {
    this.iceThickness.fill(0);
  }

  /**
   * Compute equilibrium line altitude (ELA) from the mean polar temperature.
   * ELA is the altitude above which snow accumulates faster than it melts.
   * Lower temperatures → lower ELA → more glaciation.
   */
  computeELA(temperatureMap: Float32Array): number {
    const cellCount = this.gridSize * this.gridSize;
    // Average temperature of cells in polar regions (top/bottom 15% of rows)
    const polarBand = Math.floor(this.gridSize * 0.15);
    let sum = 0;
    let count = 0;

    for (let row = 0; row < polarBand; row++) {
      for (let col = 0; col < this.gridSize; col++) {
        sum += temperatureMap[row * this.gridSize + col];
        count++;
      }
    }
    for (let row = this.gridSize - polarBand; row < this.gridSize; row++) {
      for (let col = 0; col < this.gridSize; col++) {
        sum += temperatureMap[row * this.gridSize + col];
        count++;
      }
    }

    const meanPolarTemp = count > 0 ? sum / count : 0;

    // ELA approximation: lower temp → lower ELA
    // At −20°C, ELA ≈ 0 m (sea level glaciation);  at 0°C, ELA ≈ 3000 m
    return Math.max(0, 3000 + meanPolarTemp * 150);
  }

  /**
   * Run one glacial tick.
   * 1. Compute ELA from temperature.
   * 2. Accumulate / ablate ice at each cell.
   * 3. Erode under glaciers (quarrying + abrasion).
   * 4. Deposit moraines at glacier margins.
   */
  tick(
    timeMa: number,
    deltaMa: number,
    stateViews: StateBufferViews,
    stratigraphy: StratigraphyStack,
    rng: Xoshiro256ss,
  ): GlacialResult {
    const { heightMap, temperatureMap } = stateViews;
    const gs = this.gridSize;
    const cellCount = gs * gs;

    // 1 — ELA
    const ela = this.computeELA(temperatureMap);

    let glaciatedCells = 0;
    let totalEroded = 0;
    let totalDeposited = 0;

    for (let i = 0; i < cellCount; i++) {
      const h = heightMap[i];
      const temp = temperatureMap[i];

      // 2 — Ice budget: accumulate above ELA in cold areas, ablate below
      if (h > ela && temp < GLACIATION_TEMP_THRESHOLD) {
        // Accumulation
        const coldness = GLACIATION_TEMP_THRESHOLD - temp;
        this.iceThickness[i] += ICE_ACCUMULATION_RATE * coldness * deltaMa;
      } else if (this.iceThickness[i] > 0) {
        // Ablation
        const warmth = Math.max(0, temp - GLACIATION_TEMP_THRESHOLD);
        this.iceThickness[i] -= ICE_ABLATION_RATE * warmth * deltaMa;
        if (this.iceThickness[i] < 0) this.iceThickness[i] = 0;
      }

      if (this.iceThickness[i] <= 0) continue;
      glaciatedCells++;

      // 3 — Glacial erosion: proportional to ice thickness and local slope
      const row = Math.floor(i / gs);
      const col = i % gs;
      // Approximate slope from neighbors
      let maxSlope = 0;
      const neighbors = this.getNeighborIndices(row, col);
      for (const nIdx of neighbors) {
        const dh = Math.abs(heightMap[i] - heightMap[nIdx]);
        if (dh > maxSlope) maxSlope = dh;
      }

      const erosion = Math.min(
        GLACIAL_EROSION_RATE * this.iceThickness[i] * (1 + maxSlope * 0.001) * deltaMa,
        MAX_GLACIAL_EROSION,
      );

      if (erosion > 0.01) {
        const actualEroded = stratigraphy.erodeTop(i, erosion);
        heightMap[i] -= actualEroded;
        totalEroded += actualEroded;

        // 4 — Deposit moraine at glacier margin (find downhill neighbor with no ice)
        for (const nIdx of neighbors) {
          if (this.iceThickness[nIdx] <= 0 && heightMap[nIdx] < heightMap[i]) {
            const moraine = Math.min(
              MORAINE_DEPOSITION_RATE * deltaMa,
              actualEroded * MORAINE_FRACTION_OF_EROSION,
            );
            if (moraine > 0.01) {
              heightMap[nIdx] += moraine;
              totalDeposited += moraine;

              stratigraphy.pushLayer(nIdx, {
                rockType: RockType.SED_TILLITE,
                ageDeposited: timeMa,
                thickness: moraine,
                dipAngle: rng.next() * 5,
                dipDirection: rng.nextFloat(0, 360),
                deformation: DeformationType.UNDEFORMED,
                unconformity: true, // glacial unconformity
                soilHorizon: SoilOrder.NONE,
                formationName: 0,
              });
            }
            break; // deposit at first suitable neighbor
          }
        }
      }
    }

    return { glaciatedCells, equilibriumLineAltitude: ela, totalEroded, totalDeposited };
  }

  /** 4-connected neighbor indices with wrapping. */
  private getNeighborIndices(row: number, col: number): number[] {
    const gs = this.gridSize;
    const neighbors: number[] = [];
    if (row > 0) neighbors.push((row - 1) * gs + col);
    if (row < gs - 1) neighbors.push((row + 1) * gs + col);
    neighbors.push(row * gs + ((col - 1 + gs) % gs));
    neighbors.push(row * gs + ((col + 1) % gs));
    return neighbors;
  }
}
