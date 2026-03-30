using GeoTime.Core.Models;
using GeoTime.Core.Proc;

namespace GeoTime.Core.Engines;

/// <summary>Glacial extent, ice accumulation/ablation, erosion, moraine deposition.</summary>
public sealed class GlacialEngine
{
    private const double GLACIATION_TEMP = -5;
    private const double GLACIAL_EROSION_RATE = 0.02;
    private const double MAX_GLACIAL_EROSION = 30;
    private const double ICE_ACCUMULATION_RATE = 0.5;
    private const double ICE_ABLATION_RATE = 1.0;
    private const double MORAINE_DEPOSITION_RATE = 5;
    private const double MORAINE_FRACTION = 0.5;

    private readonly int _gs;
    public float[] IceThickness { get; }

    public GlacialEngine(int gridSize)
    {
        _gs = gridSize;
        IceThickness = new float[gridSize * gridSize];
    }

    public void Clear() => Array.Fill(IceThickness, 0f);

    public double ComputeELA(float[] temperatureMap)
    {
        int polarBand = (int)(_gs * 0.15);
        double sum = 0; int count = 0;
        for (int row = 0; row < polarBand; row++)
            for (int col = 0; col < _gs; col++) { sum += temperatureMap[row * _gs + col]; count++; }
        for (int row = _gs - polarBand; row < _gs; row++)
            for (int col = 0; col < _gs; col++) { sum += temperatureMap[row * _gs + col]; count++; }
        double mean = count > 0 ? sum / count : 0;
        return Math.Max(0, 3000 + mean * 150);
    }

    public GlacialResult Tick(double timeMa, double deltaMa, SimulationState state,
        StratigraphyStack strat, Xoshiro256ss rng)
    {
        var hm = state.HeightMap;
        var tm = state.TemperatureMap;
        double ela = ComputeELA(tm);
        int glaciated = 0; double totalEroded = 0, totalDepo = 0;
        int cc = _gs * _gs;

        for (int i = 0; i < cc; i++)
        {
            double h = hm[i], temp = tm[i];
            if (h > ela && temp < GLACIATION_TEMP)
                IceThickness[i] += (float)(ICE_ACCUMULATION_RATE * (GLACIATION_TEMP - temp) * deltaMa);
            else if (IceThickness[i] > 0)
            {
                IceThickness[i] -= (float)(ICE_ABLATION_RATE * Math.Max(0, temp - GLACIATION_TEMP) * deltaMa);
                if (IceThickness[i] < 0) IceThickness[i] = 0;
            }
            if (IceThickness[i] <= 0) continue;
            glaciated++;

            int row = i / _gs, col = i % _gs;
            double maxSlope = 0;
            var neighbors = BoundaryClassifier.GetNeighborIndices(row, col, _gs);
            foreach (int n in neighbors)
            {
                double dh = Math.Abs(hm[i] - hm[n]);
                if (dh > maxSlope) maxSlope = dh;
            }

            double erosion = Math.Min(GLACIAL_EROSION_RATE * IceThickness[i] * (1 + maxSlope * 0.001) * deltaMa, MAX_GLACIAL_EROSION);
            if (erosion > 0.01)
            {
                double actual = strat.ErodeTop(i, erosion);
                hm[i] -= (float)actual;
                totalEroded += actual;

                foreach (int n in neighbors)
                {
                    if (IceThickness[n] <= 0 && hm[n] < hm[i])
                    {
                        double moraine = Math.Min(MORAINE_DEPOSITION_RATE * deltaMa, actual * MORAINE_FRACTION);
                        if (moraine > 0.01)
                        {
                            hm[n] += (float)moraine;
                            totalDepo += moraine;
                            strat.PushLayer(n, new StratigraphicLayer
                            {
                                RockType = RockType.SED_TILLITE, AgeDeposited = timeMa,
                                Thickness = moraine, DipAngle = rng.Next() * 5,
                                DipDirection = rng.NextFloat(0, 360), Unconformity = true,
                            });
                        }
                        break;
                    }
                }
            }
        }
        return new GlacialResult { GlaciatedCells = glaciated, EquilibriumLineAltitude = ela, TotalEroded = totalEroded, TotalDeposited = totalDepo };
    }
}

public sealed class GlacialResult
{
    public int GlaciatedCells { get; set; }
    public double EquilibriumLineAltitude { get; set; }
    public double TotalEroded { get; set; }
    public double TotalDeposited { get; set; }
}
