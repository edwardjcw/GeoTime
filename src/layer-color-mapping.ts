/** Map statistics for layer overlay diagnostics. */
export interface MapStats {
  min: number;
  max: number;
  mean: number;
  stddev: number;
}

export function computeMapStats(data: ArrayLike<number>): MapStats {
  const n = data.length;
  if (n === 0) return { min: 0, max: 0, mean: 0, stddev: 0 };

  let min = Infinity;
  let max = -Infinity;
  let sum = 0;
  for (let i = 0; i < n; i++) {
    const v = data[i];
    if (v < min) min = v;
    if (v > max) max = v;
    sum += v;
  }
  const mean = sum / n;
  let varSum = 0;
  for (let i = 0; i < n; i++) {
    const d = data[i] - mean;
    varSum += d * d;
  }
  return { min, max, mean, stddev: Math.sqrt(varSum / n) };
}

/**
 * Stretch a scalar value across the physical palette range when the dataset span
 * is wide enough to avoid flat overlays. Falls back to the raw value when the
 * span is too narrow (uniform or uninitialized data).
 */
export function stretchToPhysicalRange(
  value: number,
  dataMin: number,
  dataMax: number,
  physicalMin: number,
  physicalMax: number,
  minSpan: number,
): number {
  const span = dataMax - dataMin;
  if (span < minSpan) return value;
  const t = (value - dataMin) / span;
  return physicalMin + t * (physicalMax - physicalMin);
}

/** Temperature (°C) → RGB: blue (cold) through white (0°C) to red (hot). */
export function temperatureColor(t: number): [number, number, number] {
  if (t <= -40) return [0, 0, 200];
  if (t < 0) {
    const f = (t + 40) / 40;
    return [Math.round(f * 255), Math.round(f * 255), 200 + Math.round(f * 55)];
  }
  const f = Math.min(1, t / 45);
  return [200 + Math.round(f * 55), Math.round((1 - f) * 200), Math.round((1 - f) * 200)];
}

/** Precipitation (mm/yr) → RGB: tan (dry) to deep blue-green (wet). */
export function precipitationColor(p: number): [number, number, number] {
  const f = Math.min(1, p / 2500);
  return [
    Math.round(200 - f * 180),
    Math.round(160 + f * 60),
    Math.round(60 + f * 180),
  ];
}

/** Cloud proxy — precipitation scaled to white/grey cloud appearance. */
export function cloudColor(p: number): [number, number, number] {
  const f = Math.min(1, p / 2500);
  const v = Math.round(60 + f * 195);
  return [v, v, v];
}

/** Biomass (kg/m²) → RGB: nearly black (barren) to vivid green (lush). */
export function biomassColor(b: number): [number, number, number] {
  const f = Math.min(1, b / 12);
  return [Math.round(10 + f * 20), Math.round(40 + f * 190), Math.round(10 + f * 20)];
}

/** Biomatter density (kg C/m²) → RGB: sparse dark violet to dense blue-magenta. */
export function biomatterColor(density: number): [number, number, number] {
  const f = Math.min(1, density / 3);
  return [
    Math.round(28 + f * 92),
    Math.round(18 + f * 102),
    Math.round(80 + f * 150),
  ];
}

/** Organic carbon (kg C/m²) → RGB: lean dark soil to carbon-rich amber-brown. */
export function organicCarbonColor(carbon: number): [number, number, number] {
  const f = Math.min(1, carbon / 5);
  return [
    Math.round(45 + f * 135),
    Math.round(30 + f * 95),
    Math.round(12 + f * 38),
  ];
}

/** Elevation (m) → RGB: vivid topographic bands (ocean → peaks). */
export function topoColor(h: number): [number, number, number] {
  if (h < -5000) return [0, 20, 140];
  if (h < -200) {
    const f = (h + 5000) / 4800;
    return [Math.round(f * 40), Math.round(f * 100 + 60), Math.round(120 + f * 100)];
  }
  if (h < 0) return [100, 200, 240];
  if (h < 500) return [50, 210, 70];
  if (h < 1500) return [210, 190, 50];
  if (h < 3000) return [190, 110, 35];
  if (h < 5000) return [160, 110, 90];
  return [255, 255, 255];
}

/** USDA soil order (0-12) → RGB colour for the soil layer overlay. */
export function soilOrderColor(order: number): [number, number, number] {
  switch (order) {
    case 0: return [180, 180, 180];
    case 1: return [160, 120, 60];
    case 2: return [100, 80, 40];
    case 3: return [230, 200, 120];
    case 4: return [200, 180, 140];
    case 5: return [220, 240, 255];
    case 6: return [60, 100, 60];
    case 7: return [140, 170, 100];
    case 8: return [180, 140, 80];
    case 9: return [80, 50, 20];
    case 10: return [100, 130, 160];
    case 11: return [120, 80, 40];
    case 12: return [160, 100, 140];
    default: return [180, 180, 180];
  }
}

/** Map raw height (m) to topo palette input with hybrid physical/stretch rules. */
export function scaleTopoHeight(
  h: number,
  oceanMin: number,
  oceanMax: number,
  landMin: number,
  landMax: number,
): number {
  const OCEAN_PALETTE_LO = -7000;
  const OCEAN_PALETTE_HI = -200;
  const LAND_PALETTE_LO = 0;
  const LAND_PALETTE_HI = 4000;
  const OCEAN_REF_LO = -6000;
  const OCEAN_REF_HI = -100;
  const MIN_OCEAN_SPAN = 200;
  const MIN_LAND_SPAN = 200;

  if (h < 0) {
    const oceanSpan = oceanMax - oceanMin;
    if (oceanSpan >= MIN_OCEAN_SPAN) {
      const t = Math.max(0, Math.min(1, (h - oceanMin) / oceanSpan));
      return t * (OCEAN_PALETTE_HI - OCEAN_PALETTE_LO) + OCEAN_PALETTE_LO;
    }
    const t = Math.max(0, Math.min(1, (h - OCEAN_REF_LO) / (OCEAN_REF_HI - OCEAN_REF_LO)));
    return t * (OCEAN_PALETTE_HI - OCEAN_PALETTE_LO) + OCEAN_PALETTE_LO;
  }

  const landSpan = landMax - landMin;
  if (landSpan >= MIN_LAND_SPAN) {
    const t = Math.max(0, Math.min(1, (h - landMin) / landSpan));
    return t * (LAND_PALETTE_HI - LAND_PALETTE_LO) + LAND_PALETTE_LO;
  }
  return Math.min(h, LAND_PALETTE_HI);
}

export function computeTopoExtents(data: ArrayLike<number>): {
  oceanMin: number;
  oceanMax: number;
  landMin: number;
  landMax: number;
} {
  let oceanMin = 0;
  let oceanMax = 0;
  let landMin = 0;
  let landMax = 0;
  let hasOcean = false;
  let hasLand = false;

  for (let i = 0; i < data.length; i++) {
    const h = data[i];
    if (h < 0) {
      if (!hasOcean || h < oceanMin) oceanMin = h;
      if (!hasOcean || h > oceanMax) oceanMax = h;
      hasOcean = true;
    } else {
      if (!hasLand || h < landMin) landMin = h;
      if (!hasLand || h > landMax) landMax = h;
      hasLand = true;
    }
  }

  if (!hasOcean) {
    oceanMin = -6000;
    oceanMax = -100;
  }
  if (!hasLand) {
    landMin = 0;
    landMax = 4000;
  }

  return { oceanMin, oceanMax, landMin, landMax };
}
