using GeoTime.Core.Compute;
using GeoTime.Core.Models;
using GeoTime.Core.Proc;

namespace GeoTime.Core.Engines;

/// <summary>Glacial extent, ice accumulation/ablation, erosion, moraine deposition.</summary>
public sealed class GlacialEngine(int gridSize, GpuComputeService? gpu = null)
{
    private const double GLACIATION_TEMP = -5;
    private const double GLACIAL_EROSION_RATE = 0.02;
    private const double MAX_GLACIAL_EROSION = 30;
    private const double ICE_ACCUMULATION_RATE = 0.5;
    private const double ICE_ABLATION_RATE = 1.0;
    private const double MORAINE_DEPOSITION_RATE = 5;
    private const double MORAINE_FRACTION = 0.5;

    public float[] IceThickness { get; } = new float[gridSize * gridSize];

    public void Clear() => Array.Fill(IceThickness, 0f);

    public double ComputeELA(float[] temperatureMap)
    {
        var polarBand = (int)(gridSize * 0.15);
        double sum = 0; var count = 0;
        for (var row = 0; row < polarBand; row++)
            for (var col = 0; col < gridSize; col++) { sum += temperatureMap[row * gridSize + col]; count++; }
        for (var row = gridSize - polarBand; row < gridSize; row++)
            for (var col = 0; col < gridSize; col++) { sum += temperatureMap[row * gridSize + col]; count++; }
        var mean = count > 0 ? sum / count : 0;
        return Math.Max(0, 3000 + mean * 150);
    }

    public GlacialResult Tick(double timeMa, double deltaMa, SimulationState state,
        StratigraphyStack strat, Xoshiro256ss rng)
    {
        var hm = state.HeightMap;
        var tm = state.TemperatureMap;
        var ela = ComputeELA(tm);
        var glaciated = 0; double totalEroded = 0, totalDepo = 0;
        var cc = gridSize * gridSize;

        // ── Phase 1: per-cell ice accumulation/ablation (GPU or CPU) ──────────
        bool gpuIceDone = false;
        if (gpu != null)
        {
            try
            {
                gpu.UpdateIceThickness(IceThickness, hm, tm,
                    (float)ela, (float)deltaMa, (float)GLACIATION_TEMP,
                    (float)ICE_ACCUMULATION_RATE, (float)ICE_ABLATION_RATE);
                gpuIceDone = true;
            }
            catch
            {
                // GPU ice thickness update failed — fall through to CPU path
            }
        }

        if (!gpuIceDone)
        {
            for (var i = 0; i < cc; i++)
            {
                double h = hm[i], temp = tm[i];
                if (h > ela && temp < GLACIATION_TEMP)
                    IceThickness[i] += (float)(ICE_ACCUMULATION_RATE * (GLACIATION_TEMP - temp) * deltaMa);
                else if (IceThickness[i] > 0)
                {
                    IceThickness[i] -= (float)(ICE_ABLATION_RATE * Math.Max(0, temp - GLACIATION_TEMP) * deltaMa);
                    if (IceThickness[i] < 0) IceThickness[i] = 0;
                }
            }
        }

        // ── Phase 2: erosion/moraine (sequential, neighbor-dependent, stays on CPU)
        for (var i = 0; i < cc; i++)
        {
            if (IceThickness[i] <= 0) continue;
            glaciated++;

            int row = i / gridSize, col = i % gridSize;
            double maxSlope = 0;
            var neighbors = BoundaryClassifier.GetNeighborIndices(row, col, gridSize);
            foreach (var n in neighbors)
            {
                double dh = Math.Abs(hm[i] - hm[n]);
                if (dh > maxSlope) maxSlope = dh;
            }

            var erosion = Math.Min(GLACIAL_EROSION_RATE * IceThickness[i] * (1 + maxSlope * 0.001) * deltaMa, MAX_GLACIAL_EROSION);
            if (!(erosion > 0.01)) continue;
            var actual = strat.ErodeTop(i, erosion);
            hm[i] -= (float)actual;
            totalEroded += actual;

            foreach (var n in neighbors)
            {
                if (!(IceThickness[n] <= 0) || !(hm[n] < hm[i])) continue;
                var moraine = Math.Min(MORAINE_DEPOSITION_RATE * deltaMa, actual * MORAINE_FRACTION);
                if (moraine > 0.01)
                {
                    hm[n] += (float)moraine;
                    totalDepo += moraine;
                    strat.PushLayer(n, new StratigraphicLayer
                    {
                        RockType = RockType.SED_TILLITE, AgeDeposited = timeMa,
                        Thickness = moraine, DipAngle = rng.Next() * 5,
                        DipDirection = rng.NextFloat(0, 360), Unconformity = true,
                        Deformation = DeformationType.UNDEFORMED,
                    });
                }
                break;
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
