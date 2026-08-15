/** Classify a cell for plate-overlay rendering. */
export type PlateOverlayKind = 'interior' | 'active-boundary' | 'suture';

export function isActivePlateBoundary(
  plateMap: Uint16Array | readonly number[],
  gridSize: number,
  index: number,
): boolean {
  const plate = plateMap[index];
  if (plate === 0) return false;

  const row = Math.floor(index / gridSize);
  const col = index % gridSize;

  const neighbors = [
    row > 0 ? plateMap[(row - 1) * gridSize + col] : plate,
    row < gridSize - 1 ? plateMap[(row + 1) * gridSize + col] : plate,
    plateMap[row * gridSize + ((col - 1 + gridSize) % gridSize)],
    plateMap[row * gridSize + ((col + 1) % gridSize)],
  ];

  return neighbors.some((n) => n !== 0 && n !== plate);
}

export function classifyPlateOverlayCell(
  plateMap: Uint16Array | readonly number[],
  sutureMap: Uint8Array | readonly number[],
  gridSize: number,
  index: number,
): PlateOverlayKind {
  if (sutureMap[index] > 0) return 'suture';
  if (isActivePlateBoundary(plateMap, gridSize, index)) return 'active-boundary';
  return 'interior';
}

/** Dashed alpha pattern for suture cells (0 = gap, 1 = visible). */
export function sutureDashAlpha(gridSize: number, index: number): number {
  const row = Math.floor(index / gridSize);
  const col = index % gridSize;
  return ((row + col) % 3 === 0) ? 1 : 0;
}
