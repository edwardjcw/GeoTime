// P1-3: Unit tests for throttled inspect auto-refresh (mirrors src/main.ts logic).

import { describe, it, expect } from 'vitest';

const INSPECT_REFRESH_INTERVAL_MS = 5000;

function shouldAutoRefreshInspectCell(
  panelVisible: boolean,
  cellIndex: number,
  nowMs: number,
  lastRefreshMs: number,
  pending: boolean,
  intervalMs: number,
): boolean {
  if (cellIndex < 0) return false;
  if (!panelVisible) return false;
  if (pending) return false;
  if (nowMs - lastRefreshMs < intervalMs) return false;
  return true;
}

describe('P1-3 — inspect refresh throttle', () => {
  it('skips when no cell is pinned', () => {
    expect(
      shouldAutoRefreshInspectCell(true, -1, 10_000, 0, false, INSPECT_REFRESH_INTERVAL_MS),
    ).toBe(false);
  });

  it('skips when the inspect panel is hidden', () => {
    expect(
      shouldAutoRefreshInspectCell(false, 42, 10_000, 0, false, INSPECT_REFRESH_INTERVAL_MS),
    ).toBe(false);
  });

  it('skips while a refresh request is in flight', () => {
    expect(
      shouldAutoRefreshInspectCell(true, 42, 10_000, 0, true, INSPECT_REFRESH_INTERVAL_MS),
    ).toBe(false);
  });

  it('skips when the throttle interval has not elapsed', () => {
    expect(
      shouldAutoRefreshInspectCell(true, 42, 4000, 0, false, INSPECT_REFRESH_INTERVAL_MS),
    ).toBe(false);
  });

  it('allows refresh after the interval on a pinned visible panel', () => {
    expect(
      shouldAutoRefreshInspectCell(true, 42, 5000, 0, false, INSPECT_REFRESH_INTERVAL_MS),
    ).toBe(true);
    expect(
      shouldAutoRefreshInspectCell(true, 42, 12_000, 7000, false, INSPECT_REFRESH_INTERVAL_MS),
    ).toBe(true);
  });
});
