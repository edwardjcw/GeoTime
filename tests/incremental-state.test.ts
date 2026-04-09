// @vitest-environment jsdom
// S8: Tests for incremental state data handling in the SimulationEventHandler.

import { describe, it, expect } from 'vitest';
import type { SimulationEventHandler } from '../src/api/backend-client';

describe('S8 — Incremental State Data', () => {
  it('SimulationEventHandler type includes onIncrementalStateData', () => {
    // Verify the handler type accepts the new incremental state callback.
    const handler: SimulationEventHandler = {
      onIncrementalStateData: (event) => {
        expect(event.phase).toBeDefined();
        expect(event.heightMap).toBeDefined();
      },
    };
    expect(handler.onIncrementalStateData).toBeDefined();
  });

  it('onIncrementalStateData receives phase and heightMap', () => {
    const received: Array<{ phase: string; heightMap: ArrayBuffer }> = [];
    const handler: SimulationEventHandler = {
      onIncrementalStateData: (event) => {
        received.push(event);
      },
    };

    // Simulate receiving incremental data
    const fakeHeightMap = new Float32Array([100, 200, 300, 400]).buffer;
    handler.onIncrementalStateData!({
      phase: 'tectonic:collision',
      heightMap: fakeHeightMap,
    });

    expect(received).toHaveLength(1);
    expect(received[0]!.phase).toBe('tectonic:collision');
    expect(received[0]!.heightMap.byteLength).toBe(4 * Float32Array.BYTES_PER_ELEMENT);
  });

  it('incremental height map can be decoded as Float32Array', () => {
    const originalData = new Float32Array([1500.5, -3000.2, 0, 8848.8]);
    const buffer = originalData.buffer.slice(0);

    const decoded = new Float32Array(buffer);
    expect(decoded).toHaveLength(4);
    expect(decoded[0]).toBeCloseTo(1500.5, 1);
    expect(decoded[1]).toBeCloseTo(-3000.2, 1);
    expect(decoded[2]).toBe(0);
    expect(decoded[3]).toBeCloseTo(8848.8, 1);
  });

  it('stale incremental updates are ignored when not pending', () => {
    // Simulates the main.ts guard: only process when pendingSimRequest is true
    let pendingSimRequest = false;
    let updateCount = 0;

    const handler: SimulationEventHandler = {
      onIncrementalStateData: () => {
        if (!pendingSimRequest) return;
        updateCount++;
      },
    };

    // Should be ignored (not pending)
    handler.onIncrementalStateData!({
      phase: 'tectonic:collision',
      heightMap: new ArrayBuffer(16),
    });
    expect(updateCount).toBe(0);

    // Should be processed (pending)
    pendingSimRequest = true;
    handler.onIncrementalStateData!({
      phase: 'tectonic:collision',
      heightMap: new ArrayBuffer(16),
    });
    expect(updateCount).toBe(1);
  });
});
