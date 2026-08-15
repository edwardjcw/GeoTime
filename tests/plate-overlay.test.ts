import { describe, it, expect } from 'vitest';
import {
  classifyPlateOverlayCell,
  isActivePlateBoundary,
  sutureDashAlpha,
} from '../src/plate-overlay';

describe('plate overlay classification', () => {
  const gridSize = 8;
  const cellCount = gridSize * gridSize;

  function fillSplitPlates(plateMap: Uint16Array): void {
    for (let i = 0; i < cellCount; i++) {
      plateMap[i] = (i % gridSize) < 4 ? 1 : 2;
    }
  }

  it('detects active plate boundaries between differing plates', () => {
    const plateMap = new Uint16Array(cellCount);
    fillSplitPlates(plateMap);

    const boundaryIndex = 3 * gridSize + 3; // row 3, col 3 — east neighbor is plate 2
    const interiorIndex = 3 * gridSize + 2; // row 3, col 2 — surrounded by plate 1
    expect(isActivePlateBoundary(plateMap, gridSize, boundaryIndex)).toBe(true);
    expect(isActivePlateBoundary(plateMap, gridSize, interiorIndex)).toBe(false);
  });

  it('classifies suture cells ahead of active boundaries', () => {
    const plateMap = new Uint16Array(cellCount);
    const sutureMap = new Uint8Array(cellCount);
    const interiorIndex = 3 * gridSize + 2;
    const sutureIndex = 3 * gridSize + 3;
    fillSplitPlates(plateMap);
    sutureMap[sutureIndex] = 1;

    expect(classifyPlateOverlayCell(plateMap, sutureMap, gridSize, sutureIndex)).toBe('suture');
    expect(classifyPlateOverlayCell(plateMap, sutureMap, gridSize, sutureIndex + 1)).toBe('active-boundary');
    expect(classifyPlateOverlayCell(plateMap, sutureMap, gridSize, interiorIndex)).toBe('interior');
  });

  it('produces a dashed pattern for suture rendering', () => {
    const dashed = [];
    const solid = [];
    for (let i = 0; i < gridSize * gridSize; i++) {
      if (sutureDashAlpha(gridSize, i) > 0) dashed.push(i);
      else solid.push(i);
    }
    expect(dashed.length).toBeGreaterThan(0);
    expect(solid.length).toBeGreaterThan(0);
  });
});
