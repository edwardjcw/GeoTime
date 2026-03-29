// ─── Plate Boundary Classifier ──────────────────────────────────────────────
// Classifies boundaries between adjacent plates as convergent, divergent, or
// transform based on relative plate velocity at each boundary cell.

import type { PlateInfo, StateBufferViews } from '../shared/types';
import { GRID_SIZE } from '../shared/types';

// ─── Types ──────────────────────────────────────────────────────────────────

export enum BoundaryType {
  NONE = 0,
  CONVERGENT = 1,
  DIVERGENT = 2,
  TRANSFORM = 3,
}

export interface BoundaryCell {
  cellIndex: number;
  type: BoundaryType;
  plate1: number;
  plate2: number;
  /** Magnitude of relative velocity (cm/yr). */
  relativeSpeed: number;
}

// ─── Helpers ────────────────────────────────────────────────────────────────

const TWO_PI = 2 * Math.PI;
const DEG2RAD = Math.PI / 180;

/** Map a grid row to latitude in radians (−π/2 … π/2). */
function rowToLat(row: number): number {
  return (Math.PI / 2) - (row / GRID_SIZE) * Math.PI;
}

/** Map a grid column to longitude in radians (−π … π). */
function colToLon(col: number): number {
  return (col / GRID_SIZE) * TWO_PI - Math.PI;
}

/**
 * Compute the velocity of a plate at a given (lat, lon) point due to its
 * Euler rotation pole. Returns [vLat, vLon] in cm/yr.
 */
export function plateVelocityAt(
  plate: PlateInfo,
  lat: number,
  lon: number,
): [number, number] {
  // Euler pole in radians
  const poleLat = plate.angularVelocity.lat * DEG2RAD;
  const poleLon = plate.angularVelocity.lon * DEG2RAD;
  const omega = plate.angularVelocity.rate; // cm/yr

  // Cross product of Euler pole with position gives velocity direction
  // on the unit sphere. Simplified for a spherical surface.
  const sinLat = Math.sin(lat);
  const cosLat = Math.cos(lat);
  const sinPoleLat = Math.sin(poleLat);
  const cosPoleLat = Math.cos(poleLat);
  const dLon = lon - poleLon;

  // Velocity components (tangent to sphere)
  const vLat = omega * (cosPoleLat * Math.sin(dLon));
  const vLon = omega * (sinPoleLat * cosLat - cosPoleLat * sinLat * Math.cos(dLon));

  return [vLat, vLon];
}

// ─── BoundaryClassifier ─────────────────────────────────────────────────────

export class BoundaryClassifier {
  /**
   * Classify all plate boundary cells.
   * A cell is on a boundary if any of its 4-connected neighbors belongs to a
   * different plate. The boundary type is determined by the relative velocity
   * of the two plates at that location.
   *
   * @param plateMap - The plate assignment map.
   * @param plates   - Array of plate info with Euler rotation parameters.
   * @returns Array of boundary cells with classifications.
   */
  classify(
    plateMap: Uint16Array,
    plates: PlateInfo[],
  ): BoundaryCell[] {
    const boundaries: BoundaryCell[] = [];
    const gridSize = GRID_SIZE;

    for (let row = 0; row < gridSize; row++) {
      for (let col = 0; col < gridSize; col++) {
        const idx = row * gridSize + col;
        const myPlate = plateMap[idx];

        // Check 4-connected neighbors (with wrapping on longitude)
        const neighbors = getNeighborIndices(row, col, gridSize);

        for (const nIdx of neighbors) {
          const neighborPlate = plateMap[nIdx];
          if (neighborPlate !== myPlate) {
            const lat = rowToLat(row);
            const lon = colToLon(col);

            const [v1Lat, v1Lon] = plateVelocityAt(plates[myPlate], lat, lon);
            const [v2Lat, v2Lon] = plateVelocityAt(plates[neighborPlate], lat, lon);

            // Relative velocity
            const dvLat = v1Lat - v2Lat;
            const dvLon = v1Lon - v2Lon;
            const relSpeed = Math.sqrt(dvLat * dvLat + dvLon * dvLon);

            // Normal direction from plate1 towards plate2
            const nRow = Math.floor(nIdx / gridSize);
            const nCol = nIdx % gridSize;
            const nLat = rowToLat(nRow);
            const nLon = colToLon(nCol);
            const normalLat = nLat - lat;
            const normalLon = nLon - lon;
            const normalLen = Math.sqrt(normalLat * normalLat + normalLon * normalLon);

            let boundaryType = BoundaryType.TRANSFORM;
            if (normalLen > 1e-10) {
              // Dot product of relative velocity with boundary normal
              const dot = (dvLat * normalLat + dvLon * normalLon) / normalLen;
              const threshold = relSpeed * 0.3; // 30% threshold for transform

              if (dot < -threshold) {
                boundaryType = BoundaryType.CONVERGENT;
              } else if (dot > threshold) {
                boundaryType = BoundaryType.DIVERGENT;
              }
            }

            boundaries.push({
              cellIndex: idx,
              type: boundaryType,
              plate1: myPlate,
              plate2: neighborPlate,
              relativeSpeed: relSpeed,
            });
            break; // Only record this cell once
          }
        }
      }
    }

    return boundaries;
  }
}

/**
 * Get 4-connected neighbor cell indices with longitude wrapping.
 * Latitude is clamped (no wrapping at poles).
 */
export function getNeighborIndices(
  row: number,
  col: number,
  gridSize: number,
): number[] {
  const neighbors: number[] = [];

  // Up
  if (row > 0) {
    neighbors.push((row - 1) * gridSize + col);
  }
  // Down
  if (row < gridSize - 1) {
    neighbors.push((row + 1) * gridSize + col);
  }
  // Left (wrap longitude)
  const leftCol = (col - 1 + gridSize) % gridSize;
  neighbors.push(row * gridSize + leftCol);
  // Right (wrap longitude)
  const rightCol = (col + 1) % gridSize;
  neighbors.push(row * gridSize + rightCol);

  return neighbors;
}
