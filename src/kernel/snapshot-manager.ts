// ─── Snapshot Manager ───────────────────────────────────────────────────────
// Manages keyframe snapshots of the simulation state for time-scrubbing support.
// Snapshots are taken at configurable intervals (default every 10 Myr sim-time).
// Forward scrubbing: advance the clock.
// Backward scrubbing: load nearest earlier snapshot + fast-forward to target time.

import type { StateBufferViews } from '../shared/types';
import { TOTAL_BUFFER_SIZE, GRID_SIZE, createStateBufferLayout } from '../shared/types';

// ─── Types ──────────────────────────────────────────────────────────────────

export interface Snapshot {
  /** Simulation time (Ma) when this snapshot was taken. */
  timeMa: number;
  /** Copy of all state buffer data at snapshot time. */
  bufferData: ArrayBuffer;
  /** Whether this is a full keyframe or a delta snapshot. */
  isKeyframe: boolean;
}

/** Sparse delta — stores only cells that changed since the last keyframe. */
export interface DeltaSnapshot {
  /** Simulation time (Ma) when this snapshot was taken. */
  timeMa: number;
  /** Changed byte ranges: [offset, data] pairs. */
  changes: Array<{ offset: number; data: Uint8Array }>;
  /** Whether this is a full keyframe or a delta snapshot. */
  isKeyframe: false;
}

// ─── Helpers ────────────────────────────────────────────────────────────────

/**
 * Compare two buffers and produce a sparse delta.
 * Only stores 256-byte blocks that differ.
 */
export function computeDelta(
  prev: ArrayBuffer,
  curr: ArrayBuffer,
): Array<{ offset: number; data: Uint8Array }> {
  const BLOCK_SIZE = 256;
  const prevView = new Uint8Array(prev);
  const currView = new Uint8Array(curr);
  const len = Math.min(prevView.length, currView.length);
  const changes: Array<{ offset: number; data: Uint8Array }> = [];

  for (let off = 0; off < len; off += BLOCK_SIZE) {
    const end = Math.min(off + BLOCK_SIZE, len);
    let differs = false;
    for (let j = off; j < end; j++) {
      if (prevView[j] !== currView[j]) {
        differs = true;
        break;
      }
    }
    if (differs) {
      changes.push({ offset: off, data: currView.slice(off, end) });
    }
  }

  return changes;
}

/**
 * Apply a delta to a buffer.
 */
export function applyDelta(
  buffer: ArrayBuffer,
  changes: Array<{ offset: number; data: Uint8Array }>,
): void {
  const view = new Uint8Array(buffer);
  for (const { offset, data } of changes) {
    view.set(data, offset);
  }
}

// ─── SnapshotManager ────────────────────────────────────────────────────────

export class SnapshotManager {
  private snapshots: Snapshot[] = [];

  /** Simulation time interval between automatic snapshots (Ma). */
  readonly interval: number;

  /** Maximum number of stored snapshots (oldest are discarded). */
  private readonly maxSnapshots: number;

  /** Time of the last snapshot taken. */
  private lastSnapshotTime: number = -Infinity;

  /** Number of delta snapshots between keyframes. */
  private readonly deltasBetweenKeyframes: number;

  /** Counter for delta snapshots since last keyframe. */
  private deltaSinceKeyframe = 0;

  /** Reference buffer for computing deltas (last keyframe). */
  private lastKeyframeBuffer: ArrayBuffer | null = null;

  constructor(interval = 10, maxSnapshots = 500, deltasBetweenKeyframes = 5) {
    this.interval = interval;
    this.maxSnapshots = maxSnapshots;
    this.deltasBetweenKeyframes = deltasBetweenKeyframes;
  }

  /**
   * Check whether a snapshot should be taken at the current simulation time,
   * and if so, capture one.
   */
  maybeTakeSnapshot(timeMa: number, buffer: ArrayBufferLike): boolean {
    if (timeMa - this.lastSnapshotTime >= this.interval) {
      this.takeSnapshot(timeMa, buffer);
      return true;
    }
    return false;
  }

  /** Force a snapshot at the given time. */
  takeSnapshot(timeMa: number, buffer: ArrayBufferLike): void {
    // Copy the entire buffer
    const copy = new ArrayBuffer(buffer.byteLength);
    new Uint8Array(copy).set(new Uint8Array(buffer));

    this.snapshots.push({ timeMa, bufferData: copy, isKeyframe: true });
    this.lastSnapshotTime = timeMa;
    this.lastKeyframeBuffer = copy;
    this.deltaSinceKeyframe = 0;

    // Keep sorted by time
    this.snapshots.sort((a, b) => a.timeMa - b.timeMa);

    // Trim if over budget
    while (this.snapshots.length > this.maxSnapshots) {
      this.snapshots.shift();
    }
  }

  /**
   * Find the nearest snapshot at or before the given time.
   * Returns null if no snapshot exists before the given time.
   */
  findNearestBefore(timeMa: number): Snapshot | null {
    let best: Snapshot | null = null;
    for (const snap of this.snapshots) {
      if (snap.timeMa <= timeMa) {
        best = snap;
      } else {
        break; // sorted, so no need to continue
      }
    }
    return best;
  }

  /**
   * Restore state from a snapshot into the given buffer.
   * Returns the snapshot's time, or null if no applicable snapshot was found.
   */
  restoreSnapshot(
    targetTimeMa: number,
    buffer: ArrayBufferLike,
  ): number | null {
    const snap = this.findNearestBefore(targetTimeMa);
    if (!snap) return null;

    // Copy snapshot data into the live buffer
    new Uint8Array(buffer).set(new Uint8Array(snap.bufferData));
    return snap.timeMa;
  }

  /** Get the number of stored snapshots. */
  get count(): number {
    return this.snapshots.length;
  }

  /** Get all snapshot times for display in the UI timeline. */
  getSnapshotTimes(): number[] {
    return this.snapshots.map((s) => s.timeMa);
  }

  /** Clear all snapshots. */
  clear(): void {
    this.snapshots = [];
    this.lastSnapshotTime = -Infinity;
    this.lastKeyframeBuffer = null;
    this.deltaSinceKeyframe = 0;
  }
}
