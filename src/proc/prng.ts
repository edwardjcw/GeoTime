// ─── Seeded PRNG — xoshiro256** ─────────────────────────────────────────────
// Deterministic pseudo-random number generator suitable for procedural
// generation.  Uses SplitMix64 to expand a single uint32 seed into the
// four uint64 state words required by xoshiro256**.
//
// All 64-bit arithmetic is performed with pairs of uint32 values (hi, lo)
// so the implementation stays within JavaScript's safe-integer range.

// ─── 32-bit helpers ─────────────────────────────────────────────────────────

/** Keep a value in uint32 range. */
const u32 = (n: number): number => n >>> 0;

/** Pair representing a uint64 as (hi, lo) where value = hi*2^32 + lo. */
type U64 = [number, number];

const ZERO: U64 = [0, 0];

function u64(hi: number, lo: number): U64 {
  return [u32(hi), u32(lo)];
}

function u64Add(a: U64, b: U64): U64 {
  const lo = (a[1] + b[1]) >>> 0;
  const carry = (lo < a[1] ? 1 : 0);
  const hi = (a[0] + b[0] + carry) >>> 0;
  return [hi, lo];
}

function u64Xor(a: U64, b: U64): U64 {
  return [u32(a[0] ^ b[0]), u32(a[1] ^ b[1])];
}

function u64ShiftLeft(v: U64, n: number): U64 {
  if (n === 0) return v;
  if (n >= 64) return ZERO;
  if (n >= 32) return [u32(v[1] << (n - 32)), 0];
  return [u32((v[0] << n) | (v[1] >>> (32 - n))), u32(v[1] << n)];
}

function u64ShiftRight(v: U64, n: number): U64 {
  if (n === 0) return v;
  if (n >= 64) return ZERO;
  if (n >= 32) return [0, u32(v[0] >>> (n - 32))];
  return [u32(v[0] >>> n), u32((v[1] >>> n) | (v[0] << (32 - n)))];
}

function u64Rotl(v: U64, n: number): U64 {
  if (n === 0) return v;
  return u64Xor(u64ShiftLeft(v, n), u64ShiftRight(v, 64 - n));
}

/** Multiply two uint64 values using four 32×32→64 partial products. */
function u64Mul(a: U64, b: U64): U64 {
  const aHi = a[0] >>> 0;
  const aLo = a[1] >>> 0;
  const bHi = b[0] >>> 0;
  const bLo = b[1] >>> 0;

  // Split each 32-bit half into 16-bit pieces to avoid precision loss.
  const aLoH = (aLo >>> 16) & 0xffff;
  const aLoL = aLo & 0xffff;
  const bLoH = (bLo >>> 16) & 0xffff;
  const bLoL = bLo & 0xffff;

  // Low × Low (full 64-bit result needed)
  let t = aLoL * bLoL;
  const lo0 = t & 0xffff;
  t = (t >>> 16) + aLoH * bLoL;
  let lo1 = t & 0xffff;
  let carry = t >>> 16;
  t = lo1 + aLoL * bLoH;
  lo1 = t & 0xffff;
  carry += t >>> 16;

  // High 32 bits — only lower 32 bits of the full product matter.
  let hi = (carry + aLoH * bLoH) >>> 0;
  hi = (hi + Math.imul(aHi, bLo)) >>> 0;
  hi = (hi + Math.imul(aLo, bHi)) >>> 0;

  const lo = ((lo1 << 16) | lo0) >>> 0;
  return [hi, lo];
}

// ─── SplitMix64 (seeding helper) ───────────────────────────────────────────

function splitMix64(state: U64): { value: U64; next: U64 } {
  const GOLDEN: U64 = u64(0x9e3779b9, 0x7f4a7c15);
  const next = u64Add(state, GOLDEN);
  let z = next;
  z = u64Xor(z, u64ShiftRight(z, 30));
  z = u64Mul(z, u64(0xbf58476d, 0x1ce4e5b9));
  z = u64Xor(z, u64ShiftRight(z, 27));
  z = u64Mul(z, u64(0x94d049bb, 0x133111eb));
  z = u64Xor(z, u64ShiftRight(z, 31));
  return { value: z, next };
}

// ─── Xoshiro256** ───────────────────────────────────────────────────────────

export class Xoshiro256ss {
  private s0: U64;
  private s1: U64;
  private s2: U64;
  private s3: U64;

  constructor(seed: number) {
    // Expand the uint32 seed into the initial SplitMix64 state.
    let sm: U64 = u64(0, seed >>> 0);
    let r: { value: U64; next: U64 };
    r = splitMix64(sm); sm = r.next; this.s0 = r.value;
    r = splitMix64(sm); sm = r.next; this.s1 = r.value;
    r = splitMix64(sm); sm = r.next; this.s2 = r.value;
    r = splitMix64(sm); this.s3 = r.value;
  }

  /** Advance the generator and return the raw uint64 result. */
  private nextU64(): U64 {
    const result = u64Rotl(u64Mul(this.s1, u64(0, 5)), 7);
    const resultFinal = u64Mul(result, u64(0, 9));

    const t = u64ShiftLeft(this.s1, 17);

    this.s2 = u64Xor(this.s2, this.s0);
    this.s3 = u64Xor(this.s3, this.s1);
    this.s1 = u64Xor(this.s1, this.s2);
    this.s0 = u64Xor(this.s0, this.s3);

    this.s2 = u64Xor(this.s2, t);
    this.s3 = u64Rotl(this.s3, 45);

    return resultFinal;
  }

  /** Return a float64 in [0, 1). */
  next(): number {
    const v = this.nextU64();
    // Use the upper 53 bits for full double-precision mantissa coverage.
    const hi53 = v[0] >>> 0;
    const lo53 = (v[1] >>> 11) >>> 0; // keep 21 bits from lo
    return (hi53 * 2097152 + lo53) / 9007199254740992; // 2^53
  }

  /** Return an integer in [min, max] (inclusive). */
  nextInt(min: number, max: number): number {
    return min + Math.floor(this.next() * (max - min + 1));
  }

  /** Return a float in [min, max). */
  nextFloat(min: number, max: number): number {
    return min + this.next() * (max - min);
  }
}
