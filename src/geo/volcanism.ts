// ─── Volcanism System ───────────────────────────────────────────────────────
// Models volcanic eruptions, their geological products, and atmospheric
// effects. Each eruption type produces specific rock types and has different
// eruption parameters.

import type {
  PlateInfo,
  HotspotInfo,
  StratigraphicLayer,
  StateBufferViews,
} from '../shared/types';
import {
  GRID_SIZE,
  RockType,
  DeformationType,
  SoilOrder,
} from '../shared/types';
import { BoundaryType, type BoundaryCell } from './boundary-classifier';
import type { StratigraphyStack } from './stratigraphy';
import type { Xoshiro256ss } from '../proc/prng';

// ─── Volcano Types ──────────────────────────────────────────────────────────

export enum VolcanoType {
  SHIELD = 0,
  STRATOVOLCANO = 1,
  CINDER_CONE = 2,
  CALDERA = 3,
  FLOOD_BASALT = 4,
  SUBMARINE_RIDGE = 5,
}

// ─── Eruption Record ────────────────────────────────────────────────────────

export interface EruptionRecord {
  cellIndex: number;
  volcanoType: VolcanoType;
  lat: number;
  lon: number;
  /** Eruption intensity 0–1. */
  intensity: number;
  /** Height added to the terrain (meters). */
  heightAdded: number;
  /** Rock type deposited. */
  rockType: RockType;
  /** CO₂ degassed (arbitrary units). */
  co2Degassed: number;
  /** SO₂ degassed (arbitrary units). */
  so2Degassed: number;
}

// ─── Constants ──────────────────────────────────────────────────────────────

const TWO_PI = 2 * Math.PI;
const DEG2RAD = Math.PI / 180;

function rowToLat(row: number): number {
  return (Math.PI / 2) - (row / GRID_SIZE) * Math.PI;
}

function colToLon(col: number): number {
  return (col / GRID_SIZE) * TWO_PI - Math.PI;
}

// ─── Volcanism Engine ───────────────────────────────────────────────────────

export class VolcanismEngine {
  /**
   * Process volcanic activity for one tectonic tick.
   * Eruptions occur at:
   *  1. Convergent boundaries (subduction arcs) → stratovolcanoes
   *  2. Divergent boundaries (mid-ocean ridges) → submarine volcanism
   *  3. Hotspot locations → shield volcanoes
   *  4. Rift zones → flood basalts (occasional)
   *
   * @returns Array of eruption records for this tick.
   */
  tick(
    timeMa: number,
    deltaMa: number,
    boundaries: BoundaryCell[],
    hotspots: HotspotInfo[],
    plates: PlateInfo[],
    stateViews: StateBufferViews,
    stratigraphy: StratigraphyStack,
    rng: Xoshiro256ss,
  ): EruptionRecord[] {
    const eruptions: EruptionRecord[] = [];

    // 1 — Subduction arc volcanism (convergent boundaries)
    this.processSubductionVolcanism(
      timeMa, deltaMa, boundaries, plates, stateViews,
      stratigraphy, rng, eruptions,
    );

    // 2 — Mid-ocean ridge volcanism (divergent boundaries)
    this.processRidgeVolcanism(
      timeMa, deltaMa, boundaries, plates, stateViews,
      stratigraphy, rng, eruptions,
    );

    // 3 — Hotspot volcanism
    this.processHotspotVolcanism(
      timeMa, deltaMa, hotspots, stateViews,
      stratigraphy, rng, eruptions,
    );

    return eruptions;
  }

  // ── Subduction arc volcanism ───────────────────────────────────────────

  private processSubductionVolcanism(
    timeMa: number,
    deltaMa: number,
    boundaries: BoundaryCell[],
    plates: PlateInfo[],
    stateViews: StateBufferViews,
    stratigraphy: StratigraphyStack,
    rng: Xoshiro256ss,
    eruptions: EruptionRecord[],
  ): void {
    // Probability of eruption per convergent boundary cell per tick
    const eruptionProb = Math.min(0.02 * deltaMa, 0.5);

    for (const boundary of boundaries) {
      if (boundary.type !== BoundaryType.CONVERGENT) continue;

      // Only subduction: one plate must be oceanic
      const p1 = plates[boundary.plate1];
      const p2 = plates[boundary.plate2];
      const hasOceanic = p1.isOceanic || p2.isOceanic;
      if (!hasOceanic) continue;

      if (rng.next() > eruptionProb) continue;

      const row = Math.floor(boundary.cellIndex / GRID_SIZE);
      const col = boundary.cellIndex % GRID_SIZE;
      const lat = rowToLat(row) / DEG2RAD;
      const lon = colToLon(col) / DEG2RAD;

      const intensity = 0.3 + rng.next() * 0.7;
      const isExplosive = rng.next() > 0.5;

      const rockType = isExplosive ? RockType.IGN_ANDESITE : RockType.IGN_DACITE;
      const heightAdded = intensity * 200 * deltaMa; // meters
      const co2 = intensity * 0.1 * deltaMa;
      const so2 = intensity * 0.05 * deltaMa;

      // Update terrain
      stateViews.heightMap[boundary.cellIndex] += heightAdded;
      stateViews.crustThicknessMap[boundary.cellIndex] += heightAdded / 1000;

      // Add stratigraphic layer
      stratigraphy.pushLayer(boundary.cellIndex, {
        rockType,
        ageDeposited: timeMa,
        thickness: heightAdded,
        dipAngle: 5 + rng.next() * 15,
        dipDirection: rng.nextFloat(0, 360),
        deformation: DeformationType.UNDEFORMED,
        unconformity: false,
        soilHorizon: SoilOrder.NONE,
        formationName: 0,
      });

      eruptions.push({
        cellIndex: boundary.cellIndex,
        volcanoType: VolcanoType.STRATOVOLCANO,
        lat, lon, intensity, heightAdded, rockType,
        co2Degassed: co2,
        so2Degassed: so2,
      });
    }
  }

