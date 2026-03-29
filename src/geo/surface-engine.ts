// ─── Surface Process Engine ─────────────────────────────────────────────────
// Orchestrator for Phase 3 surface processes: fluvial erosion, glacial
// erosion, aeolian/chemical weathering, and pedogenesis (soil formation).
// Runs after the tectonic engine each tick.

import type { StateBufferViews } from '../shared/types';
import { GRID_SIZE } from '../shared/types';
import type { EventBus } from '../kernel/event-bus';
import type { EventLog } from '../kernel/event-log';
import { ErosionEngine, type ErosionResult } from './erosion-engine';
import { GlacialEngine, type GlacialResult } from './glacial-engine';
import { WeatheringEngine, type WeatheringResult } from './weathering-engine';
import { PedogenesisEngine, type PedogenesisResult } from './pedogenesis';
import type { StratigraphyStack } from './stratigraphy';
import type { Xoshiro256ss } from '../proc/prng';
import { Xoshiro256ss as XoshiroClass } from '../proc/prng';

// ─── Types ──────────────────────────────────────────────────────────────────

export interface SurfaceTickResult {
  erosion: ErosionResult;
  glacial: GlacialResult;
  weathering: WeatheringResult;
  pedogenesis: PedogenesisResult;
}

export interface SurfaceEngineConfig {
  /** Minimum surface process tick interval in Ma (default 0.5). */
  minTickInterval?: number;
  /** Grid size (default GRID_SIZE). */
  gridSize?: number;
}

// ─── SurfaceEngine ──────────────────────────────────────────────────────────

export class SurfaceEngine {
  private readonly erosionEngine: ErosionEngine;
  private readonly glacialEngine: GlacialEngine;
  private readonly weatheringEngine: WeatheringEngine;
  private readonly pedogenesisEngine: PedogenesisEngine;
  private readonly bus: EventBus;
  private readonly eventLog: EventLog;
  private rng: XoshiroClass;

  private stateViews: StateBufferViews | null = null;
  private stratigraphy: StratigraphyStack | null = null;

  /** Time accumulator for sub-tick batching. */
  private accumulator = 0;
  private readonly minTickInterval: number;

  /** Track previous glaciation state for advance/retreat events. */
  private prevGlaciatedCells = 0;

  constructor(
    bus: EventBus,
    eventLog: EventLog,
    seed: number,
    config?: SurfaceEngineConfig,
  ) {
    const gridSize = config?.gridSize ?? GRID_SIZE;
    this.bus = bus;
    this.eventLog = eventLog;
    this.rng = new XoshiroClass(seed);
    this.minTickInterval = config?.minTickInterval ?? 0.5;

    this.erosionEngine = new ErosionEngine(gridSize);
    this.glacialEngine = new GlacialEngine(gridSize);
    this.weatheringEngine = new WeatheringEngine(gridSize);
    this.pedogenesisEngine = new PedogenesisEngine(gridSize);
  }

  /**
   * Initialize the surface engine with shared state and stratigraphy.
   */
  initialize(
    stateViews: StateBufferViews,
    stratigraphy: StratigraphyStack,
  ): void {
    this.stateViews = stateViews;
    this.stratigraphy = stratigraphy;
    this.glacialEngine.clear();
    this.prevGlaciatedCells = 0;
  }

  /**
   * Process one simulation tick. Called after the tectonic engine each frame.
   * Batches sub-ticks if deltaMa exceeds the minimum tick interval.
   */
  tick(timeMa: number, deltaMa: number): SurfaceTickResult | null {
    if (!this.stateViews || !this.stratigraphy || deltaMa <= 0) return null;

    this.accumulator += deltaMa;

    let lastResult: SurfaceTickResult | null = null;

    while (this.accumulator >= this.minTickInterval) {
      const subDelta = this.minTickInterval;
      this.accumulator -= subDelta;
      const subTime = timeMa - this.accumulator;

      lastResult = this.processSurfaceTick(subTime, subDelta);
    }

    return lastResult;
  }

  // ── Core tick processing ──────────────────────────────────────────────

  private processSurfaceTick(timeMa: number, deltaMa: number): SurfaceTickResult {
    const stateViews = this.stateViews!;
    const stratigraphy = this.stratigraphy!;

    // 1 — Fluvial erosion (rivers, sediment transport)
    const erosion = this.erosionEngine.tick(
      timeMa, deltaMa, stateViews, stratigraphy, this.rng,
    );

    // 2 — Glacial erosion (ice sheets, moraines)
    const glacial = this.glacialEngine.tick(
      timeMa, deltaMa, stateViews, stratigraphy, this.rng,
    );

    // 3 — Aeolian & chemical weathering
    const weathering = this.weatheringEngine.tick(
      timeMa, deltaMa, stateViews, stratigraphy, this.rng,
    );

    // 4 — Pedogenesis (soil formation)
    const pedogenesis = this.pedogenesisEngine.tick(
      timeMa, deltaMa, stateViews, stratigraphy,
    );

    // ── Emit events ─────────────────────────────────────────────────────

    // Erosion cycle summary
    if (erosion.cellsAffected > 0) {
      this.bus.emit('EROSION_CYCLE', {
        totalEroded: erosion.totalEroded,
        totalDeposited: erosion.totalDeposited,
        cellsAffected: erosion.cellsAffected,
      });
    }

    // Glaciation advance / retreat
    if (glacial.glaciatedCells > this.prevGlaciatedCells * 1.2 && glacial.glaciatedCells > 10) {
      this.bus.emit('GLACIATION_ADVANCE', {
        glaciatedCells: glacial.glaciatedCells,
        equilibriumLineAltitude: glacial.equilibriumLineAltitude,
      });
      this.eventLog.record({
        timeMa,
        type: 'ICE_AGE_ONSET',
        description: `Glaciation advancing: ${glacial.glaciatedCells} cells glaciated, ELA ${glacial.equilibriumLineAltitude.toFixed(0)} m`,
      });
    } else if (
      glacial.glaciatedCells < this.prevGlaciatedCells * 0.8 &&
      this.prevGlaciatedCells > 10
    ) {
      this.bus.emit('GLACIATION_RETREAT', {
        glaciatedCells: glacial.glaciatedCells,
      });
      this.eventLog.record({
        timeMa,
        type: 'ICE_AGE_END',
        description: `Glaciation retreating: ${glacial.glaciatedCells} cells remaining`,
      });
    }
    this.prevGlaciatedCells = glacial.glaciatedCells;

    // Major river formation
    if (erosion.riverCells.length > 100) {
      const maxAreaCell = erosion.riverCells[0]; // first is often the largest
      this.bus.emit('MAJOR_RIVER_FORMED', {
        cellIndex: maxAreaCell,
        drainageArea: erosion.riverCells.length,
      });
    }

    return { erosion, glacial, weathering, pedogenesis };
  }

  // ── Getters ───────────────────────────────────────────────────────────

  /** Get the glacial engine (for ice thickness visualization). */
  getGlacialEngine(): GlacialEngine {
    return this.glacialEngine;
  }

  /** Get the erosion engine. */
  getErosionEngine(): ErosionEngine {
    return this.erosionEngine;
  }
}
