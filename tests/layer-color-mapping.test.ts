import { describe, expect, it } from 'vitest';
import {
  buildScalarLayerRgba,
  buildTopoLayerRgba,
  computeMapStats,
  countDistinctRgbInRgba,
  stretchToPhysicalRange,
} from '../src/layer-color-mapping';
import { temperatureColor } from '../src/layer-color-mapping';

describe('stretchToPhysicalRange', () => {
  it('maps min/max to physical endpoints when data spans', () => {
    expect(stretchToPhysicalRange(0, 0, 100, -40, 45, 0)).toBe(-40);
    expect(stretchToPhysicalRange(100, 0, 100, -40, 45, 0)).toBe(45);
    expect(stretchToPhysicalRange(50, 0, 100, -40, 45, 0)).toBe(2.5);
  });

  it('returns midpoint when data is uniform', () => {
    expect(stretchToPhysicalRange(7, 3, 3, -40, 45, 0)).toBe(2.5);
  });
});

describe('layer overlay RGBA builders', () => {
  it('produces many distinct colors for a latitudinal temperature gradient', () => {
    const n = 64 * 64;
    const data = new Float32Array(n);
    for (let row = 0; row < 64; row++) {
      const t = -30 + (row / 63) * 60;
      for (let col = 0; col < 64; col++) {
        data[row * 64 + col] = t;
      }
    }
    const rgba = buildScalarLayerRgba(data, temperatureColor, -40, 45, 180);
    const distinct = countDistinctRgbInRgba(rgba);
    expect(distinct).toBeGreaterThan(8);
  });

  it('produces many distinct colors for synthetic topography', () => {
    const n = 64 * 64;
    const data = new Float32Array(n);
    for (let i = 0; i < n; i++) {
      data[i] = (i % 64) < 32 ? -3000 + (i % 32) * 100 : 500 + (i % 32) * 80;
    }
    const rgba = buildTopoLayerRgba(data);
    const distinct = countDistinctRgbInRgba(rgba);
    expect(distinct).toBeGreaterThan(4);
  });

  it('flags a uniform scalar field as a single overlay color', () => {
    const data = new Float32Array(256).fill(5);
    const rgba = buildScalarLayerRgba(data, temperatureColor, -40, 45, 180);
    expect(countDistinctRgbInRgba(rgba)).toBe(1);
    expect(computeMapStats(data).stddev).toBe(0);
  });
});
