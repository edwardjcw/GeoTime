import { SimClock } from '../src/kernel/sim-clock';
import { EventBus } from '../src/kernel/event-bus';

describe('SimClock', () => {
  let bus: EventBus;
  let clock: SimClock;

  beforeEach(() => {
    bus = new EventBus();
    clock = new SimClock(bus);
  });

  it('should start at -4500 Ma', () => {
    expect(clock.t).toBe(-4500);
  });

  it('should start unpaused', () => {
    expect(clock.paused).toBe(false);
  });

  it('should advance time correctly when unpaused', () => {
    clock.advance(1);
    expect(clock.t).toBe(-4500 + 1);
    clock.setRate(2);
    clock.advance(1);
    expect(clock.t).toBe(-4500 + 1 + 2);
  });

  it('should not advance when paused', () => {
    clock.pause();
    clock.advance(10);
    expect(clock.t).toBe(-4500);
  });

  it('should seekTo a specific time', () => {
    clock.seekTo(-2000);
    expect(clock.t).toBe(-2000);
  });

  it('should clamp rate between 0.001 and 100', () => {
    clock.setRate(0);
    expect(clock.rate).toBe(0.001);
    clock.setRate(-5);
    expect(clock.rate).toBe(0.001);
    clock.setRate(999);
    expect(clock.rate).toBe(100);
    clock.setRate(50);
    expect(clock.rate).toBe(50);
  });

  it('should emit TICK events on advance', () => {
    const ticks: { timeMa: number; deltaMa: number }[] = [];
    bus.on('TICK', (payload) => ticks.push(payload));
    clock.advance(1);
    expect(ticks).toHaveLength(1);
    expect(ticks[0].timeMa).toBe(-4499);
    expect(ticks[0].deltaMa).toBe(1);
  });

  it('should toggle pause correctly', () => {
    expect(clock.paused).toBe(false);
    clock.togglePause();
    expect(clock.paused).toBe(true);
    clock.togglePause();
    expect(clock.paused).toBe(false);
  });
});
