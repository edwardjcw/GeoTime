// ─── Pedogenesis (Soil Formation) ───────────────────────────────────────────
// Models soil formation on exposed land surfaces using a simplified CLORPT
// (Climate, Organisms, Relief, Parent material, Time) framework.
// Assigns USDA soil orders and updates soilTypeMap / soilDepthMap.

import type { StateBufferViews } from '../shared/types';
import { GRID_SIZE, RockType, SoilOrder } from '../shared/types';
import type { StratigraphyStack } from './stratigraphy';

// ─── Constants ──────────────────────────────────────────────────────────────

/** Minimum temperature for soil formation (°C). */
const MIN_SOIL_TEMP = -20;

/** Minimum precipitation for soil formation (mm/yr). */
const MIN_SOIL_PRECIP = 10;

/** Base soil formation rate (meters depth per Ma). */
const BASE_SOIL_RATE = 0.01;

/** Maximum soil depth (meters). */
const MAX_SOIL_DEPTH = 5;

// ─── Types ──────────────────────────────────────────────────────────────────

export interface PedogenesisResult {
  /** Number of cells where soil was formed or updated. */
  cellsFormed: number;
  /** Number of cells with mature (classified) soils. */
  classifiedCells: number;
}

// ─── PedogenesisEngine ──────────────────────────────────────────────────────

export class PedogenesisEngine {
  private readonly gridSize: number;

  constructor(gridSize: number = GRID_SIZE) {
    this.gridSize = gridSize;
  }

  /**
   * Classify soil order based on climate, parent rock, and soil depth.
   * Simplified USDA soil taxonomy assignment.
   *
   * Formation conditions (from GeoTime_Implementation_Plan.md Section 7):
   * - ENTISOL: young soil, minimal profile development (< 0.2 m depth)
   * - INCEPTISOL: slightly more developed (0.2–0.5 m), moderate climate
   * - MOLLISOL: temperate grasslands, moderate precip (400–800 mm)
   * - ALFISOL: deciduous forest climate, moderate-high precip (600–1200 mm)
   * - ULTISOL: warm/humid, heavily leached (precip > 1000 mm, temp > 15°C)
   * - OXISOL: tropical, extreme weathering (precip > 1500 mm, temp > 22°C)
   * - SPODOSOL: cool/humid coniferous forest (temp < 10°C, precip > 500 mm)
   * - HISTOSOL: wet, organic accumulation (precip > 800 mm, low slope)
   * - ARIDISOL: arid (precip < 250 mm)
   * - VERTISOL: clay-rich, expanding/contracting (precip 500–1000 mm, warm)
   * - ANDISOL: volcanic parent material
   * - GELISOL: permafrost (temp < −2°C year-round)
   */
  classifySoil(
    temperature: number,
    precipitation: number,
    parentRock: RockType,
    soilDepth: number,
    height: number,
  ): SoilOrder {
    // Permafrost → GELISOL
    if (temperature < -2) {
      return SoilOrder.GELISOL;
    }

    // Volcanic parent → ANDISOL
    if (
      parentRock === RockType.IGN_BASALT ||
      parentRock === RockType.IGN_ANDESITE ||
      parentRock === RockType.IGN_DACITE ||
      parentRock === RockType.IGN_TUFF ||
      parentRock === RockType.IGN_PYROCLASTIC
    ) {
      return SoilOrder.ANDISOL;
    }

    // Very young / shallow → ENTISOL
    if (soilDepth < 0.2) {
      return SoilOrder.ENTISOL;
    }

    // Arid → ARIDISOL
    if (precipitation < 250) {
      return SoilOrder.ARIDISOL;
    }

    // Slightly developed → INCEPTISOL
    if (soilDepth < 0.5) {
      return SoilOrder.INCEPTISOL;
    }

    // Tropical extreme weathering → OXISOL
    if (temperature > 22 && precipitation > 1500) {
      return SoilOrder.OXISOL;
    }

    // Warm humid, heavily leached → ULTISOL
    if (temperature > 15 && precipitation > 1000) {
      return SoilOrder.ULTISOL;
    }

    // Cool humid, coniferous → SPODOSOL
    if (temperature < 10 && precipitation > 500) {
      return SoilOrder.SPODOSOL;
    }

    // Wet, low-lying → HISTOSOL
    if (precipitation > 800 && height < 200) {
      return SoilOrder.HISTOSOL;
    }

    // Clay-rich warm areas → VERTISOL
    if (
      precipitation >= 500 &&
      precipitation <= 1000 &&
      temperature > 18 &&
      (parentRock === RockType.SED_SHALE || parentRock === RockType.SED_MUDSTONE)
    ) {
      return SoilOrder.VERTISOL;
    }

    // Deciduous forest climate → ALFISOL
    if (precipitation >= 600 && precipitation <= 1200 && temperature >= 5) {
      return SoilOrder.ALFISOL;
    }

    // Temperate grasslands → MOLLISOL
    if (precipitation >= 400 && precipitation <= 800 && temperature >= 0) {
      return SoilOrder.MOLLISOL;
    }

    // Fallback
    return SoilOrder.INCEPTISOL;
  }

