using GeoTime.Core.Models;
using GeoTime.Core.Proc;

namespace GeoTime.Core.Engines;

/// <summary>General circulation model + ice-age forcing.</summary>
public sealed class ClimateEngine
{
    private const double S0 = 1361;
    private const double LAMBDA = 3.0;
    private const double CO2_REF = 280;
    private const double LAPSE_RATE = 6.5;
    private const double ALBEDO_OCEAN = 0.06;
    private const double ALBEDO_LAND = 0.30;
    private const double ALBEDO_ICE = 0.85;
    private const double SNOWBALL_THRESHOLD = -10;

    private readonly int _gs;
    public ClimateEngine(int gridSize) => _gs = gridSize;

    public ClimateResult Tick(double timeMa, double deltaMa, SimulationState state,
        AtmosphericComposition atmo, Xoshiro256ss _rng)
    {
        int cc = _gs * _gs;
        double co2Ppm = atmo.CO2 * 1_000_000;
        double dT_ghg = co2Ppm > 0 ? LAMBDA * Math.Log(co2Ppm / CO2_REF) / Math.Log(2) : 0;
        double dT_milan = 2 * Math.Sin(timeMa * 2 * Math.PI / 100);

        double tempSum = 0, eqTempSum = 0;
        int eqCount = 0, iceCells = 0;

        for (int row = 0; row < _gs; row++)
        {
            double latDeg = 90.0 - (double)row / (_gs - 1) * 180;
            double latRad = latDeg * Math.PI / 180;
            for (int col = 0; col < _gs; col++)
            {
                int i = row * _gs + col;
                double h = state.HeightMap[i];
                bool isIce = state.TemperatureMap[i] < -5;
                double albedo = isIce ? ALBEDO_ICE : (h < 0 ? ALBEDO_OCEAN : ALBEDO_LAND);
                if (isIce) iceCells++;

                double T_base = 30 * Math.Cos(latRad);
                double hKm = Math.Max(0, h / 1000);
                double T_final = T_base - hKm * LAPSE_RATE + dT_ghg + dT_milan;

                double alpha = Math.Min(1, deltaMa * 0.5);
                state.TemperatureMap[i] = (float)(state.TemperatureMap[i] * (1 - alpha) + T_final * alpha);
                tempSum += state.TemperatureMap[i];

                if (Math.Abs(latDeg) < 10) { eqTempSum += state.TemperatureMap[i]; eqCount++; }
            }
        }

        // 3-cell circulation winds
        for (int row = 0; row < _gs; row++)
        {
            double latDeg = 90.0 - (double)row / (_gs - 1) * 180;
            double absLat = Math.Abs(latDeg);
            double sign = latDeg >= 0 ? 1 : -1;
            for (int col = 0; col < _gs; col++)
            {
                int i = row * _gs + col;
                double u, v;
                if (absLat <= 30) { u = -Math.Cos(absLat * Math.PI / 30); v = sign * -0.3 * Math.Sin(absLat * Math.PI / 30); }
                else if (absLat <= 60) { u = Math.Cos((absLat - 45) * Math.PI / 30); v = sign * 0.1 * Math.Cos((absLat - 45) * Math.PI / 30); }
                else { u = -Math.Cos((absLat - 75) * Math.PI / 15); v = sign * -0.2 * Math.Sin((absLat - 75) * Math.PI / 15); }
                state.WindUMap[i] = (float)u;
                state.WindVMap[i] = (float)v;
            }
        }

        double meanT = tempSum / cc;
        double eqT = eqCount > 0 ? eqTempSum / eqCount : meanT;

        return new ClimateResult
        {
            MeanTemperature = meanT, EquatorialTemperature = eqT,
            CO2Ppm = co2Ppm, IceAlbedoFeedback = (double)iceCells / cc,
            SnowballTriggered = eqT < SNOWBALL_THRESHOLD, IceCells = iceCells,
        };
    }
}

public sealed class ClimateResult
{
    public double MeanTemperature { get; set; }
    public double EquatorialTemperature { get; set; }
    public double CO2Ppm { get; set; }
    public double IceAlbedoFeedback { get; set; }
    public bool SnowballTriggered { get; set; }
    public int IceCells { get; set; }
}
