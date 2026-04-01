using GeoTime.Core.Models;
using GeoTime.Core.Proc;

namespace GeoTime.Core.Engines;

/// <summary>General circulation model + ice-age forcing.</summary>
public sealed class ClimateEngine(int gridSize)
{
    private const double S0 = 1361;
    private const double LAMBDA = 3.0;
    private const double CO2_REF = 280;
    private const double LAPSE_RATE = 6.5;
    private const double ALBEDO_OCEAN = 0.06;
    private const double ALBEDO_LAND = 0.30;
    private const double ALBEDO_ICE = 0.85;
    private const double SNOWBALL_THRESHOLD = -10;

    public ClimateResult Tick(double timeMa, double deltaMa, SimulationState state,
        AtmosphericComposition atmo, Xoshiro256ss _rng)
    {
        var cc = gridSize * gridSize;
        var co2Ppm = atmo.CO2 * 1_000_000;
        var dT_ghg = co2Ppm > 0 ? LAMBDA * Math.Log(co2Ppm / CO2_REF) / Math.Log(2) : 0;
        var dT_milan = 2 * Math.Sin(timeMa * 2 * Math.PI / 100);

        double tempSum = 0, eqTempSum = 0;
        int eqCount = 0, iceCells = 0;

        for (var row = 0; row < gridSize; row++)
        {
            var latDeg = 90.0 - (double)row / (gridSize - 1) * 180;
            var latRad = latDeg * Math.PI / 180;
            for (var col = 0; col < gridSize; col++)
            {
                var i = row * gridSize + col;
                double h = state.HeightMap[i];
                var isIce = state.TemperatureMap[i] < -5;
                var albedo = isIce ? ALBEDO_ICE : h < 0 ? ALBEDO_OCEAN : ALBEDO_LAND;
                if (isIce) iceCells++;

                var T_base = 30 * Math.Cos(latRad);
                var hKm = Math.Max(0, h / 1000);
                var T_final = T_base - hKm * LAPSE_RATE + dT_ghg + dT_milan;

                var alpha = Math.Min(1, deltaMa * 0.5);
                state.TemperatureMap[i] = (float)(state.TemperatureMap[i] * (1 - alpha) + T_final * alpha);
                tempSum += state.TemperatureMap[i];

                if (!(Math.Abs(latDeg) < 10)) continue;
                eqTempSum += state.TemperatureMap[i]; eqCount++;
            }
        }

        // 3-cell circulation winds
        for (var row = 0; row < gridSize; row++)
        {
            var latDeg = 90.0 - (double)row / (gridSize - 1) * 180;
            var absLat = Math.Abs(latDeg);
            double sign = latDeg >= 0 ? 1 : -1;
            for (var col = 0; col < gridSize; col++)
            {
                var i = row * gridSize + col;
                double u, v;
                switch (absLat)
                {
                    case <= 30:
                        u = -Math.Cos(absLat * Math.PI / 30); v = sign * -0.3 * Math.Sin(absLat * Math.PI / 30);
                        break;
                    case <= 60:
                        u = Math.Cos((absLat - 45) * Math.PI / 30); v = sign * 0.1 * Math.Cos((absLat - 45) * Math.PI / 30);
                        break;
                    default:
                        u = -Math.Cos((absLat - 75) * Math.PI / 15); v = sign * -0.2 * Math.Sin((absLat - 75) * Math.PI / 15);
                        break;
                }
                state.WindUMap[i] = (float)u;
                state.WindVMap[i] = (float)v;
            }
        }

        var meanT = tempSum / cc;
        var eqT = eqCount > 0 ? eqTempSum / eqCount : meanT;

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
