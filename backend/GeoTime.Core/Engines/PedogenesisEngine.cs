using GeoTime.Core.Models;

namespace GeoTime.Core.Engines;

/// <summary>Soil formation using simplified USDA taxonomy (CLORPT framework).</summary>
public sealed class PedogenesisEngine
{
    private const double MIN_SOIL_TEMP = -20;
    private const double MIN_SOIL_PRECIP = 10;
    private const double BASE_SOIL_RATE = 0.01;
    private const double MAX_SOIL_DEPTH = 5;

    private readonly int _gs;
    public PedogenesisEngine(int gridSize) => _gs = gridSize;

    public SoilOrder ClassifySoil(double temp, double precip, RockType parent, double depth, double height)
    {
        if (temp < -2) return SoilOrder.GELISOL;
        if (parent is RockType.IGN_BASALT or RockType.IGN_ANDESITE or RockType.IGN_DACITE
            or RockType.IGN_TUFF or RockType.IGN_PYROCLASTIC) return SoilOrder.ANDISOL;
        if (depth < 0.2) return SoilOrder.ENTISOL;
        if (precip < 250) return SoilOrder.ARIDISOL;
        if (depth < 0.5) return SoilOrder.INCEPTISOL;
        if (temp > 22 && precip > 1500) return SoilOrder.OXISOL;
        if (temp > 15 && precip > 1000) return SoilOrder.ULTISOL;
        if (temp < 10 && precip > 500) return SoilOrder.SPODOSOL;
        if (precip > 800 && height < 200) return SoilOrder.HISTOSOL;
        if (precip >= 500 && precip <= 1000 && temp > 18
            && (parent is RockType.SED_SHALE or RockType.SED_MUDSTONE)) return SoilOrder.VERTISOL;
        if (precip >= 600 && precip <= 1200 && temp >= 5) return SoilOrder.ALFISOL;
        if (precip >= 400 && precip <= 800 && temp >= 0) return SoilOrder.MOLLISOL;
        return SoilOrder.INCEPTISOL;
    }

    public double SoilFormationRate(double temp, double precip, RockType parent)
    {
        if (temp < MIN_SOIL_TEMP || precip < MIN_SOIL_PRECIP) return 0;
        double tf = Math.Clamp((temp + 20) / 50.0, 0, 1);
        double pf = Math.Clamp(precip / 2000.0, 0, 1);
        double hf = 1.0;
        if ((int)parent < (int)RockType.SED_SANDSTONE) hf = 0.5;
        else if ((int)parent >= (int)RockType.MET_SLATE) hf = 0.6;
        return BASE_SOIL_RATE * tf * pf * hf;
    }

    public PedogenesisResult Tick(double timeMa, double deltaMa, SimulationState state, StratigraphyStack strat)
    {
        int cc = _gs * _gs;
        int formed = 0, classified = 0;
        for (int i = 0; i < cc; i++)
        {
            if (state.HeightMap[i] <= 0) continue;
            double temp = state.TemperatureMap[i], precip = state.PrecipitationMap[i];
            var top = strat.GetTopLayer(i);
            var parent = top?.RockType ?? RockType.IGN_GRANITE;
            double rate = SoilFormationRate(temp, precip, parent);
            if (rate <= 0) continue;
            double newDepth = Math.Min(MAX_SOIL_DEPTH, state.SoilDepthMap[i] + rate * deltaMa);
            state.SoilDepthMap[i] = (float)newDepth;
            formed++;
            var order = ClassifySoil(temp, precip, parent, newDepth, state.HeightMap[i]);
            state.SoilTypeMap[i] = (byte)order;
            if (order != SoilOrder.NONE) { classified++; if (top != null) top.SoilHorizon = order; }
        }
        return new PedogenesisResult { CellsFormed = formed, ClassifiedCells = classified };
    }
}

public sealed class PedogenesisResult
{
    public int CellsFormed { get; set; }
    public int ClassifiedCells { get; set; }
}
