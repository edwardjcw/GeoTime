import { Xoshiro256ss } from '../src/proc/prng';

describe('Xoshiro256ss', () => {
  it('should produce deterministic results given the same seed', () => {
    const rng1 = new Xoshiro256ss(42);
    const rng2 = new Xoshiro256ss(42);
    const values1 = Array.from({ length: 20 }, () => rng1.next());
    const values2 = Array.from({ length: 20 }, () => rng2.next());
    expect(values1).toEqual(values2);
  });

  it('should produce different results for different seeds', () => {
    const rng1 = new Xoshiro256ss(1);
    const rng2 = new Xoshiro256ss(2);
    const v1 = Array.from({ length: 5 }, () => rng1.next());
    const v2 = Array.from({ length: 5 }, () => rng2.next());
    expect(v1).not.toEqual(v2);
  });

  it('should produce values in [0, 1) from next()', () => {
    const rng = new Xoshiro256ss(123);
    for (let i = 0; i < 1000; i++) {
      const v = rng.next();
      expect(v).toBeGreaterThanOrEqual(0);
      expect(v).toBeLessThan(1);
    }
  });

  it('nextInt should return integers in range', () => {
    const rng = new Xoshiro256ss(99);
    for (let i = 0; i < 500; i++) {
      const v = rng.nextInt(5, 15);
      expect(Number.isInteger(v)).toBe(true);
      expect(v).toBeGreaterThanOrEqual(5);
      expect(v).toBeLessThanOrEqual(15);
    }
  });

  it('nextFloat should return floats in range', () => {
    const rng = new Xoshiro256ss(77);
    for (let i = 0; i < 500; i++) {
      const v = rng.nextFloat(2.5, 7.5);
      expect(v).toBeGreaterThanOrEqual(2.5);
      expect(v).toBeLessThan(7.5);
    }
  });
});
