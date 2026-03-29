import { EventLog, type GeoLogEntry } from '../src/kernel/event-log';

describe('EventLog', () => {
  let log: EventLog;

  beforeEach(() => {
    log = new EventLog();
  });

  it('should start empty', () => {
    expect(log.length).toBe(0);
    expect(log.getAll()).toEqual([]);
  });

  it('should record and retrieve entries', () => {
    log.record({
      timeMa: -4000,
      type: 'VOLCANIC_ERUPTION',
      description: 'Test eruption',
    });
    expect(log.length).toBe(1);
    const all = log.getAll();
    expect(all[0].type).toBe('VOLCANIC_ERUPTION');
    expect(all[0].timeMa).toBe(-4000);
  });

  it('should record entries with optional location', () => {
    log.record({
      timeMa: -3500,
      type: 'VOLCANIC_ERUPTION',
      description: 'Located eruption',
      location: { lat: 30, lon: 45 },
    });
    expect(log.getAll()[0].location).toEqual({ lat: 30, lon: 45 });
  });

  it('should filter by time range', () => {
    log.record({ timeMa: -4000, type: 'VOLCANIC_ERUPTION', description: 'a' });
    log.record({ timeMa: -3000, type: 'PLATE_COLLISION', description: 'b' });
    log.record({ timeMa: -2000, type: 'PLATE_RIFT', description: 'c' });
    log.record({ timeMa: -1000, type: 'VOLCANIC_ERUPTION', description: 'd' });

    const range = log.getRange(-3500, -1500);
    expect(range).toHaveLength(2);
    expect(range[0].timeMa).toBe(-3000);
    expect(range[1].timeMa).toBe(-2000);
  });

  it('should filter by event type', () => {
    log.record({ timeMa: -4000, type: 'VOLCANIC_ERUPTION', description: 'a' });
    log.record({ timeMa: -3000, type: 'PLATE_COLLISION', description: 'b' });
    log.record({ timeMa: -2000, type: 'VOLCANIC_ERUPTION', description: 'c' });

    const eruptions = log.getByType('VOLCANIC_ERUPTION');
    expect(eruptions).toHaveLength(2);
    expect(eruptions.every((e) => e.type === 'VOLCANIC_ERUPTION')).toBe(true);
  });

  it('should return recent entries', () => {
    for (let i = 0; i < 10; i++) {
      log.record({ timeMa: -4000 + i * 100, type: 'TICK', description: `tick ${i}` });
    }
    const recent = log.getRecent(3);
    expect(recent).toHaveLength(3);
    expect(recent[0].timeMa).toBe(-3300);
    expect(recent[2].timeMa).toBe(-3100);
  });

  it('should respect maxEntries limit', () => {
    const smallLog = new EventLog(5);
    for (let i = 0; i < 10; i++) {
      smallLog.record({ timeMa: i, type: 'TICK', description: `${i}` });
    }
    expect(smallLog.length).toBe(5);
    // Oldest entries should be discarded
    const all = smallLog.getAll();
    expect(all[0].timeMa).toBe(5);
    expect(all[4].timeMa).toBe(9);
  });

  it('should clear all entries', () => {
    log.record({ timeMa: -4000, type: 'VOLCANIC_ERUPTION', description: 'test' });
    log.clear();
    expect(log.length).toBe(0);
    expect(log.getAll()).toEqual([]);
  });
});
