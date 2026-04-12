// ─── Icosphere Mesh Generator with LOD Support ─────────────────────────────

export interface IcosphereGeometry {
  positions: Float32Array;
  indices: Uint32Array;
  uvs: Float32Array;
}

// Golden ratio
const PHI = (1 + Math.sqrt(5)) / 2;

// 12 vertices of a regular icosahedron (normalised to unit sphere)
const ICO_VERTICES: [number, number, number][] = [
  [-1, PHI, 0], [1, PHI, 0], [-1, -PHI, 0], [1, -PHI, 0],
  [0, -1, PHI], [0, 1, PHI], [0, -1, -PHI], [0, 1, -PHI],
  [PHI, 0, -1], [PHI, 0, 1], [-PHI, 0, -1], [-PHI, 0, 1],
];

// 20 triangular faces (indices into ICO_VERTICES)
const ICO_FACES: [number, number, number][] = [
  [0, 11, 5], [0, 5, 1], [0, 1, 7], [0, 7, 10], [0, 10, 11],
  [1, 5, 9], [5, 11, 4], [11, 10, 2], [10, 7, 6], [7, 1, 8],
  [3, 9, 4], [3, 4, 2], [3, 2, 6], [3, 6, 8], [3, 8, 9],
  [4, 9, 5], [2, 4, 11], [6, 2, 10], [8, 6, 7], [9, 8, 1],
];

/**
 * Create an icosphere by subdividing a regular icosahedron and projecting
 * all vertices onto the unit sphere.
 *
 * @param subdivisions - Number of recursive subdivision passes (0 = raw icosahedron).
 *   Each pass quadruples the face count: faces = 20 * 4^subdivisions.
 */
