import { createIcosphere } from '../src/render/icosphere';

describe('createIcosphere', () => {
  it('should generate at least the base vertex count for subdivision 0', () => {
    const geo = createIcosphere(0);
    // Base icosahedron has 12 vertices; seam-fix duplicates may add a few more
    expect(geo.positions.length / 3).toBeGreaterThanOrEqual(12);
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

  it('should generate valid UV coordinates (u in [0, 2], v in [0, 1])', () => {
    // After seam-fix, u-coordinates of duplicated vertices may exceed 1.0
    // (up to ~2.0) because they are shifted by +1 to avoid the wrapping seam.
    // The texture must use RepeatWrapping for this to render correctly.
    const geo = createIcosphere(1);
    const count = geo.uvs.length / 2;
    for (let i = 0; i < count; i++) {
      const u = geo.uvs[i * 2];
      const v = geo.uvs[i * 2 + 1];
      expect(u).toBeGreaterThanOrEqual(0);
      expect(u).toBeLessThanOrEqual(2);
      expect(v).toBeGreaterThanOrEqual(0);
      expect(v).toBeLessThanOrEqual(1);
    }
  });

  it('should not have seam triangles with u-range > 0.5 after fix', () => {
    const geo = createIcosphere(3);
    const faceCount = geo.indices.length / 3;
    let seamCount = 0;
    for (let f = 0; f < faceCount; f++) {
      const i0 = geo.indices[f * 3];
      const i1 = geo.indices[f * 3 + 1];
      const i2 = geo.indices[f * 3 + 2];
      const u0 = geo.uvs[i0 * 2];
      const u1 = geo.uvs[i1 * 2];
      const u2 = geo.uvs[i2 * 2];
      const maxU = Math.max(u0, u1, u2);
      const minU = Math.min(u0, u1, u2);
      if (maxU - minU > 0.5) seamCount++;
    }
    expect(seamCount).toBe(0);
  });
});
