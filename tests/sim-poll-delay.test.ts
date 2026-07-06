// P0-2: Unit tests for adaptive sim poll delay (mirrors src/main.ts constants).

import { describe, it, expect } from 'vitest';

const SIM_MIN_POLL_MS = 200;
const SIM_POLL_FACTOR = 1.05;
const SIM_DEFAULT_POLL_MS = 500;

function computeSimPollDelayMs(lastTotalMs: number | null | undefined): number {
  if (lastTotalMs == null || lastTotalMs <= 0 || !Number.isFinite(lastTotalMs)) {
    return SIM_DEFAULT_POLL_MS;
  }
  return Math.max(SIM_MIN_POLL_MS, Math.round(lastTotalMs * SIM_POLL_FACTOR));
}

describe('P0-2 — adaptive sim poll delay', () => {
  it('uses default delay when no stats are available', () => {
    expect(computeSimPollDelayMs(null)).toBe(SIM_DEFAULT_POLL_MS);
    expect(computeSimPollDelayMs(undefined)).toBe(SIM_DEFAULT_POLL_MS);
    expect(computeSimPollDelayMs(0)).toBe(SIM_DEFAULT_POLL_MS);
  });

  it('scales delay by tick duration with a safety factor', () => {
    expect(computeSimPollDelayMs(2400)).toBe(2520);
    expect(computeSimPollDelayMs(3448)).toBe(3620);
  });

  it('never schedules below the minimum poll interval', () => {
    expect(computeSimPollDelayMs(50)).toBe(SIM_MIN_POLL_MS);
    expect(computeSimPollDelayMs(100)).toBe(SIM_MIN_POLL_MS);
  });

  it('handles non-finite values with the default delay', () => {
    expect(computeSimPollDelayMs(Number.NaN)).toBe(SIM_DEFAULT_POLL_MS);
    expect(computeSimPollDelayMs(Number.POSITIVE_INFINITY)).toBe(SIM_DEFAULT_POLL_MS);
  });
});
