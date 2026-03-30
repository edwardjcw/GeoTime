namespace GeoTime.Core.Proc;

/// <summary>
/// Seeded PRNG — xoshiro256** implemented with native 64-bit unsigned arithmetic.
/// Uses SplitMix64 to expand a single uint seed into four 64-bit state words.
/// </summary>
public sealed class Xoshiro256ss
{
    private ulong _s0, _s1, _s2, _s3;

    public Xoshiro256ss(uint seed)
    {
        ulong sm = seed;
        (_s0, sm) = SplitMix64(sm);
        (_s1, sm) = SplitMix64(sm);
        (_s2, sm) = SplitMix64(sm);
        (_s3, _)  = SplitMix64(sm);
    }

    private static (ulong value, ulong next) SplitMix64(ulong state)
    {
        ulong next = state + 0x9E3779B97F4A7C15UL;
        ulong z = next;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (z, next);
    }

    private ulong NextU64()
    {
        ulong result = RotateLeft(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;

        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = RotateLeft(_s3, 45);

        return result;
    }

    private static ulong RotateLeft(ulong value, int bits)
        => (value << bits) | (value >> (64 - bits));

    /// <summary>Return a double in [0, 1).</summary>
    public double Next()
    {
        ulong v = NextU64();
        return (v >> 11) * (1.0 / (1UL << 53));
    }

    /// <summary>Return an integer in [min, max] (inclusive).</summary>
    public int NextInt(int min, int max)
        => min + (int)(Next() * (max - min + 1));

    /// <summary>Return a double in [min, max).</summary>
    public double NextFloat(double min, double max)
        => min + Next() * (max - min);
}
