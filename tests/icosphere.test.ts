import { createIcosphere } from '../src/render/icosphere';

describe('createIcosphere', () => {
  it('should generate correct vertex count for subdivision 0 (12 vertices)', () => {
    const geo = createIcosphere(0);
    expect(geo.positions.length / 3).toBe(12);
  });

  it('should generate correct face count for subdivision 0 (20 faces)', () => {
    const geo = createIcosphere(0);
    expect(geo.indices.length / 3).toBe(20);
  });

  it('should increase vertex/face count with higher subdivision', () => {
    const geo0 = createIcosphere(0);
    const geo1 = createIcosphere(1);
    expect(geo1.positions.length).toBeGreaterThan(geo0.positions.length);
    expect(geo1.indices.length).toBeGreaterThan(geo0.indices.length);
  });

  it('all vertices should be on the unit sphere (length ≈ 1.0)', () => {
    const geo = createIcosphere(2);
    const count = geo.positions.length / 3;
    for (let i = 0; i < count; i++) {
      const x = geo.positions[i * 3];
      const y = geo.positions[i * 3 + 1];
      const z = geo.positions[i * 3 + 2];
      const len = Math.sqrt(x * x + y * y + z * z);
      expect(len).toBeCloseTo(1.0, 4);
    }
  });

  it('should generate valid UV coordinates (all in [0, 1])', () => {
    const geo = createIcosphere(1);
    for (let i = 0; i < geo.uvs.length; i++) {
      expect(geo.uvs[i]).toBeGreaterThanOrEqual(0);
      expect(geo.uvs[i]).toBeLessThanOrEqual(1);
    }
  });
});
