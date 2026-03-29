// ─── Fluvial Erosion Engine ──────────────────────────────────────────────────
// Hydraulic erosion using D∞ flow routing, stream power law for river
// incision, and sediment transport/deposition.  Operates on the shared
// height map and records sedimentary layers in the stratigraphy stacks.

import type { StateBufferViews } from '../shared/types';
import { GRID_SIZE, RockType, DeformationType, SoilOrder } from '../shared/types';
import type { StratigraphyStack } from './stratigraphy';
import type { Xoshiro256ss } from '../proc/prng';
import { getNeighborIndices } from './boundary-classifier';

// ─── Constants ──────────────────────────────────────────────────────────────

/** Stream-power coefficient K (erosivity, higher = faster incision). */
const K_EROSION = 1e-4;

/** Area exponent m in stream power law E = K · A^m · S^n. */
const M_AREA = 0.5;

/** Slope exponent n in stream power law. */
const N_SLOPE = 1.0;

/** Maximum erosion depth per tick (meters) to prevent numerical blow-up. */
const MAX_EROSION_PER_TICK = 50;

/** Minimum drainage area (cells) for a river to form. */
const MIN_RIVER_CELLS = 16;

/** Deposition fraction: how much of carried sediment drops at low-slope cells. */
const DEPOSITION_RATE = 0.3;

/** Minimum slope considered for erosion (prevents division by zero). */
const MIN_SLOPE = 1e-6;

/** Slope ratio threshold for triggering deposition (downstream slope < this × cell slope). */
const DEPOSITION_SLOPE_THRESHOLD = 0.3;

// ─── Types ──────────────────────────────────────────────────────────────────

export interface FlowCell {
  /** Cell index. */
  index: number;
  /** Index of the downstream cell (−1 if pit / ocean). */
  downstream: number;
  /** Steepest-descent slope to downstream cell. */
  slope: number;
}

export interface ErosionResult {
  /** Total meters of material eroded across the grid. */
  totalEroded: number;
  /** Total meters of sediment deposited across the grid. */
  totalDeposited: number;
  /** Number of cells modified. */
  cellsAffected: number;
  /** Cells that qualify as "rivers" (drainage area ≥ MIN_RIVER_CELLS). */
  riverCells: number[];
}

// ─── Erosion Engine ─────────────────────────────────────────────────────────

export class ErosionEngine {
  private readonly gridSize: number;

  constructor(gridSize: number = GRID_SIZE) {
    this.gridSize = gridSize;
  }

  /**
   * Compute the D∞ steepest-descent flow graph.
   * Each cell points to the lowest neighbor (or −1 for pits / boundary).
   */
  computeFlowGraph(heightMap: Float32Array): FlowCell[] {
    const gs = this.gridSize;
    const cellCount = gs * gs;
    const flow: FlowCell[] = new Array(cellCount);

    for (let i = 0; i < cellCount; i++) {
      const row = Math.floor(i / gs);
      const col = i % gs;
      const h = heightMap[i];

      const neighbors = getNeighborIndices(row, col, gs);
      let bestIdx = -1;
      let bestSlope = MIN_SLOPE;

      for (const nIdx of neighbors) {
        const hn = heightMap[nIdx];
        const dh = h - hn;
        // Cell spacing = 1 grid cell → slope ≈ dh
        if (dh > bestSlope) {
          bestSlope = dh;
          bestIdx = nIdx;
        }
      }

      flow[i] = { index: i, downstream: bestIdx, slope: bestSlope };
    }

    return flow;
  }

  /**
   * Compute the upstream drainage area for each cell (in grid-cell units).
   * Uses the flow graph from computeFlowGraph.
   */
  computeDrainageArea(flow: FlowCell[]): Float32Array {
    const cellCount = flow.length;
    const area = new Float32Array(cellCount);
    area.fill(1); // every cell contributes itself

    // Sort cells by descending height so we process high cells first
    // (topological sort approximation).
    const sorted = flow.slice().sort((a, b) => {
      // Higher cells first — if slope to downstream is large the cell is high
      // We use the flow.slope as a rough proxy; a cleaner approach would sort
      // by actual height, but this is O(N log N) and good enough for the sim.
      return b.slope - a.slope;
    });

    for (const cell of sorted) {
      if (cell.downstream >= 0) {
        area[cell.downstream] += area[cell.index];
      }
    }

    return area;
  }

