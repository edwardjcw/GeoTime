// ─── Cross-Section Engine ───────────────────────────────────────────────────
// AGENT-SECTION: Samples stratigraphy along a user-drawn polyline path on the
// globe surface, producing a CrossSectionProfile for 2D rendering.
// Great-circle interpolation, lat/lon → grid cell mapping, and deep earth
// zone construction.

import type {
  LatLon,
  StateBufferViews,
  CrossSectionSample,
  CrossSectionProfile,
  DeepEarthZone,
  CrossSectionPathPayload,
} from '../shared/types';
import { GRID_SIZE, RockType, SoilOrder } from '../shared/types';
import type { StratigraphyStack } from './stratigraphy';
import type { EventBus } from '../kernel/event-bus';

// ─── Constants ──────────────────────────────────────────────────────────────

const DEG2RAD = Math.PI / 180;
const RAD2DEG = 180 / Math.PI;
const TWO_PI = 2 * Math.PI;

/** Earth radius in km. */
export const EARTH_RADIUS_KM = 6371;

/** Default number of sample points along the path. */
export const DEFAULT_SAMPLE_COUNT = 512;

// ─── Deep Earth Zones ───────────────────────────────────────────────────────

/** Standard deep-earth zones below the crust, constant for Earth-analog. */
export function getDeepEarthZones(): DeepEarthZone[] {
  return [
    { name: 'Lithospheric Mantle', topKm: 30,   bottomKm: 100,  rockType: RockType.DEEP_LITHMAN },
    { name: 'Asthenosphere',       topKm: 100,  bottomKm: 410,  rockType: RockType.DEEP_ASTHEN },
    { name: 'Transition Zone',     topKm: 410,  bottomKm: 660,  rockType: RockType.DEEP_TRANS },
    { name: 'Lower Mantle',        topKm: 660,  bottomKm: 2891, rockType: RockType.DEEP_LOWMAN },
    { name: 'Core-Mantle Boundary',topKm: 2891, bottomKm: 2921, rockType: RockType.DEEP_CMB },
    { name: 'Outer Core',          topKm: 2921, bottomKm: 5150, rockType: RockType.DEEP_OUTCORE },
    { name: 'Inner Core',          topKm: 5150, bottomKm: 6371, rockType: RockType.DEEP_INCORE },
  ];
}

// ─── Coordinate Helpers ─────────────────────────────────────────────────────

/** Convert latitude (degrees, -90..90) to grid row. */
export function latToRow(latDeg: number): number {
  // Row 0 = +90° (north pole), row GRID_SIZE-1 = −90° (south pole)
  const clamped = Math.max(-90, Math.min(90, latDeg));
  return Math.round(((90 - clamped) / 180) * (GRID_SIZE - 1));
}

/** Convert longitude (degrees, -180..180) to grid column. */
export function lonToCol(lonDeg: number): number {
  // Normalise to -180..180
  let lon = lonDeg % 360;
  if (lon > 180) lon -= 360;
  if (lon < -180) lon += 360;
  return Math.round(((lon + 180) / 360) * (GRID_SIZE - 1));
}

/** Convert lat/lon (degrees) to flat cell index. */
export function latLonToIndex(latDeg: number, lonDeg: number): number {
  const row = latToRow(latDeg);
  const col = lonToCol(lonDeg);
  return row * GRID_SIZE + col;
}

// ─── Great-Circle Interpolation ─────────────────────────────────────────────

/**
 * Compute the central angle (radians) between two lat/lon points on a sphere
 * using the Haversine formula.
 */
export function centralAngle(
  lat1Deg: number, lon1Deg: number,
  lat2Deg: number, lon2Deg: number,
): number {
  const φ1 = lat1Deg * DEG2RAD;
  const φ2 = lat2Deg * DEG2RAD;
  const Δφ = (lat2Deg - lat1Deg) * DEG2RAD;
  const Δλ = (lon2Deg - lon1Deg) * DEG2RAD;

  const a = Math.sin(Δφ / 2) ** 2 +
            Math.cos(φ1) * Math.cos(φ2) * Math.sin(Δλ / 2) ** 2;
  return 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
}

