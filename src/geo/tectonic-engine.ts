// ─── Tectonic Engine ────────────────────────────────────────────────────────
// Main simulation engine for Phase 2: plate tectonics and volcanism.
// Orchestrates plate motion, boundary processes, isostasy, hotspot tracking,
// volcanism, and stratigraphy updates each tectonic tick.

import type {
  PlateInfo,
  HotspotInfo,
  StateBufferViews,
  AtmosphericComposition,
  TickPayload,
} from '../shared/types';
import { GRID_SIZE, RockType, DeformationType, SoilOrder } from '../shared/types';
import type { EventBus } from '../kernel/event-bus';
import type { EventLog } from '../kernel/event-log';
import {
  BoundaryClassifier,
  BoundaryType,
  plateVelocityAt,
  getNeighborIndices,
  type BoundaryCell,
} from './boundary-classifier';
import { VolcanismEngine, type EruptionRecord } from './volcanism';
import { StratigraphyStack } from './stratigraphy';
import { Xoshiro256ss } from '../proc/prng';

// ─── Constants ──────────────────────────────────────────────────────────────

const DEG2RAD = Math.PI / 180;
const TWO_PI = 2 * Math.PI;

/** Isostatic density ratio (crust / mantle). */
const ISOSTATIC_RATIO = 2.7 / 3.3;

/** Mantle density (kg/m³) for isostatic calculations. */
const MANTLE_DENSITY = 3300;

/** Crust density (kg/m³). */
const CRUST_DENSITY = 2700;

// ─── Helpers ────────────────────────────────────────────────────────────────

function rowToLat(row: number): number {
  return (Math.PI / 2) - (row / GRID_SIZE) * Math.PI;
}

function colToLon(col: number): number {
  return (col / GRID_SIZE) * TWO_PI - Math.PI;
}

// ─── TectonicEngine ─────────────────────────────────────────────────────────

export interface TectonicEngineConfig {
  /** Minimum tectonic tick interval in Ma (default 0.1). */
  minTickInterval?: number;
}

export class TectonicEngine {
  readonly stratigraphy: StratigraphyStack;
  private readonly boundaryClassifier: BoundaryClassifier;
  private readonly volcanismEngine: VolcanismEngine;
  private readonly bus: EventBus;
  private readonly eventLog: EventLog;
  private rng: Xoshiro256ss;

  private plates: PlateInfo[] = [];
  private hotspots: HotspotInfo[] = [];
  private atmosphere: AtmosphericComposition = { n2: 0.78, o2: 0.21, co2: 0.0004, h2o: 0.01 };
  private stateViews: StateBufferViews | null = null;

  /** Time accumulator for sub-tick batching. */
  private accumulator = 0;
  private readonly minTickInterval: number;

  constructor(
    bus: EventBus,
    eventLog: EventLog,
    seed: number,
    config?: TectonicEngineConfig,
  ) {
    this.bus = bus;
    this.eventLog = eventLog;
    this.rng = new Xoshiro256ss(seed);
    this.stratigraphy = new StratigraphyStack();
    this.boundaryClassifier = new BoundaryClassifier();
    this.volcanismEngine = new VolcanismEngine();
    this.minTickInterval = config?.minTickInterval ?? 0.1;
  }

  /**
   * Initialize the tectonic system with data from planet generation.
   */
  initialize(
    plates: PlateInfo[],
    hotspots: HotspotInfo[],
    atmosphere: AtmosphericComposition,
    stateViews: StateBufferViews,
  ): void {
    this.plates = plates;
    this.hotspots = hotspots;
    this.atmosphere = atmosphere;
    this.stateViews = stateViews;

    // Initialize basement stratigraphy for each cell
    const cellCount = GRID_SIZE * GRID_SIZE;
    for (let i = 0; i < cellCount; i++) {
      const plate = plates[stateViews.plateMap[i]];
      this.stratigraphy.initializeBasement(
        i,
        plate.isOceanic,
        stateViews.rockAgeMap[i],
      );
    }
  }