  // ── Mid-ocean ridge volcanism ──────────────────────────────────────────

  private processRidgeVolcanism(
    timeMa: number,
    deltaMa: number,
    boundaries: BoundaryCell[],
    plates: PlateInfo[],
    stateViews: StateBufferViews,
    stratigraphy: StratigraphyStack,
    rng: Xoshiro256ss,
    eruptions: EruptionRecord[],
  ): void {
    const eruptionProb = Math.min(0.05 * deltaMa, 0.8);

    for (const boundary of boundaries) {
      if (boundary.type !== BoundaryType.DIVERGENT) continue;

      // Both plates should be oceanic for mid-ocean ridge
      const p1 = plates[boundary.plate1];
      const p2 = plates[boundary.plate2];
      if (!p1.isOceanic || !p2.isOceanic) continue;

      if (rng.next() > eruptionProb) continue;

      const row = Math.floor(boundary.cellIndex / GRID_SIZE);
      const col = boundary.cellIndex % GRID_SIZE;
      const lat = rowToLat(row) / DEG2RAD;
      const lon = colToLon(col) / DEG2RAD;

      const intensity = 0.1 + rng.next() * 0.3;
      const heightAdded = intensity * 50 * deltaMa;
      const co2 = intensity * 0.02 * deltaMa;

      stateViews.heightMap[boundary.cellIndex] += heightAdded;

      stratigraphy.pushLayer(boundary.cellIndex, {
        rockType: RockType.IGN_PILLOW_BASALT,
        ageDeposited: timeMa,
        thickness: heightAdded,
        dipAngle: 0,
        dipDirection: 0,
        deformation: DeformationType.UNDEFORMED,
        unconformity: false,
        soilHorizon: SoilOrder.NONE,
        formationName: 0,
      });

      eruptions.push({
        cellIndex: boundary.cellIndex,
        volcanoType: VolcanoType.SUBMARINE_RIDGE,
        lat, lon, intensity, heightAdded,
        rockType: RockType.IGN_PILLOW_BASALT,
        co2Degassed: co2,
        so2Degassed: 0,
      });
    }
  }

  // ── Hotspot volcanism ──────────────────────────────────────────────────

  private processHotspotVolcanism(
    timeMa: number,
    deltaMa: number,
    hotspots: HotspotInfo[],
    stateViews: StateBufferViews,
    stratigraphy: StratigraphyStack,
    rng: Xoshiro256ss,
    eruptions: EruptionRecord[],
  ): void {
    const { heightMap, plateMap } = stateViews;
    const gridSize = GRID_SIZE;

    for (const hotspot of hotspots) {
      // Probability scales with hotspot strength
      const eruptionProb = Math.min(0.1 * hotspot.strength * deltaMa, 0.9);
      if (rng.next() > eruptionProb) continue;

      // Convert hotspot lat/lon (degrees) to grid coordinates
      const latRad = hotspot.lat * DEG2RAD;
      const lonRad = hotspot.lon * DEG2RAD;
      const row = Math.round(((Math.PI / 2 - latRad) / Math.PI) * gridSize);
      const col = Math.round(((lonRad + Math.PI) / TWO_PI) * gridSize);

      const clampedRow = Math.max(0, Math.min(gridSize - 1, row));
      const clampedCol = Math.max(0, Math.min(gridSize - 1, col));
      const cellIndex = clampedRow * gridSize + clampedCol;

      const isOceanic = heightMap[cellIndex] < 0;
      const intensity = hotspot.strength * (0.5 + rng.next() * 0.5);
      const heightAdded = intensity * 150 * deltaMa;
      const rockType = RockType.IGN_BASALT; // Shield volcanoes produce basalt
      const co2 = intensity * 0.05 * deltaMa;

      heightMap[cellIndex] += heightAdded;
      stateViews.crustThicknessMap[cellIndex] += heightAdded / 1000;

      stratigraphy.pushLayer(cellIndex, {
        rockType,
        ageDeposited: timeMa,
        thickness: heightAdded,
        dipAngle: 2 + rng.next() * 5, // Shield volcanoes have gentle slopes
        dipDirection: rng.nextFloat(0, 360),
        deformation: DeformationType.UNDEFORMED,
        unconformity: false,
        soilHorizon: SoilOrder.NONE,
        formationName: 0,
      });

      eruptions.push({
        cellIndex,
        volcanoType: isOceanic ? VolcanoType.SUBMARINE_RIDGE : VolcanoType.SHIELD,
        lat: hotspot.lat,
        lon: hotspot.lon,
        intensity,
        heightAdded,
        rockType,
        co2Degassed: co2,
        so2Degassed: 0,
      });
    }
  }

  /**
   * Compute total CO₂ and SO₂ degassed from an array of eruption records.
   */
  static totalDegassing(eruptions: EruptionRecord[]): { co2: number; so2: number } {
    let co2 = 0;
    let so2 = 0;
    for (const e of eruptions) {
      co2 += e.co2Degassed;
      so2 += e.so2Degassed;
    }
    return { co2, so2 };
  }
}