/**
 * Interpolate along a great-circle arc between two points.
 * @param t - Fraction along the arc (0 → p1, 1 → p2).
 * @returns Interpolated {lat, lon} in degrees.
 */
export function greatCircleInterpolate(
  lat1Deg: number, lon1Deg: number,
  lat2Deg: number, lon2Deg: number,
  t: number,
): LatLon {
  const d = centralAngle(lat1Deg, lon1Deg, lat2Deg, lon2Deg);

  // Degenerate case: same point (or nearly)
  if (d < 1e-12) {
    return { lat: lat1Deg, lon: lon1Deg };
  }

  const sinD = Math.sin(d);
  const a = Math.sin((1 - t) * d) / sinD;
  const b = Math.sin(t * d) / sinD;

  const φ1 = lat1Deg * DEG2RAD;
  const λ1 = lon1Deg * DEG2RAD;
  const φ2 = lat2Deg * DEG2RAD;
  const λ2 = lon2Deg * DEG2RAD;

  const x = a * Math.cos(φ1) * Math.cos(λ1) + b * Math.cos(φ2) * Math.cos(λ2);
  const y = a * Math.cos(φ1) * Math.sin(λ1) + b * Math.cos(φ2) * Math.sin(λ2);
  const z = a * Math.sin(φ1) + b * Math.sin(φ2);

  const lat = Math.atan2(z, Math.sqrt(x * x + y * y)) * RAD2DEG;
  const lon = Math.atan2(y, x) * RAD2DEG;

  return { lat, lon };
}

/**
 * Compute the total path distance in km across a polyline of lat/lon points.
 */
export function computePathDistanceKm(points: LatLon[]): number {
  let total = 0;
  for (let i = 0; i < points.length - 1; i++) {
    const angle = centralAngle(
      points[i].lat, points[i].lon,
      points[i + 1].lat, points[i + 1].lon,
    );
    total += angle * EARTH_RADIUS_KM;
  }
  return total;
}

/**
 * Generate N equally-spaced sample points along a polyline path.
 * Each segment is a great-circle arc.
 */
export function samplePathPoints(
  pathPoints: LatLon[],
  numSamples: number = DEFAULT_SAMPLE_COUNT,
): LatLon[] {
  if (pathPoints.length === 0) return [];
  if (pathPoints.length === 1) return [{ ...pathPoints[0] }];

  // Compute cumulative arc-length distances for each path segment
  const segLengths: number[] = [];
  let totalAngle = 0;
  for (let i = 0; i < pathPoints.length - 1; i++) {
    const angle = centralAngle(
      pathPoints[i].lat, pathPoints[i].lon,
      pathPoints[i + 1].lat, pathPoints[i + 1].lon,
    );
    segLengths.push(angle);
    totalAngle += angle;
  }

  if (totalAngle < 1e-12) {
    // All points at essentially the same location
    return Array.from({ length: numSamples }, () => ({ ...pathPoints[0] }));
  }

  // Build cumulative distances
  const cumDist: number[] = [0];
  for (let i = 0; i < segLengths.length; i++) {
    cumDist.push(cumDist[i] + segLengths[i]);
  }

  const samples: LatLon[] = [];
  for (let s = 0; s < numSamples; s++) {
    const frac = numSamples > 1 ? s / (numSamples - 1) : 0;
    const targetDist = frac * totalAngle;

    // Find which segment this sample falls in
    let segIdx = 0;
    while (segIdx < segLengths.length - 1 && cumDist[segIdx + 1] < targetDist) {
      segIdx++;
    }

    // Interpolate within this segment
    const segStart = cumDist[segIdx];
    const segLen = segLengths[segIdx];
    const t = segLen > 1e-12 ? (targetDist - segStart) / segLen : 0;

    const p = greatCircleInterpolate(
      pathPoints[segIdx].lat, pathPoints[segIdx].lon,
      pathPoints[segIdx + 1].lat, pathPoints[segIdx + 1].lon,
      Math.max(0, Math.min(1, t)),
    );
    samples.push(p);
  }

  return samples;
}

