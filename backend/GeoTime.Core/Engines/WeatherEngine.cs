using GeoTime.Core.Models;
using GeoTime.Core.Proc;

namespace GeoTime.Core.Engines;

/// <summary>Frontal systems, tropical cyclones, orographic precipitation, clouds.</summary>
public sealed class WeatherEngine
{
    private const double CYCLONE_SST = 26;
    private const double CYCLONE_LAT_MIN = 5;
    private const double CYCLONE_LAT_MAX = 20;
    private const double FRONTAL_PRECIP = 500;
    private const double OROG_WINDWARD = 2.0;
    private const double OROG_LEEWARD = 0.5;
    private const double OROG_HEIGHT = 500;

    private readonly int _gs;
    public WeatherEngine(int gridSize) => _gs = gridSize;

    public WeatherResult Tick(double _timeMa, double deltaMa, SimulationState state, Xoshiro256ss rng)
    {
        int cc = _gs * _gs;
        int fronts = 0, cyclones = 0, precipCells = 0;
        var tropicals = new List<TropicalCyclone>();

        for (int row = 0; row < _gs; row++)
        {
            double latDeg = 90.0 - (double)row / (_gs - 1) * 180;
            double absLat = Math.Abs(latDeg);

            for (int col = 0; col < _gs; col++)
            {
                int i = row * _gs + col;
                double h = state.HeightMap[i], temp = state.TemperatureMap[i];
                double windU = state.WindUMap[i];
                bool isOcean = h < 0;
                double moistBase = isOcean ? 1.0 : 0.4;
                double tempFactor = Math.Max(0, (temp + 10) / 50);
                double moisture = moistBase * tempFactor;

                double oroMult = 1.0;
                if (h > OROG_HEIGHT)
                {
                    int nc = (col + (windU > 0 ? -1 : 1) + _gs) % _gs;
                    double nh = state.HeightMap[row * _gs + nc];
                    oroMult = h > nh ? OROG_WINDWARD * (1 + (h - nh) / 2000) : OROG_LEEWARD;
                }

                bool atPolar = absLat > 55 && absLat < 65;
                bool atITCZ = absLat < 10;
                double frontalP = 0;
                if (atPolar) { frontalP = FRONTAL_PRECIP * moisture * rng.NextFloat(0.5, 1.5); fronts++; }
                else if (atITCZ) { frontalP = FRONTAL_PRECIP * moisture * 1.5 * rng.NextFloat(0.7, 1.3); fronts++; }

                double cyclonicP = 0;
                if (absLat > 30 && absLat < 60 && rng.NextFloat(0, 1) < 0.002 * deltaMa)
                { cyclonicP = FRONTAL_PRECIP * moisture * rng.NextFloat(1, 3); cyclones++; }

                if (isOcean && temp > CYCLONE_SST && absLat > CYCLONE_LAT_MIN && absLat < CYCLONE_LAT_MAX
                    && rng.NextFloat(0, 1) < 0.0005 * deltaMa)
                {
                    double lon = (double)col / _gs * 360 - 180;
                    int intensity = Math.Clamp((int)((temp - CYCLONE_SST) / 2) + 1, 1, 5);
                    tropicals.Add(new TropicalCyclone { Lat = latDeg, Lon = lon, Intensity = intensity });
                    cyclonicP += FRONTAL_PRECIP * 4 * moisture;
                }

                double total = (frontalP + cyclonicP) * oroMult * deltaMa;
                state.PrecipitationMap[i] = (float)Math.Max(0, state.PrecipitationMap[i] * 0.9 + total * 0.1);
                if (state.PrecipitationMap[i] > 10) precipCells++;

                state.CloudTypeMap[i] = (byte)AssignCloud(temp, state.PrecipitationMap[i], moisture, absLat);
                state.CloudCoverMap[i] = (float)Math.Clamp(moisture * 0.7 + state.PrecipitationMap[i] / 5000, 0, 1);
            }
        }
        return new WeatherResult { FrontCount = fronts, CycloneCount = cyclones + tropicals.Count, TropicalCyclones = tropicals, PrecipCells = precipCells };
    }

    private static CloudGenus AssignCloud(double temp, double precip, double moisture, double absLat)
    {
        if (moisture < 0.1 && precip < 50) return CloudGenus.NONE;
        if (absLat < 20 && temp > 20) return CloudGenus.CIRRUS;
        if (precip > 400 && temp > 10) return CloudGenus.CUMULONIMBUS;
        if (precip > 200) return CloudGenus.NIMBOSTRATUS;
        if (temp > 15 && moisture > 0.5) return CloudGenus.CUMULUS;
        if (temp < 10 && moisture > 0.4) return CloudGenus.STRATUS;
        if (absLat > 30 && absLat < 60 && moisture > 0.3) return CloudGenus.STRATOCUMULUS;
        if (precip > 50) return CloudGenus.ALTOSTRATUS;
        return CloudGenus.CIRROSTRATUS;
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
    public List<TropicalCyclone> TropicalCyclones { get; set; } = new();
    public int PrecipCells { get; set; }
}
