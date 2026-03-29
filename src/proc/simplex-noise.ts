// ─── 3-D Simplex Noise ──────────────────────────────────────────────────────
// Classic simplex noise in three dimensions, seeded from a Xoshiro256** PRNG
// so that each planet gets a unique but reproducible noise field.

import { Xoshiro256ss } from './prng';

// ─── Constants ──────────────────────────────────────────────────────────────

// Gradients for 3D simplex noise — 12 edges of a cube.
const GRAD3: ReadonlyArray<readonly [number, number, number]> = [
  [1, 1, 0], [-1, 1, 0], [1, -1, 0], [-1, -1, 0],
  [1, 0, 1], [-1, 0, 1], [1, 0, -1], [-1, 0, -1],
  [0, 1, 1], [0, -1, 1], [0, 1, -1], [0, -1, -1],
];

const F3 = 1 / 3;
const G3 = 1 / 6;

// ─── SimplexNoise class ─────────────────────────────────────────────────────

export class SimplexNoise {
  private readonly perm: Uint8Array;
  private readonly permMod12: Uint8Array;

  constructor(rng: Xoshiro256ss) {
    // Build a Fisher–Yates shuffled permutation table.
    const p = new Uint8Array(256);
    for (let i = 0; i < 256; i++) p[i] = i;
    for (let i = 255; i > 0; i--) {
      const j = rng.nextInt(0, i);
      const tmp = p[i];
      p[i] = p[j];
      p[j] = tmp;
    }

    // Double the table so we can avoid index wrapping.
    this.perm = new Uint8Array(512);
    this.permMod12 = new Uint8Array(512);
    for (let i = 0; i < 512; i++) {
      this.perm[i] = p[i & 255];
      this.permMod12[i] = this.perm[i] % 12;
    }
  }

  /** Return simplex noise in [-1, 1] for a 3-D coordinate. */
  noise3D(x: number, y: number, z: number): number {
    const { perm, permMod12 } = this;

    // Skew the input space to determine which simplex cell we are in.
    const s = (x + y + z) * F3;
    const i = Math.floor(x + s);
    const j = Math.floor(y + s);
    const k = Math.floor(z + s);

    const t = (i + j + k) * G3;
    const X0 = i - t;
    const Y0 = j - t;
    const Z0 = k - t;
    const x0 = x - X0;
    const y0 = y - Y0;
    const z0 = z - Z0;

    // Determine which simplex we are in.
    let i1: number, j1: number, k1: number;
    let i2: number, j2: number, k2: number;
    if (x0 >= y0) {
      if (y0 >= z0)      { i1=1; j1=0; k1=0; i2=1; j2=1; k2=0; }
      else if (x0 >= z0) { i1=1; j1=0; k1=0; i2=1; j2=0; k2=1; }
      else               { i1=0; j1=0; k1=1; i2=1; j2=0; k2=1; }
    } else {
      if (y0 < z0)       { i1=0; j1=0; k1=1; i2=0; j2=1; k2=1; }
      else if (x0 < z0)  { i1=0; j1=1; k1=0; i2=0; j2=1; k2=1; }
      else               { i1=0; j1=1; k1=0; i2=1; j2=1; k2=0; }
    }

    const x1 = x0 - i1 + G3;
    const y1 = y0 - j1 + G3;
    const z1 = z0 - k1 + G3;
    const x2 = x0 - i2 + 2 * G3;
    const y2 = y0 - j2 + 2 * G3;
    const z2 = z0 - k2 + 2 * G3;
    const x3 = x0 - 1 + 3 * G3;
    const y3 = y0 - 1 + 3 * G3;
    const z3 = z0 - 1 + 3 * G3;

    const ii = i & 255;
    const jj = j & 255;
    const kk = k & 255;

    const gi0 = permMod12[ii      + perm[jj      + perm[kk]]];
    const gi1 = permMod12[ii + i1 + perm[jj + j1 + perm[kk + k1]]];
    const gi2 = permMod12[ii + i2 + perm[jj + j2 + perm[kk + k2]]];
    const gi3 = permMod12[ii + 1  + perm[jj + 1  + perm[kk + 1]]];

    let n0: number, n1: number, n2: number, n3: number;

    let t0 = 0.6 - x0 * x0 - y0 * y0 - z0 * z0;
    if (t0 < 0) { n0 = 0; } else {
      t0 *= t0;
      const g = GRAD3[gi0];
      n0 = t0 * t0 * (g[0] * x0 + g[1] * y0 + g[2] * z0);
    }

    let t1 = 0.6 - x1 * x1 - y1 * y1 - z1 * z1;
    if (t1 < 0) { n1 = 0; } else {
      t1 *= t1;
      const g = GRAD3[gi1];
      n1 = t1 * t1 * (g[0] * x1 + g[1] * y1 + g[2] * z1);
    }

    let t2 = 0.6 - x2 * x2 - y2 * y2 - z2 * z2;
    if (t2 < 0) { n2 = 0; } else {
      t2 *= t2;
      const g = GRAD3[gi2];
      n2 = t2 * t2 * (g[0] * x2 + g[1] * y2 + g[2] * z2);
    }

    let t3 = 0.6 - x3 * x3 - y3 * y3 - z3 * z3;
    if (t3 < 0) { n3 = 0; } else {
      t3 *= t3;
      const g = GRAD3[gi3];
      n3 = t3 * t3 * (g[0] * x3 + g[1] * y3 + g[2] * z3);
    }

    // Scale to [-1, 1].
    return 32 * (n0 + n1 + n2 + n3);
  }

  /**
   * Fractional Brownian motion — layer multiple octaves of simplex noise.
   * @param octaves      Number of noise layers to sum.
   * @param lacunarity   Frequency multiplier per octave (default 2).
   * @param persistence  Amplitude multiplier per octave (default 0.5).
   */
  fbm(
    x: number,
    y: number,
    z: number,
    octaves: number,
    lacunarity = 2,
    persistence = 0.5,
  ): number {
    let value = 0;
    let amplitude = 1;
    let frequency = 1;
    let maxAmplitude = 0;

    for (let i = 0; i < octaves; i++) {
      value += amplitude * this.noise3D(
        x * frequency,
        y * frequency,
        z * frequency,
      );
      maxAmplitude += amplitude;
      amplitude *= persistence;
      frequency *= lacunarity;
    }

    return value / maxAmplitude; // normalise to [-1, 1]
  }
}
