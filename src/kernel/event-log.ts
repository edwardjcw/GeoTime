// ─── Geological Event Log ───────────────────────────────────────────────────
// Records significant geological events (eruptions, collisions, rifts) with
// timestamps for display in the UI timeline and for snapshot replay.

import type { GeoEventType } from '../shared/types';

// ─── Types ──────────────────────────────────────────────────────────────────

export interface GeoLogEntry {
  /** Simulation time in Ma when the event occurred. */
  timeMa: number;
  /** Event type identifier. */
  type: GeoEventType;
  /** Human-readable description. */
  description: string;
  /** Optional cell index or lat/lon where the event occurred. */
  location?: { lat: number; lon: number };
}

// ─── EventLog ───────────────────────────────────────────────────────────────

export class EventLog {
  private entries: GeoLogEntry[] = [];

  /** Maximum stored entries to prevent unbounded growth. */
  private readonly maxEntries: number;

  constructor(maxEntries = 10_000) {
    this.maxEntries = maxEntries;
  }

  /** Record a new event. */
  record(entry: GeoLogEntry): void {
    this.entries.push(entry);
    // Trim oldest if over budget
    if (this.entries.length > this.maxEntries) {
      this.entries.splice(0, this.entries.length - this.maxEntries);
    }
  }

  /** Get all entries. */
  getAll(): ReadonlyArray<GeoLogEntry> {
    return this.entries;
  }

  /** Get entries within a time range [startMa, endMa]. */
  getRange(startMa: number, endMa: number): GeoLogEntry[] {
    return this.entries.filter(
      (e) => e.timeMa >= startMa && e.timeMa <= endMa,
    );
  }

  /** Get entries of a specific event type. */
  getByType(type: GeoEventType): GeoLogEntry[] {
    return this.entries.filter((e) => e.type === type);
  }

  /** Get the N most recent entries. */
  getRecent(count: number): GeoLogEntry[] {
    return this.entries.slice(-count);
  }

  /** Total number of recorded events. */
  get length(): number {
    return this.entries.length;
  }

  /** Clear all entries. */
  clear(): void {
    this.entries = [];
  }
}
