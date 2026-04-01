using GeoTime.Core.Models;

namespace GeoTime.Core.Engines;

/// <summary>Soil formation using simplified USDA taxonomy (CLORPT framework).</summary>
public sealed class PedogenesisEngine(int gridSize)
{
    private const double MIN_SOIL_TEMP = -20;
    private const double MIN_SOIL_PRECIP = 10;
    private const double BASE_SOIL_RATE = 0.01;
    private const double MAX_SOIL_DEPTH = 5;

    public static SoilOrder ClassifySoil(double temp, double precip, RockType parent, double depth, double height)
    {
        if (temp < -2) return SoilOrder.GELISOL;
        if (parent is RockType.IGN_BASALT or RockType.IGN_ANDESITE or RockType.IGN_DACITE
            or RockType.IGN_TUFF or RockType.IGN_PYROCLASTIC) return SoilOrder.ANDISOL;
        if (depth < 0.2) return SoilOrder.ENTISOL;
        if (precip < 250) return SoilOrder.ARIDISOL;
        if (depth < 0.5) return SoilOrder.INCEPTISOL;
        return temp switch
        {
            > 22 when precip > 1500 => SoilOrder.OXISOL,
            > 15 when precip > 1000 => SoilOrder.ULTISOL,
            < 10 when precip > 500 => SoilOrder.SPODOSOL,
            _ => precip switch
            {
                > 800 when height < 200 => SoilOrder.HISTOSOL,
                >= 500 and <= 1000 when temp > 18 && parent is RockType.SED_SHALE or RockType.SED_MUDSTONE => SoilOrder
                    .VERTISOL,
                >= 600 and <= 1200 when temp >= 5 => SoilOrder.ALFISOL,
                >= 400 and <= 800 when temp >= 0 => SoilOrder.MOLLISOL,
                _ => SoilOrder.INCEPTISOL
            }
        };
    }

    public static double SoilFormationRate(double temp, double precip, RockType parent)
    {
        if (temp < MIN_SOIL_TEMP || precip < MIN_SOIL_PRECIP) return 0;
        var tf = Math.Clamp((temp + 20) / 50.0, 0, 1);
        var pf = Math.Clamp(precip / 2000.0, 0, 1);
        var hf = (int)parent switch
        {
            < (int)RockType.SED_SANDSTONE => 0.5,
            >= (int)RockType.MET_SLATE => 0.6,
            _ => 1.0
        };
        return BASE_SOIL_RATE * tf * pf * hf;
    }

    public PedogenesisResult Tick(double timeMa, double deltaMa, SimulationState state, StratigraphyStack strat)
    {
        var cc = gridSize * gridSize;
        int formed = 0, classified = 0;
        for (var i = 0; i < cc; i++)
        {
            if (state.HeightMap[i] <= 0) continue;
            double temp = state.TemperatureMap[i], precip = state.PrecipitationMap[i];
            var top = strat.GetTopLayer(i);
            var parent = top?.RockType ?? RockType.IGN_GRANITE;
            var rate = SoilFormationRate(temp, precip, parent);
            if (rate <= 0) continue;
            var newDepth = Math.Min(MAX_SOIL_DEPTH, state.SoilDepthMap[i] + rate * deltaMa);
            state.SoilDepthMap[i] = (float)newDepth;
            formed++;
            var order = ClassifySoil(temp, precip, parent, newDepth, state.HeightMap[i]);
            state.SoilTypeMap[i] = (byte)order;
            if (order == SoilOrder.NONE) continue;
            classified++;
            top?.SoilHorizon = order;
        }
        return new PedogenesisResult { CellsFormed = formed, ClassifiedCells = classified };
    }
}

public sealed class PedogenesisResult
{
    public int CellsFormed { get; set; }
    public int ClassifiedCells { get; set; }
}
