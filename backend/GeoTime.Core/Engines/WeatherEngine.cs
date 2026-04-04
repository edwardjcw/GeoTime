using GeoTime.Core.Models;
using GeoTime.Core.Proc;

namespace GeoTime.Core.Engines;

/// <summary>Frontal systems, tropical cyclones, orographic precipitation, clouds.</summary>
public sealed class WeatherEngine(int gridSize)
{
    private const double CYCLONE_SST = 26;
    private const double CYCLONE_LAT_MIN = 5;
    private const double CYCLONE_LAT_MAX = 20;
    private const double FRONTAL_PRECIP = 500;
    private const double OROG_WINDWARD = 2.0;
    private const double OROG_LEEWARD = 0.5;
    private const double OROG_HEIGHT = 500;

    public WeatherResult Tick(double _timeMa, double deltaMa, SimulationState state, Xoshiro256ss rng)
    {
        var cc = gridSize * gridSize;

        // Pre-generate random rolls sequentially to maintain determinism across
        // parallel execution.  Each cell gets 3 rolls (frontal, extratropical, tropical).
        var roll = new double[cc * 3];
        for (var i = 0; i < roll.Length; i++)
            roll[i] = rng.NextFloat(0.0, 1.0);

        int fronts = 0, cyclones = 0, precipCells = 0;
        var tropicals = new System.Collections.Concurrent.ConcurrentBag<TropicalCyclone>();
        var lockObj = new object();

        Parallel.For(0, gridSize,
            () => (fronts: 0, cyclones: 0, precipCells: 0),
            (row, _, local) =>
            {
                var latDeg = 90.0 - (double)row / (gridSize - 1) * 180;
                var absLat = Math.Abs(latDeg);

                for (var col = 0; col < gridSize; col++)
                {
                    var i = row * gridSize + col;
                    var r0 = roll[i * 3 + 0];
                    var r1 = roll[i * 3 + 1];
                    var r2 = roll[i * 3 + 2];

                    double h = state.HeightMap[i], temp = state.TemperatureMap[i];
                    double windU = state.WindUMap[i];
                    var isOcean = h < 0;
                    var moistBase = isOcean ? 1.0 : 0.4;
                    var tempFactor = Math.Max(0, (temp + 10) / 50);
                    var moisture = moistBase * tempFactor;

                    var oroMult = 1.0;
                    if (h > OROG_HEIGHT)
                    {
                        var nc = (col + (windU > 0 ? -1 : 1) + gridSize) % gridSize;
                        double nh = state.HeightMap[row * gridSize + nc];
                        oroMult = h > nh ? OROG_WINDWARD * (1 + (h - nh) / 2000) : OROG_LEEWARD;
                    }

                    var atPolar = absLat is > 55 and < 65;
                    var atITCZ = absLat < 10;
                    double frontalP = 0;
                    if (atPolar) { frontalP = FRONTAL_PRECIP * moisture * (0.5 + r0); local.fronts++; }
                    else if (atITCZ) { frontalP = FRONTAL_PRECIP * moisture * 1.5 * (0.7 + 0.6 * r0); local.fronts++; }

                    double cyclonicP = 0;
                    if (absLat is > 30 and < 60 && r1 < 0.002 * deltaMa)
                    { cyclonicP = FRONTAL_PRECIP * moisture * (1 + 2 * r2); local.cyclones++; }

                    if (isOcean && temp > CYCLONE_SST && absLat is > CYCLONE_LAT_MIN and < CYCLONE_LAT_MAX
                        && r2 < 0.0005 * deltaMa)
                    {
                        var lon = (double)col / gridSize * 360 - 180;
                        var intensity = Math.Clamp((int)((temp - CYCLONE_SST) / 2) + 1, 1, 5);
                        tropicals.Add(new TropicalCyclone { Lat = latDeg, Lon = lon, Intensity = intensity });
                        cyclonicP += FRONTAL_PRECIP * 4 * moisture;
                    }

                    var total = (frontalP + cyclonicP) * oroMult * deltaMa;
                    state.PrecipitationMap[i] = (float)Math.Max(0, state.PrecipitationMap[i] * 0.9 + total * 0.1);
                    if (state.PrecipitationMap[i] > 10) local.precipCells++;

                    state.CloudTypeMap[i] = (byte)AssignCloud(temp, state.PrecipitationMap[i], moisture, absLat);
                    state.CloudCoverMap[i] = (float)Math.Clamp(moisture * 0.7 + state.PrecipitationMap[i] / 5000, 0, 1);
                }
                return local;
            },
            local =>
            {
                lock (lockObj)
                {
                    fronts += local.fronts;
                    cyclones += local.cyclones;
                    precipCells += local.precipCells;
                }
            });

        var tropicalList = tropicals.ToList();
        return new WeatherResult { FrontCount = fronts, CycloneCount = cyclones + tropicalList.Count, TropicalCyclones = tropicalList, PrecipCells = precipCells };
    }

    private static CloudGenus AssignCloud(double temp, double precip, double moisture, double absLat)
    {
        if (moisture < 0.1 && precip < 50) return CloudGenus.NONE;
        if (absLat < 20 && temp > 20) return CloudGenus.CIRRUS;
        switch (precip)
        {
            case > 400 when temp > 10:
                return CloudGenus.CUMULONIMBUS;
            case > 200:
                return CloudGenus.NIMBOSTRATUS;
        }

        switch (temp)
        {
            case > 15 when moisture > 0.5:
                return CloudGenus.CUMULUS;
            case < 10 when moisture > 0.4:
                return CloudGenus.STRATUS;
        }

        if (absLat is > 30 and < 60 && moisture > 0.3) return CloudGenus.STRATOCUMULUS;
        return precip > 50 ? CloudGenus.ALTOSTRATUS : CloudGenus.CIRROSTRATUS;
    }
}

public sealed class TropicalCyclone
{
    public double Lat { get; set; }
    public double Lon { get; set; }
    public int Intensity { get; set; }
}

public sealed class WeatherResult
{
    public int FrontCount { get; set; }
    public int CycloneCount { get; set; }
    public List<TropicalCyclone> TropicalCyclones { get; set; } = [];
    public int PrecipCells { get; set; }
}