  /**
   * Process one simulation tick. Called each frame with the simulation delta.
   * Batches sub-ticks if deltaMa exceeds the minimum tick interval.
   */
  tick(timeMa: number, deltaMa: number): EruptionRecord[] {
    if (!this.stateViews || deltaMa <= 0) return [];

    this.accumulator += deltaMa;
    const allEruptions: EruptionRecord[] = [];

    while (this.accumulator >= this.minTickInterval) {
      const subDelta = this.minTickInterval;
      this.accumulator -= subDelta;
      const subTime = timeMa - this.accumulator;

      const eruptions = this.processTectonicTick(subTime, subDelta);
      allEruptions.push(...eruptions);
    }

    return allEruptions;
  }

  // ── Core tick processing ──────────────────────────────────────────────

  private processTectonicTick(timeMa: number, deltaMa: number): EruptionRecord[] {
    const stateViews = this.stateViews!;

    // 1 — Classify plate boundaries
    const boundaries = this.boundaryClassifier.classify(
      stateViews.plateMap,
      this.plates,
    );

    // 2 — Process plate interactions at boundaries
    this.processConvergentBoundaries(boundaries, deltaMa, timeMa);
    this.processDivergentBoundaries(boundaries, deltaMa, timeMa);

    // 3 — Isostatic adjustment
    this.applyIsostasy(stateViews, deltaMa);

    // 4 — Update hotspot positions (hotspots are fixed; plates move over them)
    // The hotspot lat/lon stays fixed; the plate above may change.

    // 5 — Volcanism
    const eruptions = this.volcanismEngine.tick(
      timeMa, deltaMa, boundaries, this.hotspots,
      this.plates, stateViews, this.stratigraphy, this.rng,
    );

    // 6 — Emit events for significant eruptions
    for (const eruption of eruptions) {
      if (eruption.intensity > 0.5) {
        this.bus.emit('VOLCANIC_ERUPTION', {
          lat: eruption.lat,
          lon: eruption.lon,
          intensity: eruption.intensity,
        });
        this.eventLog.record({
          timeMa,
          type: 'VOLCANIC_ERUPTION',
          description: `Eruption at (${eruption.lat.toFixed(1)}°, ${eruption.lon.toFixed(1)}°), intensity ${eruption.intensity.toFixed(2)}`,
          location: { lat: eruption.lat, lon: eruption.lon },
        });
      }
    }

    // 7 — Update atmospheric CO₂ from volcanic degassing
    const degassing = VolcanismEngine.totalDegassing(eruptions);
    this.atmosphere.co2 += degassing.co2 * 1e-6;

    return eruptions;
  }

  // ── Convergent boundary processes ─────────────────────────────────────

  private processConvergentBoundaries(
    boundaries: BoundaryCell[],
    deltaMa: number,
    timeMa: number,
  ): void {
    const stateViews = this.stateViews!;

    for (const b of boundaries) {
      if (b.type !== BoundaryType.CONVERGENT) continue;

      const p1 = this.plates[b.plate1];
      const p2 = this.plates[b.plate2];

      if (p1.isOceanic && !p2.isOceanic) {
        // Oceanic-continental subduction: oceanic plate dips under
        this.applySubduction(b.cellIndex, deltaMa, stateViews);
      } else if (!p1.isOceanic && p2.isOceanic) {
        // Same, reversed
        this.applySubduction(b.cellIndex, deltaMa, stateViews);
      } else if (!p1.isOceanic && !p2.isOceanic) {
        // Continental collision
        this.applyContinentalCollision(b, deltaMa, timeMa, stateViews);
      } else {
        // Oceanic-oceanic: older plate subducts
        this.applySubduction(b.cellIndex, deltaMa, stateViews);
      }
    }
  }

  private applySubduction(
    cellIndex: number,
    deltaMa: number,
    stateViews: StateBufferViews,
  ): void {
    // Trench deepening
    const trenchRate = -50 * deltaMa; // meters per Ma
    stateViews.heightMap[cellIndex] += trenchRate;

    // Crustal thinning at the trench
    stateViews.crustThicknessMap[cellIndex] = Math.max(
      3,
      stateViews.crustThicknessMap[cellIndex] - 0.5 * deltaMa,
    );
  }

