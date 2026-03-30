using GeoTime.Core.Models;
using GeoTime.Core.Proc;

namespace GeoTime.Core.Engines;

/// <summary>Aeolian and chemical weathering engine.</summary>
public sealed class WeatheringEngine
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

    private readonly int _gs;
    public WeatheringEngine(int gridSize) => _gs = gridSize;

    public RockType GetWeatheringProduct(RockType parent, double temp, double precip)
    {
        if (temp > TROPICAL_TEMP && precip > 1000) return RockType.SED_LATERITE;
        if (precip < ARID_PRECIP) return RockType.SED_CALICHE;
        if (parent is RockType.SED_LIMESTONE or RockType.SED_DOLOSTONE or RockType.SED_CHALK)
            return RockType.SED_REGOLITH;
        return RockType.SED_REGOLITH;
    }

    public double ChemicalWeatheringRate(double temp, double precip)
    {
        if (temp < CHEM_MIN_TEMP) return 0;
        return CHEM_BASE * Math.Exp(CHEM_TEMP_FACTOR * (temp - 15)) * (1 + precip * CHEM_PRECIP_FACTOR);
    }

    public WeatheringResult Tick(double timeMa, double deltaMa, SimulationState state,
        StratigraphyStack strat, Xoshiro256ss rng)
    {
        int cc = _gs * _gs;
        double chemW = 0, aeolW = 0, totalDepo = 0;
        int affected = 0;

        for (int i = 0; i < cc; i++)
        {
            if (state.HeightMap[i] <= 0) continue;
            double temp = state.TemperatureMap[i], precip = state.PrecipitationMap[i];
            double wu = state.WindUMap[i], wv = state.WindVMap[i];
            double ws2 = wu * wu + wv * wv;

            double chemRate = ChemicalWeatheringRate(temp, precip);
            double chemAmt = Math.Min(chemRate * deltaMa, MAX_WEATHERING);
            if (chemAmt > 0.001)
            {
                var top = strat.GetTopLayer(i);
                var parent = top?.RockType ?? RockType.IGN_GRANITE;
                var product = GetWeatheringProduct(parent, temp, precip);
                double eroded = strat.ErodeTop(i, chemAmt);
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

            if (precip < ARID_PRECIP && ws2 > WIND_THRESHOLD)
            {
                double excess = Math.Sqrt(ws2) - Math.Sqrt(WIND_THRESHOLD);
                double aeolAmt = Math.Min(AEOLIAN_RATE * excess * deltaMa, MAX_WEATHERING);
                if (aeolAmt > 0.001)
                {
                    double eroded = strat.ErodeTop(i, aeolAmt);
                    if (eroded > 0)
                    {
                        state.HeightMap[i] -= (float)eroded;
                        aeolW += eroded; affected++;
                        int row = i / _gs, col = i % _gs;
                        double wm = Math.Sqrt(ws2);
                        int dCol = wm > 0 ? (int)Math.Round(wu / wm) : 0;
                        int dRow = wm > 0 ? (int)Math.Round(wv / wm) : 0;
                        int dr = Math.Clamp(row + dRow, 0, _gs - 1);
                        int dc = (col + dCol + _gs) % _gs;
                        int di = dr * _gs + dc;
                        if (di != i)
                        {
                            double loess = Math.Min(eroded * LOESS_FRACTION, LOESS_RATE * deltaMa);
                            if (loess > 0.001)
                            {
                                state.HeightMap[di] += (float)loess;
                                totalDepo += loess;
                                strat.PushLayer(di, new StratigraphicLayer
                                {
                                    RockType = RockType.SED_LOESS, AgeDeposited = timeMa, Thickness = loess,
                                });
                            }
                        }
                    }
                }
            }
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
