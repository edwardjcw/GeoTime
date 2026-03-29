import { EventBus } from '../src/kernel/event-bus';

describe('EventBus', () => {
  let bus: EventBus;

  beforeEach(() => {
    bus = new EventBus();
  });

  it('should subscribe and receive events', () => {
    const received: { timeMa: number; deltaMa: number }[] = [];
    bus.on('TICK', (payload) => received.push(payload));
    bus.emit('TICK', { timeMa: 100, deltaMa: 1 });
    expect(received).toHaveLength(1);
    expect(received[0]).toEqual({ timeMa: 100, deltaMa: 1 });
  });

  it('should support multiple subscribers on the same topic', () => {
    let count1 = 0;
    let count2 = 0;
    bus.on('TICK', () => { count1++; });
    bus.on('TICK', () => { count2++; });
    bus.emit('TICK', { timeMa: 0, deltaMa: 0 });
    expect(count1).toBe(1);
    expect(count2).toBe(1);
  });

  it('should unsubscribe correctly via off', () => {
    let count = 0;
    const cb = () => { count++; };
    bus.on('TICK', cb);
    bus.emit('TICK', { timeMa: 0, deltaMa: 0 });
    expect(count).toBe(1);
    bus.off('TICK', cb);
    bus.emit('TICK', { timeMa: 0, deltaMa: 0 });
    expect(count).toBe(1);
  });

  it('should unsubscribe correctly via the returned function', () => {
    let count = 0;
    const unsub = bus.on('TICK', () => { count++; });
    bus.emit('TICK', { timeMa: 0, deltaMa: 0 });
    expect(count).toBe(1);
    unsub();
    bus.emit('TICK', { timeMa: 0, deltaMa: 0 });
    expect(count).toBe(1);
  });

  it('should not receive events after unsubscribing', () => {
    const received: number[] = [];
    const unsub = bus.on('TICK', (p) => received.push(p.timeMa));
    bus.emit('TICK', { timeMa: 1, deltaMa: 0 });
    unsub();
    bus.emit('TICK', { timeMa: 2, deltaMa: 0 });
    expect(received).toEqual([1]);
  });

  it('should clear all subscribers', () => {
    let count = 0;
    bus.on('TICK', () => { count++; });
    bus.on('VOLCANIC_ERUPTION', () => { count++; });
    bus.clear();
    bus.emit('TICK', { timeMa: 0, deltaMa: 0 });
    bus.emit('VOLCANIC_ERUPTION', { lat: 0, lon: 0, intensity: 1 });
    expect(count).toBe(0);
  });

  it('should handle emitting with no subscribers without error', () => {
    expect(() => {
      bus.emit('TICK', { timeMa: 0, deltaMa: 0 });
    }).not.toThrow();
  });
});
