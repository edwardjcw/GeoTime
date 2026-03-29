import type { EventBus } from './event-bus';

const MIN_RATE = 0.001;
const MAX_RATE = 100;
const INITIAL_TIME = -4500;

export class SimClock {
  t: number = INITIAL_TIME;
  rate: number = 1;
  paused: boolean = false;

  private bus: EventBus;

  constructor(bus: EventBus) {
    this.bus = bus;
  }

  advance(dtReal: number): void {
    if (this.paused) return;
    const dtMa = dtReal * this.rate;
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
}