// ─── Cross-Section Engine ───────────────────────────────────────────────────

export interface CrossSectionEngineConfig {
  /** Number of sample points along the path (default 512). */
  sampleCount?: number;
}

export class CrossSectionEngine {
  private readonly bus: EventBus;
  private readonly sampleCount: number;

  private stateViews: StateBufferViews | null = null;
  private stratigraphy: StratigraphyStack | null = null;

  /** Currently active cross-section profile, or null if none. */
  private currentProfile: CrossSectionProfile | null = null;

  /** Whether labels are visible. */
  private labelsVisible = true;

  constructor(bus: EventBus, config?: CrossSectionEngineConfig) {
    this.bus = bus;
    this.sampleCount = config?.sampleCount ?? DEFAULT_SAMPLE_COUNT;

    // Listen for cross-section path requests
    this.bus.on('CROSS_SECTION_PATH', (payload: { points: LatLon[] }) => {
      this.processPath(payload.points);
    });

    // Listen for label toggles
    this.bus.on('LABEL_TOGGLE', (payload: { visible: boolean }) => {
      this.labelsVisible = payload.visible;
    });
  }

  /** Initialise with state views and stratigraphy reference. */
  initialize(stateViews: StateBufferViews, stratigraphy: StratigraphyStack): void {
    this.stateViews = stateViews;
    this.stratigraphy = stratigraphy;
  }

  /** Get the currently active profile (if any). */
  getProfile(): CrossSectionProfile | null {
    return this.currentProfile;
  }

  /** Get label visibility state. */
  getLabelsVisible(): boolean {
    return this.labelsVisible;
  }

  /**
   * Process a cross-section path: sample the polyline, retrieve stratigraphy,
   * and emit the CROSS_SECTION_READY event.
   */
  processPath(points: LatLon[]): CrossSectionProfile | null {
    if (!this.stateViews || !this.stratigraphy) return null;
    if (points.length < 2) return null;

    const profile = this.buildProfile(points, this.stateViews, this.stratigraphy);
    this.currentProfile = profile;

    this.bus.emit('CROSS_SECTION_READY', { profile });

    return profile;
  }

  /**
   * Build a full cross-section profile from a polyline path.
   * Public for testing; normally called via processPath().
   */
  buildProfile(
    pathPoints: LatLon[],
    stateViews: StateBufferViews,
    stratigraphy: StratigraphyStack,
  ): CrossSectionProfile {
    const samplePositions = samplePathPoints(pathPoints, this.sampleCount);
    const totalDistanceKm = computePathDistanceKm(pathPoints);

    const samples: CrossSectionSample[] = samplePositions.map((pos, i) => {
      const distanceKm = this.sampleCount > 1
        ? (i / (this.sampleCount - 1)) * totalDistanceKm
        : 0;

      const cellIndex = latLonToIndex(pos.lat, pos.lon);

      return {
        distanceKm,
        surfaceElevation: stateViews.heightMap[cellIndex] ?? 0,
        crustThicknessKm: (stateViews.crustThicknessMap[cellIndex] ?? 30000) / 1000,
        soilType: stateViews.soilTypeMap[cellIndex] as SoilOrder ?? SoilOrder.NONE,
        soilDepthM: stateViews.soilDepthMap[cellIndex] ?? 0,
        layers: [...stratigraphy.getLayers(cellIndex)],
      };
    });

    return {
      samples,
      totalDistanceKm,
      pathPoints: pathPoints.map(p => ({ ...p })),
      deepEarthZones: getDeepEarthZones(),
    };
  }

  /** Clear the current profile. */
  clear(): void {
    this.currentProfile = null;
  }
}
