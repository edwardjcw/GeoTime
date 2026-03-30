import type { EventBus } from './event-bus';

const MIN_RATE = 0.001;
const MAX_RATE = 100;
const INITIAL_TIME = -4500;

export interface SimClockConfig {
  /** Maximum real-time budget per advance call in seconds (adaptive tick rate).
   *  Caps dtReal to avoid runaway simulation when at max speed.
   *  Defaults to Infinity (no cap). */
  maxFrameBudget?: number;
}

export class SimClock {
  t: number = INITIAL_TIME;
  rate: number = 1;
  paused: boolean = false;

  /** Adaptive tick rate budget cap (seconds). */
  private readonly maxFrameBudget: number;

  /** Last effective real dt (after cap). */
  private _lastDtReal: number = 0;

  private bus: EventBus;

  constructor(bus: EventBus, config?: SimClockConfig) {
    this.bus = bus;
    this.maxFrameBudget = config?.maxFrameBudget ?? Infinity;
  }

  advance(dtReal: number): void {
    if (this.paused) return;

    // Adaptive tick rate: cap dt to avoid runaway when at max speed
    const cappedDt = Math.min(dtReal, this.maxFrameBudget);
    this._lastDtReal = cappedDt;

    const dtMa = cappedDt * this.rate;
    this.t += dtMa;
    this.bus.emit('TICK', { timeMa: this.t, deltaMa: dtMa });
  }

  seekTo(t: number): void {
    this.t = t;
  }

  pause(): void {
    this.paused = true;
  }

  resume(): void {
    this.paused = false;
  }

  togglePause(): void {
    this.paused = !this.paused;
  }

  setRate(rate: number): void {
    this.rate = Math.max(MIN_RATE, Math.min(MAX_RATE, rate));
  }

  /** Get the last real dt (capped by adaptive tick rate). */
  get lastDtReal(): number {
    return this._lastDtReal;
  }
}
