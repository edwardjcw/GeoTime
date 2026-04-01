namespace GeoTime.Core.Proc;

/// <summary>
/// 3D Simplex Noise seeded from a Xoshiro256** PRNG with FBM support.
/// </summary>
public sealed class SimplexNoise
{
    private static readonly int[][] Grad3 =
    [
        [1,1,0],[-1,1,0],[1,-1,0],[-1,-1,0],
        [1,0,1],[-1,0,1],[1,0,-1],[-1,0,-1],
        [0,1,1],[0,-1,1],[0,1,-1],[0,-1,-1],
    ];

    private const double F3 = 1.0 / 3.0;
    private const double G3 = 1.0 / 6.0;

    private readonly byte[] _perm = new byte[512];
    private readonly byte[] _permMod12 = new byte[512];

    public SimplexNoise(Xoshiro256ss rng)
    {
        var p = new byte[256];
        for (var i = 0; i < 256; i++) p[i] = (byte)i;
        for (var i = 255; i > 0; i--)
        {
            var j = rng.NextInt(0, i);
            (p[i], p[j]) = (p[j], p[i]);
        }
        for (var i = 0; i < 512; i++)
        {
            _perm[i] = p[i & 255];
            _permMod12[i] = (byte)(_perm[i] % 12);
        }
    }

    /// <summary>Return simplex noise in [-1, 1] for a 3D coordinate.</summary>
    public double Noise3D(double x, double y, double z)
    {
        var s = (x + y + z) * F3;
        var i = (int)Math.Floor(x + s);
        var j = (int)Math.Floor(y + s);
        var k = (int)Math.Floor(z + s);

        var t = (i + j + k) * G3;
        var x0 = x - (i - t);
        var y0 = y - (j - t);
        var z0 = z - (k - t);

        int i1, j1, k1, i2, j2, k2;
        if (x0 >= y0)
        {
            if (y0 >= z0)      { i1=1;j1=0;k1=0;i2=1;j2=1;k2=0; }
            else if (x0 >= z0) { i1=1;j1=0;k1=0;i2=1;j2=0;k2=1; }
            else               { i1=0;j1=0;k1=1;i2=1;j2=0;k2=1; }
        }
        else
        {
            if (y0 < z0)       { i1=0;j1=0;k1=1;i2=0;j2=1;k2=1; }
            else if (x0 < z0)  { i1=0;j1=1;k1=0;i2=0;j2=1;k2=1; }
            else               { i1=0;j1=1;k1=0;i2=1;j2=1;k2=0; }
        }

        double x1 = x0 - i1 + G3, y1 = y0 - j1 + G3, z1 = z0 - k1 + G3;
        double x2 = x0 - i2 + 2*G3, y2 = y0 - j2 + 2*G3, z2 = z0 - k2 + 2*G3;
        double x3 = x0 - 1 + 3*G3, y3 = y0 - 1 + 3*G3, z3 = z0 - 1 + 3*G3;

        int ii = i & 255, jj = j & 255, kk = k & 255;
        int gi0 = _permMod12[ii      + _perm[jj      + _perm[kk]]];
        int gi1 = _permMod12[ii + i1 + _perm[jj + j1 + _perm[kk + k1]]];
        int gi2 = _permMod12[ii + i2 + _perm[jj + j2 + _perm[kk + k2]]];
        int gi3 = _permMod12[ii + 1  + _perm[jj + 1  + _perm[kk + 1]]];

        var n0 = Contrib(gi0, x0, y0, z0);
        var n1 = Contrib(gi1, x1, y1, z1);
        var n2 = Contrib(gi2, x2, y2, z2);
        var n3 = Contrib(gi3, x3, y3, z3);

        return 32.0 * (n0 + n1 + n2 + n3);
    }

    private static double Contrib(int gi, double x, double y, double z)
    {
        var t = 0.6 - x*x - y*y - z*z;
        if (t < 0) return 0;
        t *= t;
        var g = Grad3[gi];
        return t * t * (g[0]*x + g[1]*y + g[2]*z);
    }

    /// <summary>Fractional Brownian motion — layered simplex noise octaves.</summary>
    public double Fbm(double x, double y, double z, int octaves,
                      double lacunarity = 2, double persistence = 0.5)
    {
        double value = 0, amplitude = 1, frequency = 1, maxAmplitude = 0;
        for (var i = 0; i < octaves; i++)
        {
            value += amplitude * Noise3D(x*frequency, y*frequency, z*frequency);
            maxAmplitude += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }
        return value / maxAmplitude;
    }
}