  /**
   * Compute soil formation rate (meters/Ma) based on climate and parent rock.
   * Warmer + wetter → faster formation. Hard rocks → slower.
   */
  soilFormationRate(
    temperature: number,
    precipitation: number,
    parentRock: RockType,
  ): number {
    if (temperature < MIN_SOIL_TEMP || precipitation < MIN_SOIL_PRECIP) {
      return 0;
    }

    // Temperature factor (warmer → faster, 0–1 range)
    const tempFactor = Math.max(0, Math.min(1, (temperature + 20) / 50));

    // Precipitation factor (wetter → faster, 0–1 range)
    const precipFactor = Math.max(0, Math.min(1, precipitation / 2000));

    // Rock hardness factor: igneous/metamorphic slower, sedimentary faster
    let hardnessFactor = 1.0;
    if (parentRock < RockType.SED_SANDSTONE) {
      hardnessFactor = 0.5; // igneous
    } else if (parentRock >= RockType.MET_SLATE) {
      hardnessFactor = 0.6; // metamorphic
    }

    return BASE_SOIL_RATE * tempFactor * precipFactor * hardnessFactor;
  }

  /**
   * Run one pedogenesis tick.
   *
   * For each land cell above sea level:
   * 1. Check if conditions allow soil formation (temp > -20°C, some moisture).
   * 2. Increase soil depth based on formation rate.
   * 3. Classify soil order based on climate, parent material, and maturity.
   * 4. Update soilTypeMap and soilDepthMap.
   * 5. Write soil horizon to the topmost stratigraphic layer.
   */
  tick(
    timeMa: number,
    deltaMa: number,
    stateViews: StateBufferViews,
    stratigraphy: StratigraphyStack,
  ): PedogenesisResult {
    const {
      heightMap,
      temperatureMap,
      precipitationMap,
      soilTypeMap,
      soilDepthMap,
    } = stateViews;
    const cellCount = this.gridSize * this.gridSize;
    let cellsFormed = 0;
    let classifiedCells = 0;

    for (let i = 0; i < cellCount; i++) {
      // Only process land above sea level
      if (heightMap[i] <= 0) continue;

      const temp = temperatureMap[i];
      const precip = precipitationMap[i];

      // Determine parent rock from stratigraphy top layer
      const topLayer = stratigraphy.getTopLayer(i);
      const parentRock = topLayer?.rockType ?? RockType.IGN_GRANITE;

      // Compute formation rate
      const rate = this.soilFormationRate(temp, precip, parentRock);
      if (rate <= 0) continue;

      // Grow soil depth
      const currentDepth = soilDepthMap[i];
      const newDepth = Math.min(MAX_SOIL_DEPTH, currentDepth + rate * deltaMa);
      soilDepthMap[i] = newDepth;
      cellsFormed++;

      // Classify soil order
      const order = this.classifySoil(temp, precip, parentRock, newDepth, heightMap[i]);
      soilTypeMap[i] = order;

      if (order !== SoilOrder.NONE) {
        classifiedCells++;
      }

      // Write soil horizon to top stratigraphic layer
      if (topLayer && order !== SoilOrder.NONE) {
        // Mutate in place (the layer is in the stack array)
        (topLayer as { soilHorizon: SoilOrder }).soilHorizon = order;
      }
    }

    return { cellsFormed, classifiedCells };
  }
}