export function createIcosphere(subdivisions: number): IcosphereGeometry {
  // Working arrays — start from the icosahedron
  const vertices: number[] = [];
  let faces: [number, number, number][] = [];

  // Normalise and push icosahedron vertices
  for (const [x, y, z] of ICO_VERTICES) {
    const len = Math.sqrt(x * x + y * y + z * z);
    vertices.push(x / len, y / len, z / len);
  }

  // Copy initial faces
  for (const f of ICO_FACES) {
    faces.push([f[0], f[1], f[2]]);
  }

  // Edge midpoint cache keyed by "minIdx:maxIdx"
  const getMidpoint = (
    cache: Map<string, number>,
    a: number,
    b: number,
  ): number => {
    const key = a < b ? `${a}:${b}` : `${b}:${a}`;
    const cached = cache.get(key);
    if (cached !== undefined) return cached;

    const ax = vertices[a * 3], ay = vertices[a * 3 + 1], az = vertices[a * 3 + 2];
    const bx = vertices[b * 3], by = vertices[b * 3 + 1], bz = vertices[b * 3 + 2];

    // Midpoint projected onto unit sphere
    let mx = (ax + bx) * 0.5;
    let my = (ay + by) * 0.5;
    let mz = (az + bz) * 0.5;
    const len = Math.sqrt(mx * mx + my * my + mz * mz);
    mx /= len;
    my /= len;
    mz /= len;

    const idx = vertices.length / 3;
    vertices.push(mx, my, mz);
    cache.set(key, idx);
    return idx;
  };

  // Subdivide
  for (let s = 0; s < subdivisions; s++) {
    const cache = new Map<string, number>();
    const newFaces: [number, number, number][] = [];

    for (const [v0, v1, v2] of faces) {
      const a = getMidpoint(cache, v0, v1);
      const b = getMidpoint(cache, v1, v2);
      const c = getMidpoint(cache, v2, v0);

      newFaces.push([v0, a, c], [v1, b, a], [v2, c, b], [a, b, c]);
    }
    faces = newFaces;
  }

  // Build typed arrays
  const vertexCount = vertices.length / 3;
  const positions = new Float32Array(vertices);
  const indices = new Uint32Array(faces.length * 3);
  for (let i = 0; i < faces.length; i++) {
    indices[i * 3] = faces[i][0];
    indices[i * 3 + 1] = faces[i][1];
    indices[i * 3 + 2] = faces[i][2];
  }

  // Equirectangular UV mapping: u = atan2(z, x) / (2π) + 0.5, v = asin(y) / π + 0.5
  const rawUvs = new Float32Array(vertexCount * 2);
  const TWO_PI = Math.PI * 2;
  for (let i = 0; i < vertexCount; i++) {
    const x = positions[i * 3];
    const y = positions[i * 3 + 1];
    const z = positions[i * 3 + 2];
    rawUvs[i * 2] = Math.atan2(z, x) / TWO_PI + 0.5;
    rawUvs[i * 2 + 1] = Math.asin(Math.max(-1, Math.min(1, y))) / Math.PI + 0.5;
  }

  // ── Seam fix: duplicate vertices at the antimeridian ─────────────────────
  // Triangles that span the u=0/u=1 boundary produce a "zipper" artifact
  // because the GPU interpolates u from ~1 back to ~0 across the triangle.
  // We detect such triangles and duplicate the offending vertex(es) with
  // u += 1.0 so interpolation proceeds smoothly.  The texture must use
  // RepeatWrapping on the S axis for this to render correctly.

  const outPositions: number[] = Array.from(positions);
  const outUvs: number[] = Array.from(rawUvs);
  const outIndices: number[] = [];

  // Cache: original vertex index → duplicated vertex index with u+1
  const dupCache = new Map<number, number>();

  const faceCount = faces.length;
  for (let f = 0; f < faceCount; f++) {
    let i0 = faces[f][0];
    let i1 = faces[f][1];
    let i2 = faces[f][2];

    const u0 = outUvs[i0 * 2], u1 = outUvs[i1 * 2], u2 = outUvs[i2 * 2];

    // Check if the triangle crosses the antimeridian seam
    const du01 = Math.abs(u0 - u1);
    const du12 = Math.abs(u1 - u2);
    const du20 = Math.abs(u2 - u0);

    if (du01 > 0.5 || du12 > 0.5 || du20 > 0.5) {
      // Triangle spans the seam — shift all low-u (< 0.5) vertices up by +1
      // so the GPU interpolates smoothly across the boundary.
      const lowCount = (u0 < 0.5 ? 1 : 0) + (u1 < 0.5 ? 1 : 0) + (u2 < 0.5 ? 1 : 0);

      if (lowCount === 1 || lowCount === 2) {
        const fixVertex = (idx: number, u: number): number => {
          if (u < 0.5) {
            let dup = dupCache.get(idx);
            if (dup === undefined) {
              dup = outPositions.length / 3;
              outPositions.push(
                positions[idx * 3],
                positions[idx * 3 + 1],
                positions[idx * 3 + 2],
              );
              outUvs.push(u + 1.0, outUvs[idx * 2 + 1]);
              dupCache.set(idx, dup);
            }
            return dup;
          }
          return idx;
        };

        if (u0 < 0.5) i0 = fixVertex(i0, u0);
        if (u1 < 0.5) i1 = fixVertex(i1, u1);
        if (u2 < 0.5) i2 = fixVertex(i2, u2);
      }
    }

    // Fix pole vertices: vertices near the poles have indeterminate u.
    // Set their u to the average of the other two vertices in the triangle.
    const y0 = outPositions[i0 * 3 + 1];
    const y1 = outPositions[i1 * 3 + 1];
    const y2 = outPositions[i2 * 3 + 1];
    const POLE_THRESHOLD = 0.999;

    if (Math.abs(y0) > POLE_THRESHOLD) {
      const avgU = (outUvs[i1 * 2] + outUvs[i2 * 2]) / 2;
      // Duplicate pole vertex with corrected u
      const dup = outPositions.length / 3;
      outPositions.push(outPositions[i0 * 3], outPositions[i0 * 3 + 1], outPositions[i0 * 3 + 2]);
      outUvs.push(avgU, outUvs[i0 * 2 + 1]);
      i0 = dup;
    }
    if (Math.abs(y1) > POLE_THRESHOLD) {
      const avgU = (outUvs[i0 * 2] + outUvs[i2 * 2]) / 2;
      const dup = outPositions.length / 3;
      outPositions.push(outPositions[i1 * 3], outPositions[i1 * 3 + 1], outPositions[i1 * 3 + 2]);
      outUvs.push(avgU, outUvs[i1 * 2 + 1]);
      i1 = dup;
    }
    if (Math.abs(y2) > POLE_THRESHOLD) {
      const avgU = (outUvs[i0 * 2] + outUvs[i2 * 2]) / 2;
      const dup = outPositions.length / 3;
      outPositions.push(outPositions[i2 * 3], outPositions[i2 * 3 + 1], outPositions[i2 * 3 + 2]);
      outUvs.push(avgU, outUvs[i2 * 2 + 1]);
      i2 = dup;
    }

    outIndices.push(i0, i1, i2);
  }

  return {
    positions: new Float32Array(outPositions),
    indices: new Uint32Array(outIndices),
    uvs: new Float32Array(outUvs),
  };
}
