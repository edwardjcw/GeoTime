// ─── Stratigraphy Stack ─────────────────────────────────────────────────────
// Per-cell layer stack management for the geological column.
// Each cell maintains a vertical stack of StratigraphicLayer records that
// grow as volcanic, sedimentary, or metamorphic events occur.

import type { StratigraphicLayer } from '../shared/types';
import { RockType, DeformationType, SoilOrder } from '../shared/types';

// ─── Constants ──────────────────────────────────────────────────────────────

/** Maximum layers per cell to prevent unbounded memory growth. */
export const MAX_LAYERS_PER_CELL = 64;

// ─── StratigraphyStack ─────────────────────────────────────────────────────

export class StratigraphyStack {
  /** Map from cell index to its layer stack (bottom → top). */
  private stacks: Map<number, StratigraphicLayer[]> = new Map();

  /** Get the layer stack for a cell (bottom → top ordering). */
  getLayers(cellIndex: number): ReadonlyArray<StratigraphicLayer> {
    return this.stacks.get(cellIndex) ?? [];
  }

  /** Get the topmost layer for a cell, or undefined if empty. */
  getTopLayer(cellIndex: number): StratigraphicLayer | undefined {
    const stack = this.stacks.get(cellIndex);
    return stack && stack.length > 0 ? stack[stack.length - 1] : undefined;
  }

  /** Total thickness of all layers at a cell (meters). */
  getTotalThickness(cellIndex: number): number {
    const stack = this.stacks.get(cellIndex);
    if (!stack) return 0;
    let total = 0;
    for (const layer of stack) total += layer.thickness;
    return total;
  }

  /**
   * Append a new layer to the top of a cell's stack.
   * If the stack exceeds MAX_LAYERS_PER_CELL, the oldest (bottom) layers
   * are merged to stay within budget.
   */
  pushLayer(cellIndex: number, layer: StratigraphicLayer): void {
    let stack = this.stacks.get(cellIndex);
    if (!stack) {
      stack = [];
      this.stacks.set(cellIndex, stack);
    }
    stack.push({ ...layer });

    // Merge oldest layers if over budget
    while (stack.length > MAX_LAYERS_PER_CELL) {
      const bottom = stack.shift()!;
      if (stack.length > 0) {
        stack[0].thickness += bottom.thickness;
      }
    }
  }

  /**
   * Initialize a cell with a Precambrian basement stack based on plate type.
   * Oceanic crust: pillow basalt + gabbro base.
   * Continental crust: granite + gneiss base.
   */
  initializeBasement(
    cellIndex: number,
    isOceanic: boolean,
    ageDeposited: number,
  ): void {
    const stack: StratigraphicLayer[] = [];

    if (isOceanic) {
      stack.push({
        rockType: RockType.IGN_GABBRO,
        ageDeposited,
        thickness: 4000, // 4 km intrusive base
        dipAngle: 0,
        dipDirection: 0,
        deformation: DeformationType.UNDEFORMED,
        unconformity: false,
        soilHorizon: SoilOrder.NONE,
        formationName: 0,
      });
      stack.push({
        rockType: RockType.IGN_PILLOW_BASALT,
        ageDeposited,
        thickness: 3000, // 3 km extrusive cap
        dipAngle: 0,
        dipDirection: 0,
        deformation: DeformationType.UNDEFORMED,
        unconformity: false,
        soilHorizon: SoilOrder.NONE,
        formationName: 0,
      });
    } else {
      stack.push({
        rockType: RockType.MET_GNEISS,
        ageDeposited,
        thickness: 15000, // 15 km deep metamorphic base
        dipAngle: 0,
        dipDirection: 0,
        deformation: DeformationType.METAMORPHOSED,
        unconformity: false,
        soilHorizon: SoilOrder.NONE,
        formationName: 0,
      });
      stack.push({
        rockType: RockType.IGN_GRANITE,
        ageDeposited,
        thickness: 20000, // 20 km upper continental crust
        dipAngle: 0,
        dipDirection: 0,
        deformation: DeformationType.UNDEFORMED,
        unconformity: false,
        soilHorizon: SoilOrder.NONE,
        formationName: 0,
      });
    }

    this.stacks.set(cellIndex, stack);
  }

  /**
   * Update dip angles for a cell due to compression or extension.
   * @param cellIndex - Target cell.
   * @param dipDelta  - Change in dip angle (degrees). Positive = steepening.
   * @param direction - Azimuth of the applied stress (0–360).
   */
  applyDeformation(
    cellIndex: number,
    dipDelta: number,
    direction: number,
    deformationType: DeformationType,
  ): void {
    const stack = this.stacks.get(cellIndex);
    if (!stack) return;

    for (const layer of stack) {
      layer.dipAngle = Math.max(0, Math.min(90, layer.dipAngle + dipDelta));
      layer.dipDirection = direction % 360;
      if (deformationType > layer.deformation) {
        layer.deformation = deformationType;
      }
    }
  }

  /**
   * Remove material from the top of the stack (erosion).
   * Returns the total thickness eroded (may be less than requested if stack
   * is thinner).
   */
  erodeTop(cellIndex: number, thickness: number): number {
    const stack = this.stacks.get(cellIndex);
    if (!stack || stack.length === 0) return 0;

    let remaining = thickness;
    let eroded = 0;

    while (remaining > 0 && stack.length > 0) {
      const top = stack[stack.length - 1];
      if (top.thickness <= remaining) {
        remaining -= top.thickness;
        eroded += top.thickness;
        stack.pop();
      } else {
        top.thickness -= remaining;
        eroded += remaining;
        remaining = 0;
      }
    }

    return eroded;
  }

  /** Get total number of cells with stacks. */
  get size(): number {
    return this.stacks.size;
  }

  /** Clear all stacks. */
  clear(): void {
    this.stacks.clear();
  }
}