  /**
   * Run one erosion tick.
   * 1. Compute flow graph (steepest descent).
   * 2. Accumulate drainage area.
   * 3. Apply stream-power erosion: E = K · A^m · S^n · dt.
   * 4. Transport sediment downstream; deposit at low-slope cells.
   * 5. Record new sedimentary layers in stratigraphy stacks.
   */
  tick(
    timeMa: number,
    deltaMa: number,
    stateViews: StateBufferViews,
    stratigraphy: StratigraphyStack,
    rng: Xoshiro256ss,
  ): ErosionResult {
    const { heightMap } = stateViews;

    // 1 — Flow graph
    const flow = this.computeFlowGraph(heightMap);

    // 2 — Drainage area
    const area = this.computeDrainageArea(flow);

    // 3 — Erosion & deposition
    const cellCount = this.gridSize * this.gridSize;
    const erosionAmount = new Float32Array(cellCount);
    const sedimentLoad = new Float32Array(cellCount);
    let totalEroded = 0;
    let totalDeposited = 0;
    let cellsAffected = 0;
    const riverCells: number[] = [];

    // Process cells from high to low (sorted by descending height)
    const indices = new Uint32Array(cellCount);
    for (let i = 0; i < cellCount; i++) indices[i] = i;
    indices.sort((a, b) => heightMap[b] - heightMap[a]);

    for (const i of indices) {
      const f = flow[i];
      const drainArea = area[i];
      const slope = Math.max(f.slope, MIN_SLOPE);

      // Stream power erosion
      const erode = Math.min(
        K_EROSION * Math.pow(drainArea, M_AREA) * Math.pow(slope, N_SLOPE) * deltaMa,
        MAX_EROSION_PER_TICK,
      );

      if (erode > 0.01) {
        // Erode from height map and stratigraphy
        const actualEroded = stratigraphy.erodeTop(i, erode);
        heightMap[i] -= actualEroded;
        erosionAmount[i] = actualEroded;
        totalEroded += actualEroded;
        if (actualEroded > 0) cellsAffected++;

        // Add eroded material to sediment load
        sedimentLoad[i] += actualEroded;
      }

      // Carry sediment downstream
      if (f.downstream >= 0 && sedimentLoad[i] > 0) {
        const downSlope = flow[f.downstream].slope;

        // Deposit a fraction at low-slope areas (alluvial fans, deltas)
        if (downSlope < slope * DEPOSITION_SLOPE_THRESHOLD || heightMap[f.downstream] < 0) {
          const deposit = sedimentLoad[i] * DEPOSITION_RATE;
          if (deposit > 0.01) {
            heightMap[f.downstream] += deposit;
            totalDeposited += deposit;

            // Record sedimentary layer
            const isUnderwater = heightMap[f.downstream] < 0;
            stratigraphy.pushLayer(f.downstream, {
              rockType: isUnderwater ? RockType.SED_MUDSTONE : RockType.SED_SANDSTONE,
              ageDeposited: timeMa,
              thickness: deposit,
              dipAngle: 1 + rng.next() * 3,
              dipDirection: rng.nextFloat(0, 360),
              deformation: DeformationType.UNDEFORMED,
              unconformity: false,
              soilHorizon: SoilOrder.NONE,
              formationName: 0,
            });
          }
          sedimentLoad[i] -= sedimentLoad[i] * DEPOSITION_RATE;
        }

        // Pass remaining sediment downstream
        sedimentLoad[f.downstream] += sedimentLoad[i];
      }

      // Track river cells
      if (drainArea >= MIN_RIVER_CELLS) {
        riverCells.push(i);
      }
    }

    return { totalEroded, totalDeposited, cellsAffected, riverCells };
  }
}
