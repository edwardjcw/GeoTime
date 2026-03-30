using GeoTime.Core.Models;

namespace GeoTime.Core.Engines;

/// <summary>Cross-section sampling engine with great-circle interpolation.</summary>
public sealed class CrossSectionEngine
{
    private const double DEG2RAD = Math.PI / 180;
    private const double RAD2DEG = 180 / Math.PI;
    public const double EARTH_RADIUS_KM = 6371;
    public const int DEFAULT_SAMPLE_COUNT = 512;

    private readonly int _sampleCount;
    private SimulationState? _state;
    private StratigraphyStack? _strat;

    public CrossSectionEngine(int sampleCount = DEFAULT_SAMPLE_COUNT)
        => _sampleCount = sampleCount;

    public void Initialize(SimulationState state, StratigraphyStack strat)
    {
        _state = state; _strat = strat;
    }

    public static List<DeepEarthZone> GetDeepEarthZones() => new()
    {
        new() { Name = "Lithospheric Mantle", TopKm = 30, BottomKm = 100, RockType = RockType.DEEP_LITHMAN },
        new() { Name = "Asthenosphere", TopKm = 100, BottomKm = 410, RockType = RockType.DEEP_ASTHEN },
        new() { Name = "Transition Zone", TopKm = 410, BottomKm = 660, RockType = RockType.DEEP_TRANS },
        new() { Name = "Lower Mantle", TopKm = 660, BottomKm = 2891, RockType = RockType.DEEP_LOWMAN },
        new() { Name = "Core-Mantle Boundary", TopKm = 2891, BottomKm = 2921, RockType = RockType.DEEP_CMB },
        new() { Name = "Outer Core", TopKm = 2921, BottomKm = 5150, RockType = RockType.DEEP_OUTCORE },
        new() { Name = "Inner Core", TopKm = 5150, BottomKm = 6371, RockType = RockType.DEEP_INCORE },
    };

    public static double CentralAngle(double lat1, double lon1, double lat2, double lon2)
    {
        double p1 = lat1 * DEG2RAD, p2 = lat2 * DEG2RAD;
        double dp = (lat2 - lat1) * DEG2RAD, dl = (lon2 - lon1) * DEG2RAD;
        double a = Math.Pow(Math.Sin(dp / 2), 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Pow(Math.Sin(dl / 2), 2);
        return 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    public static LatLon GreatCircleInterpolate(double lat1, double lon1, double lat2, double lon2, double t)
    {
        double d = CentralAngle(lat1, lon1, lat2, lon2);
        if (d < 1e-12) return new(lat1, lon1);
        double sinD = Math.Sin(d);
        double a = Math.Sin((1 - t) * d) / sinD, b = Math.Sin(t * d) / sinD;
        double p1 = lat1 * DEG2RAD, l1 = lon1 * DEG2RAD, p2 = lat2 * DEG2RAD, l2 = lon2 * DEG2RAD;
        double x = a * Math.Cos(p1) * Math.Cos(l1) + b * Math.Cos(p2) * Math.Cos(l2);
        double y = a * Math.Cos(p1) * Math.Sin(l1) + b * Math.Cos(p2) * Math.Sin(l2);
        double z = a * Math.Sin(p1) + b * Math.Sin(p2);
        return new(Math.Atan2(z, Math.Sqrt(x * x + y * y)) * RAD2DEG, Math.Atan2(y, x) * RAD2DEG);
    }

    public static int LatToRow(double latDeg, int gs)
        => Math.Clamp((int)Math.Round((90 - Math.Clamp(latDeg, -90, 90)) / 180.0 * (gs - 1)), 0, gs - 1);

    public static int LonToCol(double lonDeg, int gs)
    {
        double lon = lonDeg % 360;
        if (lon > 180) lon -= 360;
        if (lon < -180) lon += 360;
        return Math.Clamp((int)Math.Round((lon + 180) / 360.0 * (gs - 1)), 0, gs - 1);
    }

    public static int LatLonToIndex(double lat, double lon, int gs)
        => LatToRow(lat, gs) * gs + LonToCol(lon, gs);

    public CrossSectionProfile? BuildProfile(List<LatLon> pathPoints)
    {
        if (_state == null || _strat == null || pathPoints.Count < 2) return null;
        int gs = _state.GridSize;
        var samples = SamplePathPoints(pathPoints, _sampleCount);
        double totalDist = ComputePathDistanceKm(pathPoints);

        var result = new List<CrossSectionSample>();
        for (int s = 0; s < samples.Count; s++)
        {
            double distKm = _sampleCount > 1 ? (double)s / (_sampleCount - 1) * totalDist : 0;
            int ci = LatLonToIndex(samples[s].Lat, samples[s].Lon, gs);
            result.Add(new CrossSectionSample
            {
                DistanceKm = distKm,
                SurfaceElevation = _state.HeightMap[ci],
                CrustThicknessKm = _state.CrustThicknessMap[ci] / 1000.0,
                SoilType = (SoilOrder)_state.SoilTypeMap[ci],
                SoilDepthM = _state.SoilDepthMap[ci],
                Layers = _strat.GetLayers(ci).Select(l => l.Clone()).ToList(),
            });
        }

        return new CrossSectionProfile
        {
            Samples = result, TotalDistanceKm = totalDist,
            PathPoints = new List<LatLon>(pathPoints),
            DeepEarthZones = GetDeepEarthZones(),
        };
    }

    public static double ComputePathDistanceKm(List<LatLon> pts)
    {
        double total = 0;
        for (int i = 0; i < pts.Count - 1; i++)
            total += CentralAngle(pts[i].Lat, pts[i].Lon, pts[i + 1].Lat, pts[i + 1].Lon) * EARTH_RADIUS_KM;
        return total;
    }

    public static List<LatLon> SamplePathPoints(List<LatLon> pts, int numSamples)
    {
        if (pts.Count == 0) return new();
        if (pts.Count == 1) return Enumerable.Repeat(pts[0], numSamples).ToList();

        var segs = new double[pts.Count - 1];
        double totalAngle = 0;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            segs[i] = CentralAngle(pts[i].Lat, pts[i].Lon, pts[i + 1].Lat, pts[i + 1].Lon);
            totalAngle += segs[i];
        }
        if (totalAngle < 1e-12)
            return Enumerable.Repeat(pts[0], numSamples).ToList();

        var cum = new double[pts.Count];
        for (int i = 0; i < segs.Length; i++) cum[i + 1] = cum[i] + segs[i];

        var result = new List<LatLon>();
        for (int s = 0; s < numSamples; s++)
        {
            double frac = numSamples > 1 ? (double)s / (numSamples - 1) : 0;
            double target = frac * totalAngle;
            int seg = 0;
            while (seg < segs.Length - 1 && cum[seg + 1] < target) seg++;
            double t = segs[seg] > 1e-12 ? (target - cum[seg]) / segs[seg] : 0;
            result.Add(GreatCircleInterpolate(pts[seg].Lat, pts[seg].Lon, pts[seg + 1].Lat, pts[seg + 1].Lon, Math.Clamp(t, 0, 1)));
        }
        return result;
    }
}
