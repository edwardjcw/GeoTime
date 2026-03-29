import { SimplexNoise } from '../src/proc/simplex-noise';
import { Xoshiro256ss } from '../src/proc/prng';

describe('SimplexNoise', () => {
  it('should produce values in [-1, 1] for noise3D', () => {
    const noise = new SimplexNoise(new Xoshiro256ss(42));
    for (let i = 0; i < 500; i++) {
      const x = Math.random() * 100 - 50;
      const y = Math.random() * 100 - 50;
      const z = Math.random() * 100 - 50;
      const v = noise.noise3D(x, y, z);
      expect(v).toBeGreaterThanOrEqual(-1);
      expect(v).toBeLessThanOrEqual(1);
    }
  });

  it('should be deterministic with the same PRNG seed', () => {
    const noise1 = new SimplexNoise(new Xoshiro256ss(42));
    const noise2 = new SimplexNoise(new Xoshiro256ss(42));
    for (let i = 0; i < 50; i++) {
      const x = i * 0.1;
      const y = i * 0.2;
      const z = i * 0.3;
      expect(noise1.noise3D(x, y, z)).toBe(noise2.noise3D(x, y, z));
    }
  });

  it('fbm should produce different results with different octave counts', () => {
    const noise = new SimplexNoise(new Xoshiro256ss(42));
    const v1 = noise.fbm(0.5, 0.7, 0.3, 1);
    const v2 = noise.fbm(0.5, 0.7, 0.3, 4);
    expect(v1).not.toBe(v2);
  });

  it('should produce continuous values (nearby inputs give similar outputs)', () => {
    const noise = new SimplexNoise(new Xoshiro256ss(42));
    const base = noise.noise3D(1.0, 1.0, 1.0);
    const nearby = noise.noise3D(1.001, 1.001, 1.001);
    expect(Math.abs(base - nearby)).toBeLessThan(0.1);
  });
});
