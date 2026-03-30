using GeoTime.Core.Models;
using GeoTime.Core.Proc;

namespace GeoTime.Core.Engines;

/// <summary>Fluvial erosion using D∞ flow routing and stream power law.</summary>
public sealed class ErosionEngine
{
    private const double K_EROSION = 1e-4;
    private const double M_AREA = 0.5;
    private const double N_SLOPE = 1.0;
    private const double MAX_EROSION = 50;
    private const int MIN_RIVER_CELLS = 16;
    private const double DEPOSITION_RATE = 0.3;
    private const double MIN_SLOPE = 1e-6;
    private const double DEPO_SLOPE_THRESHOLD = 0.3;

    private readonly int _gs;
    public ErosionEngine(int gridSize) => _gs = gridSize;

    public ErosionResult Tick(double timeMa, double deltaMa, SimulationState state,
        StratigraphyStack strat, Xoshiro256ss rng)
    {
        int cc = _gs * _gs;
        var hm = state.HeightMap;
        var flow = ComputeFlowGraph(hm);
        var area = ComputeDrainageArea(flow);

        var indices = Enumerable.Range(0, cc).OrderByDescending(i => hm[i]).ToArray();
        var sedLoad = new float[cc];
        double totalEroded = 0, totalDepo = 0;
        int affected = 0;
        var rivers = new List<int>();

        foreach (int i in indices)
        {
            double slope = Math.Max(flow[i].slope, MIN_SLOPE);
            double erode = Math.Min(K_EROSION * Math.Pow(area[i], M_AREA)
                * Math.Pow(slope, N_SLOPE) * deltaMa, MAX_EROSION);

            if (erode > 0.01)
            {
                double actual = strat.ErodeTop(i, erode);
                hm[i] -= (float)actual;
                totalEroded += actual;
                if (actual > 0) affected++;
                sedLoad[i] += (float)actual;
            }

            int ds = flow[i].downstream;
            if (ds >= 0 && sedLoad[i] > 0)
            {
                double dsSlope = flow[ds].slope;
                if (dsSlope < slope * DEPO_SLOPE_THRESHOLD || hm[ds] < 0)
                {
                    double deposit = sedLoad[i] * DEPOSITION_RATE;
                    if (deposit > 0.01)
                    {
                        hm[ds] += (float)deposit;
                        totalDepo += deposit;
                        bool underwater = hm[ds] < 0;
                        strat.PushLayer(ds, new StratigraphicLayer
                        {
                            RockType = underwater ? RockType.SED_MUDSTONE : RockType.SED_SANDSTONE,
                            AgeDeposited = timeMa, Thickness = deposit,
                            DipAngle = 1 + rng.Next() * 3,
                            DipDirection = rng.NextFloat(0, 360),
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
        int cc = _gs * _gs;
        var flow = new (int downstream, double slope)[cc];
        for (int i = 0; i < cc; i++)
        {
            int row = i / _gs, col = i % _gs;
            double h = hm[i];
            int bestIdx = -1;
            double bestSlope = MIN_SLOPE;
            foreach (int n in BoundaryClassifier.GetNeighborIndices(row, col, _gs))
            {
                double dh = h - hm[n];
                if (dh > bestSlope) { bestSlope = dh; bestIdx = n; }
            }
            flow[i] = (bestIdx, bestSlope);
        }
        return flow;
    }

    private float[] ComputeDrainageArea((int downstream, double slope)[] flow)
    {
        int cc = flow.Length;
        var area = new float[cc];
        Array.Fill(area, 1f);
        var sorted = Enumerable.Range(0, cc).OrderByDescending(i => flow[i].slope).ToArray();
        foreach (int i in sorted)
            if (flow[i].downstream >= 0) area[flow[i].downstream] += area[i];
        return area;
    }
}

public sealed class ErosionResult
{
    public double TotalEroded { get; set; }
    public double TotalDeposited { get; set; }
    public int CellsAffected { get; set; }
    public List<int> RiverCells { get; set; } = new();
}
