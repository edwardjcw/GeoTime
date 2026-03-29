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
  const uvs = new Float32Array(vertexCount * 2);
  const TWO_PI = Math.PI * 2;
  for (let i = 0; i < vertexCount; i++) {
    const x = positions[i * 3];
    const y = positions[i * 3 + 1];
    const z = positions[i * 3 + 2];
    uvs[i * 2] = Math.atan2(z, x) / TWO_PI + 0.5;
    uvs[i * 2 + 1] = Math.asin(Math.max(-1, Math.min(1, y))) / Math.PI + 0.5;
  }

  return { positions, indices, uvs };
}
