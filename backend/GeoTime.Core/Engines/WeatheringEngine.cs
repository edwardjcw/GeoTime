using GeoTime.Core.Models;
using GeoTime.Core.Proc;

namespace GeoTime.Core.Engines;

/// <summary>Aeolian and chemical weathering engine.</summary>
public sealed class WeatheringEngine(int gridSize)
{
    private const double CHEM_BASE = 0.005;
    private const double CHEM_TEMP_FACTOR = 0.07;
    private const double CHEM_PRECIP_FACTOR = 0.001;
    private const double CHEM_MIN_TEMP = -10;
    private const double WIND_THRESHOLD = 4;
    private const double AEOLIAN_RATE = 0.01;
    private const double LOESS_RATE = 0.005;
    private const double MAX_WEATHERING = 10;
    private const double ARID_PRECIP = 250;
    private const double TROPICAL_TEMP = 20;
    private const double PRODUCT_DENSITY = 0.3;
    private const double LOESS_FRACTION = 0.5;

    public static RockType GetWeatheringProduct(RockType parent, double temp, double precip)
    {
        if (temp > TROPICAL_TEMP && precip > 1000) return RockType.SED_LATERITE;
        return precip < ARID_PRECIP ? RockType.SED_CALICHE : RockType.SED_REGOLITH;
    }

    public static double ChemicalWeatheringRate(double temp, double precip)
    {
        if (temp < CHEM_MIN_TEMP) return 0;
        return CHEM_BASE * Math.Exp(CHEM_TEMP_FACTOR * (temp - 15)) * (1 + precip * CHEM_PRECIP_FACTOR);
    }

    public WeatheringResult Tick(double timeMa, double deltaMa, SimulationState state,
        StratigraphyStack strat, Xoshiro256ss rng)
    {
        var cc = gridSize * gridSize;
        double chemW = 0, aeolW = 0, totalDepo = 0;
        var affected = 0;

        for (var i = 0; i < cc; i++)
        {
            if (state.HeightMap[i] <= 0) continue;
            double temp = state.TemperatureMap[i], precip = state.PrecipitationMap[i];
            double wu = state.WindUMap[i], wv = state.WindVMap[i];
            var ws2 = wu * wu + wv * wv;

            var chemRate = ChemicalWeatheringRate(temp, precip);
            var chemAmt = Math.Min(chemRate * deltaMa, MAX_WEATHERING);
            if (chemAmt > 0.001)
            {
                var top = strat.GetTopLayer(i);
                var parent = top?.RockType ?? RockType.IGN_GRANITE;
                var product = GetWeatheringProduct(parent, temp, precip);
                var eroded = strat.ErodeTop(i, chemAmt);
                if (eroded > 0)
                {
                    strat.PushLayer(i, new StratigraphicLayer
                    {
                        RockType = product, AgeDeposited = timeMa,
                        Thickness = eroded * PRODUCT_DENSITY,
                    });
                    state.HeightMap[i] -= (float)(eroded * (1 - PRODUCT_DENSITY));
                    chemW += eroded; totalDepo += eroded * PRODUCT_DENSITY; affected++;
                }
            }

            if (!(precip < ARID_PRECIP) || !(ws2 > WIND_THRESHOLD)) continue;
            var excess = Math.Sqrt(ws2) - Math.Sqrt(WIND_THRESHOLD);
            var aeolAmt = Math.Min(AEOLIAN_RATE * excess * deltaMa, MAX_WEATHERING);
            if (!(aeolAmt > 0.001)) continue;
            var erodeTop = strat.ErodeTop(i, aeolAmt);
            if (!(erodeTop > 0)) continue;
            state.HeightMap[i] -= (float)erodeTop;
            aeolW += erodeTop; affected++;
            int row = i / gridSize, col = i % gridSize;
            var wm = Math.Sqrt(ws2);
            var dCol = wm > 0 ? (int)Math.Round(wu / wm) : 0;
            var dRow = wm > 0 ? (int)Math.Round(wv / wm) : 0;
            var dr = Math.Clamp(row + dRow, 0, gridSize - 1);
            var dc = (col + dCol + gridSize) % gridSize;
            var di = dr * gridSize + dc;
            if (di == i) continue;
            var loess = Math.Min(erodeTop * LOESS_FRACTION, LOESS_RATE * deltaMa);
            if (!(loess > 0.001)) continue;
            state.HeightMap[di] += (float)loess;
            totalDepo += loess;
            strat.PushLayer(di, new StratigraphicLayer
            {
                RockType = RockType.SED_LOESS, AgeDeposited = timeMa, Thickness = loess,
            });
        }
        return new WeatheringResult { ChemicalWeathered = chemW, AeolianEroded = aeolW, TotalDeposited = totalDepo, CellsAffected = affected };
    }
}

public sealed class WeatheringResult
{
    public double ChemicalWeathered { get; set; }
    public double AeolianEroded { get; set; }
    public double TotalDeposited { get; set; }
    public int CellsAffected { get; set; }
}
