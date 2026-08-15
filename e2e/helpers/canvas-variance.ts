import type { Page } from '@playwright/test';
import { PNG } from 'pngjs';

export interface CanvasVariance {
  uniqueColors: number;
  luminanceStdDev: number;
  luminanceMean: number;
}

/** Minimum thresholds for a layer overlay to be considered spatially varied. */
export const LAYER_VARIANCE_THRESHOLDS = {
  uniqueColors: 12,
  luminanceStdDev: 8,
} as const;

/**
 * Measure color variation from a Playwright screenshot of the globe viewport.
 * Uses PNG pixel data (what the user actually sees) rather than WebGL readPixels.
 */
export async function measureGlobeScreenshotVariance(
  page: Page,
  step = 4,
): Promise<CanvasVariance> {
  const pngBuffer = await page.locator('[data-testid="globe-canvas"]').screenshot();
  const png = PNG.sync.read(pngBuffer);
  const { width, height, data } = png;

  const colors = new Set<number>();
  let sum = 0;
  let sumSq = 0;
  let n = 0;

  for (let y = 0; y < height; y += step) {
    for (let x = 0; x < width; x += step) {
      const i = (width * y + x) * 4;
      const r = data[i];
      const g = data[i + 1];
      const b = data[i + 2];
      const a = data[i + 3];
      if (a < 8) continue;
      colors.add((r << 16) | (g << 8) | b);
      const lum = 0.299 * r + 0.587 * g + 0.114 * b;
      sum += lum;
      sumSq += lum * lum;
      n++;
    }
  }

  if (n === 0) return { uniqueColors: 0, luminanceStdDev: 0, luminanceMean: 0 };
  const mean = sum / n;
  const variance = Math.max(0, sumSq / n - mean * mean);
  return {
    uniqueColors: colors.size,
    luminanceStdDev: Math.sqrt(variance),
    luminanceMean: mean,
  };
}

export function assertLayerHasSpatialVariation(
  layer: string,
  stats: CanvasVariance,
): void {
  if (
    stats.uniqueColors < LAYER_VARIANCE_THRESHOLDS.uniqueColors
    || stats.luminanceStdDev < LAYER_VARIANCE_THRESHOLDS.luminanceStdDev
  ) {
    throw new Error(
      `Layer "${layer}" looks flat: uniqueColors=${stats.uniqueColors} ` +
      `(need ≥${LAYER_VARIANCE_THRESHOLDS.uniqueColors}), ` +
      `luminanceStdDev=${stats.luminanceStdDev.toFixed(2)} ` +
      `(need ≥${LAYER_VARIANCE_THRESHOLDS.luminanceStdDev})`,
    );
  }
}
