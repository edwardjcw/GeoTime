using GeoTime.Core.Models;
using GeoTime.Core.Proc;

namespace GeoTime.Core.Engines;

/// <summary>Fluvial erosion using D∞ flow routing and stream power law.</summary>
public sealed class ErosionEngine(int gridSize)
{
    private const double K_EROSION = 1e-4;
    private const double M_AREA = 0.5;
    private const double N_SLOPE = 1.0;
    private const double MAX_EROSION = 50;
    private const int MIN_RIVER_CELLS = 16;
    private const double DEPOSITION_RATE = 0.3;
    private const double MIN_SLOPE = 1e-6;
    private const double DEPO_SLOPE_THRESHOLD = 0.3;

    /// <summary>
    /// Multiplier applied to K_EROSION for cells identified as river channels
    /// (i.e., RiverChannelMap[i] ≥ <see cref="RiverChannelErodeThreshold"/>).
    /// Enhanced channel erosion deepens river valleys over geological time.
    /// </summary>
    private const double RiverChannelErosionBoost = 3.0;

    /// <summary>
    /// Minimum flow-accumulation value (from
    /// <see cref="SimulationState.RiverChannelMap"/>) for a cell to receive the
    /// river-channel erosion boost.
    /// </summary>
    private const float RiverChannelErodeThreshold = 500f;

    public ErosionResult Tick(double timeMa, double deltaMa, SimulationState state,
        StratigraphyStack strat, Xoshiro256ss rng)
    {
        var cc = gridSize * gridSize;
        var hm = state.HeightMap;
        var flow = ComputeFlowGraph(hm);
        var area = ComputeDrainageArea(flow);

        var indices = Enumerable.Range(0, cc).OrderByDescending(i => hm[i]).ToArray();
        var sedLoad = new float[cc];
        double totalEroded = 0, totalDepo = 0;
        var affected = 0;
        var rivers = new List<int>();

        foreach (var i in indices)
        {
            var slope = Math.Max(flow[i].slope, MIN_SLOPE);
            // Apply a river-channel erosion boost when the cell lies in a recognised
            // river channel (high flow accumulation populated by HydroDetectorService).
            double channelBoost = state.RiverChannelMap[i] >= RiverChannelErodeThreshold
                ? RiverChannelErosionBoost : 1.0;
            var erode = Math.Min(K_EROSION * channelBoost * Math.Pow(area[i], M_AREA)
                                           * Math.Pow(slope, N_SLOPE) * deltaMa, MAX_EROSION);

            if (erode > 0.01)
            {
                var actual = strat.ErodeTop(i, erode);
                hm[i] -= (float)actual;
                totalEroded += actual;
                if (actual > 0) affected++;
                sedLoad[i] += (float)actual;
            }

            var ds = flow[i].downstream;
            if (ds >= 0 && sedLoad[i] > 0)
            {
                var dsSlope = flow[ds].slope;
                if (dsSlope < slope * DEPO_SLOPE_THRESHOLD || hm[ds] < 0)
                {
                    var deposit = sedLoad[i] * DEPOSITION_RATE;
                    if (deposit > 0.01)
                    {
                        hm[ds] += (float)deposit;
                        totalDepo += deposit;
                        var underwater = hm[ds] < 0;
                        strat.PushLayer(ds, new StratigraphicLayer
                        {
                            RockType = underwater ? RockType.SED_MUDSTONE : RockType.SED_SANDSTONE,
                            AgeDeposited = timeMa, Thickness = deposit,
                            DipAngle = 1 + rng.Next() * 3,
                            DipDirection = rng.NextFloat(0, 360),
                            Deformation = DeformationType.UNDEFORMED,
                        });
                    }
                    sedLoad[i] -= (float)(sedLoad[i] * DEPOSITION_RATE);
                }
                sedLoad[ds] += sedLoad[i];
            }
            if (area[i] >= MIN_RIVER_CELLS) rivers.Add(i);
        }

        return new ErosionResult
        {
            TotalEroded = totalEroded, TotalDeposited = totalDepo,
            CellsAffected = affected, RiverCells = rivers,
        };
    }

    private (int downstream, double slope)[] ComputeFlowGraph(float[] hm)
    {
        var cc = gridSize * gridSize;
        var flow = new (int downstream, double slope)[cc];
        for (var i = 0; i < cc; i++)
        {
            int row = i / gridSize, col = i % gridSize;
            double h = hm[i];
            var bestIdx = -1;
            var bestSlope = MIN_SLOPE;
            foreach (var n in BoundaryClassifier.GetNeighborIndices(row, col, gridSize))
            {
                var dh = h - hm[n];
                if (!(dh > bestSlope)) continue;
                bestSlope = dh; bestIdx = n;
            }
            flow[i] = (bestIdx, bestSlope);
        }
        return flow;
    }

    private static float[] ComputeDrainageArea((int downstream, double slope)[] flow)
    {
        var cc = flow.Length;
        var area = new float[cc];
        Array.Fill(area, 1f);
        var sorted = Enumerable.Range(0, cc).OrderByDescending(i => flow[i].slope).ToArray();
        foreach (var i in sorted)
            if (flow[i].downstream >= 0) area[flow[i].downstream] += area[i];
        return area;
    }
}

public sealed class ErosionResult
{
    public double TotalEroded { get; set; }
    public double TotalDeposited { get; set; }
    public int CellsAffected { get; set; }
    public List<int> RiverCells { get; set; } = [];
}
