import { SnapshotManager } from '../src/kernel/snapshot-manager';
import { TOTAL_BUFFER_SIZE } from '../src/shared/types';

function makeBuffer(): ArrayBuffer {
  return new ArrayBuffer(TOTAL_BUFFER_SIZE);
}

function fillBuffer(buf: ArrayBufferLike, value: number): void {
  new Uint8Array(buf).fill(value);
}

describe('SnapshotManager', () => {
  let manager: SnapshotManager;

  beforeEach(() => {
    manager = new SnapshotManager(10, 50);
  });

  it('should start with no snapshots', () => {
    expect(manager.count).toBe(0);
    expect(manager.getSnapshotTimes()).toEqual([]);
  });

  it('should take a forced snapshot', () => {
    const buf = makeBuffer();
    manager.takeSnapshot(-4500, buf);
    expect(manager.count).toBe(1);
    expect(manager.getSnapshotTimes()).toEqual([-4500]);
  });

  it('should take snapshot when interval is met', () => {
    const buf = makeBuffer();
    expect(manager.maybeTakeSnapshot(-4500, buf)).toBe(true);
    expect(manager.count).toBe(1);

    // Too soon — interval is 10 Ma
    expect(manager.maybeTakeSnapshot(-4495, buf)).toBe(false);
    expect(manager.count).toBe(1);

    // Exactly at interval
    expect(manager.maybeTakeSnapshot(-4490, buf)).toBe(true);
    expect(manager.count).toBe(2);
  });

  it('should find nearest snapshot before a given time', () => {
    const buf = makeBuffer();
    manager.takeSnapshot(-4500, buf);
    manager.takeSnapshot(-4400, buf);
    manager.takeSnapshot(-4300, buf);

    const snap = manager.findNearestBefore(-4350);
    expect(snap).not.toBeNull();
    expect(snap!.timeMa).toBe(-4400);

    const snapExact = manager.findNearestBefore(-4300);
    expect(snapExact!.timeMa).toBe(-4300);
  });

  it('should return null if no snapshot before the given time', () => {
    const buf = makeBuffer();
    manager.takeSnapshot(-4000, buf);
    expect(manager.findNearestBefore(-5000)).toBeNull();
  });

  it('should restore snapshot data into a buffer', () => {
    const srcBuf = makeBuffer();
    fillBuffer(srcBuf, 42);
    manager.takeSnapshot(-4500, srcBuf);

    const destBuf = makeBuffer();
    fillBuffer(destBuf, 0);

    const restoredTime = manager.restoreSnapshot(-4500, destBuf);
    expect(restoredTime).toBe(-4500);
    expect(new Uint8Array(destBuf)[0]).toBe(42);
    expect(new Uint8Array(destBuf)[100]).toBe(42);
  });

  it('should make independent copies (not alias the source buffer)', () => {
    const buf = makeBuffer();
    fillBuffer(buf, 10);
    manager.takeSnapshot(-4500, buf);

    // Modify the source buffer
    fillBuffer(buf, 99);

    // Restore and check snapshot preserved original data
    const destBuf = makeBuffer();
    manager.restoreSnapshot(-4500, destBuf);
    expect(new Uint8Array(destBuf)[0]).toBe(10);
  });

  it('should keep snapshots sorted by time', () => {
    const buf = makeBuffer();
    manager.takeSnapshot(-4000, buf);
    manager.takeSnapshot(-4500, buf);
    manager.takeSnapshot(-4200, buf);

    const times = manager.getSnapshotTimes();
    expect(times).toEqual([-4500, -4200, -4000]);
  });

  it('should trim oldest snapshots when exceeding maxSnapshots', () => {
    const smallManager = new SnapshotManager(1, 3);
    const buf = makeBuffer();

    for (let i = 0; i < 5; i++) {
      smallManager.takeSnapshot(-4500 + i * 10, buf);
    }

    expect(smallManager.count).toBe(3);
    const times = smallManager.getSnapshotTimes();
    expect(times[0]).toBe(-4480);
    expect(times[2]).toBe(-4460);
  });

  it('should clear all snapshots', () => {
    const buf = makeBuffer();
    manager.takeSnapshot(-4500, buf);
    manager.takeSnapshot(-4400, buf);
    manager.clear();
    expect(manager.count).toBe(0);
    expect(manager.getSnapshotTimes()).toEqual([]);
  });

  it('should restore from nearest earlier snapshot', () => {
    const buf1 = makeBuffer();
    fillBuffer(buf1, 1);
    manager.takeSnapshot(-4500, buf1);

    const buf2 = makeBuffer();
    fillBuffer(buf2, 2);
    manager.takeSnapshot(-4400, buf2);

    const dest = makeBuffer();
    const time = manager.restoreSnapshot(-4350, dest);
    expect(time).toBe(-4400);
    expect(new Uint8Array(dest)[0]).toBe(2);
  });
});