  private applyContinentalCollision(
    boundary: BoundaryCell,
    deltaMa: number,
    timeMa: number,
    stateViews: StateBufferViews,
  ): void {
    const cellIndex = boundary.cellIndex;

    // Crustal thickening (fold-and-thrust belt)
    const thickeningRate = 2 * deltaMa * boundary.relativeSpeed;
    stateViews.crustThicknessMap[cellIndex] += thickeningRate;

    // Mountain building
    const upliftRate = 100 * deltaMa * boundary.relativeSpeed;
    stateViews.heightMap[cellIndex] += upliftRate;

    // Deform stratigraphy
    this.stratigraphy.applyDeformation(
      cellIndex,
      2 * deltaMa,
      0, // simplified direction
      DeformationType.FOLDED,
    );

    // Emit collision event (sampled, not every cell)
    if (boundary.relativeSpeed > 2.0) {
      this.bus.emit('PLATE_COLLISION', {
        plate1: boundary.plate1,
        plate2: boundary.plate2,
        boundaryPoints: [],
      });
      this.eventLog.record({
        timeMa,
        type: 'PLATE_COLLISION',
        description: `Collision between plates ${boundary.plate1} and ${boundary.plate2}`,
      });
    }
  }

  // ── Divergent boundary processes ──────────────────────────────────────

  private processDivergentBoundaries(
    boundaries: BoundaryCell[],
    deltaMa: number,
    timeMa: number,
  ): void {
    const stateViews = this.stateViews!;

    for (const b of boundaries) {
      if (b.type !== BoundaryType.DIVERGENT) continue;

      const p1 = this.plates[b.plate1];
      const p2 = this.plates[b.plate2];

      // Rift formation: crustal thinning and graben deepening
      const thinningRate = 0.3 * deltaMa * b.relativeSpeed;
      stateViews.crustThicknessMap[b.cellIndex] = Math.max(
        3,
        stateViews.crustThicknessMap[b.cellIndex] - thinningRate,
      );

      // New oceanic crust if thinned enough (seafloor spreading)
      if (stateViews.crustThicknessMap[b.cellIndex] < 10) {
        stateViews.rockTypeMap[b.cellIndex] = RockType.IGN_BASALT;
        stateViews.rockAgeMap[b.cellIndex] = timeMa;
      }

      // Emit rift event for significant rifts
      if (b.relativeSpeed > 1.5 && !p1.isOceanic && !p2.isOceanic) {
        this.bus.emit('PLATE_RIFT', {
          plate1: b.plate1,
          plate2: b.plate2,
          boundaryPoints: [],
        });
        this.eventLog.record({
          timeMa,
          type: 'PLATE_RIFT',
          description: `Rift between plates ${b.plate1} and ${b.plate2}`,
        });
      }
    }
  }

  // ── Isostatic adjustment ──────────────────────────────────────────────

  /**
   * Adjust surface elevation towards isostatic equilibrium.
   * Thicker crust → higher elevation; thinner crust → lower elevation.
   * Uses an exponential relaxation model.
   */
  private applyIsostasy(stateViews: StateBufferViews, deltaMa: number): void {
    const cellCount = GRID_SIZE * GRID_SIZE;
    const relaxRate = Math.min(1, 0.1 * deltaMa); // relaxation per tick

    for (let i = 0; i < cellCount; i++) {
      const crustThickness = stateViews.crustThicknessMap[i]; // km
      // Equilibrium elevation (Airy isostasy model):
      // elevation = crustThickness * (1 - ρ_crust/ρ_mantle) - sea_level_offset
      const equilibrium = crustThickness * 1000 * (1 - ISOSTATIC_RATIO) - 4500;

      // Relax towards equilibrium
      const current = stateViews.heightMap[i];
      stateViews.heightMap[i] += (equilibrium - current) * relaxRate;
    }
  }

  // ── Getters ───────────────────────────────────────────────────────────

  getPlates(): ReadonlyArray<PlateInfo> {
    return this.plates;
  }

  getHotspots(): ReadonlyArray<HotspotInfo> {
    return this.hotspots;
  }

  getAtmosphere(): Readonly<AtmosphericComposition> {
    return this.atmosphere;
  }
}
