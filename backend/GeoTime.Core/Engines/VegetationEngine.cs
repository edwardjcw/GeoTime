using GeoTime.Core.Models;
using GeoTime.Core.Kernel;
using GeoTime.Core.Proc;

namespace GeoTime.Core.Engines;

/// <summary>Vegetation: Miami Model NPP, biomass, forest fires, albedo feedback.</summary>
public sealed class VegetationEngine
{
    public const double MAX_BIOMASS = 40;
    public const double MIN_GRASS_PRECIP = 250;
    public const double MIN_GRASS_SOIL = 0.1;
    public const double BASE_FIRE_PROB = 0.05;
    public const double DRY_PRECIP = 400;
    public const double FIRE_BIOMASS_THRESH = 10;
    public const double FIRE_BURN_FRAC = 0.6;

    private readonly EventBus _bus;
    private readonly EventLog _eventLog;
    private readonly Xoshiro256ss _rng;
    private readonly int _gs;
    private SimulationState? _state;
    private double _accumulator;
    private readonly double _minTick;
    public bool Enabled { get; }

    public VegetationEngine(EventBus bus, EventLog log, uint seed, int gridSize, double minTick = 1.0, bool enabled = true)
    {
        _bus = bus; _eventLog = log; _rng = new Xoshiro256ss(seed);
        _gs = gridSize; _minTick = minTick; Enabled = enabled;
    }

    public void Initialize(SimulationState state) { _state = state; _accumulator = 0; }

    public VegetationTickResult? Tick(double timeMa, double deltaMa)
    {
        if (!Enabled || _state == null || deltaMa <= 0) return null;
        _accumulator += deltaMa;
        VegetationTickResult? last = null;
        while (_accumulator >= _minTick) { _accumulator -= _minTick; last = Process(timeMa - _accumulator, _minTick); }
        return last;
    }

    public static double ComputeNPP(double tempC, double precipMm)
    {
        if (precipMm <= 0) return 0;
        double nppT = 3000 / (1 + Math.Exp(1.315 - 0.119 * tempC));
        double nppP = 3000 * (1 - Math.Exp(-0.000664 * precipMm));
        return Math.Max(0, Math.Min(nppT, nppP));
    }

    public static double NppToBiomassRate(double npp) => npp / 1000 * 0.5;

    public static double ComputeFireProbability(double precip, double biomass)
    {
        if (biomass < FIRE_BIOMASS_THRESH) return 0;
        double prob = BASE_FIRE_PROB;
        if (precip < DRY_PRECIP) prob *= 1 + (DRY_PRECIP - precip) / DRY_PRECIP;
        prob *= 0.5 + 0.5 * Math.Min(biomass / MAX_BIOMASS, 1);
        return Math.Min(prob, 1);
    }

    private VegetationTickResult Process(double timeMa, double deltaMa)
    {
        var sv = _state!;
        int cc = _gs * _gs;
        double totalBio = 0, totalNpp = 0;
        int vegCells = 0, fires = 0;

        for (int i = 0; i < cc; i++)
        {
            double h = sv.HeightMap[i], temp = sv.TemperatureMap[i];
            double precip = sv.PrecipitationMap[i], soilD = sv.SoilDepthMap[i];
            double biomass = sv.BiomassMap[i];

            if (h <= 0) { sv.BiomassMap[i] = 0; continue; }
            if (temp < -10 || precip < 50) { sv.BiomassMap[i] = 0; continue; }

            double npp = ComputeNPP(temp, precip);
            bool canGrow = precip >= MIN_GRASS_PRECIP && soilD >= MIN_GRASS_SOIL;
            if (canGrow && npp > 0)
            {
                biomass = Math.Min(MAX_BIOMASS, biomass + NppToBiomassRate(npp) * deltaMa);
                totalNpp += npp; vegCells++;
            }

            double fp = ComputeFireProbability(precip, biomass);
            if (fp > 0 && _rng.Next() < fp * deltaMa)
            {
                double burned = biomass * FIRE_BURN_FRAC;
                biomass -= burned; fires++;
                _bus.Emit("FOREST_FIRE", new { cellIndex = i, biomassBurned = burned });
            }

            sv.BiomassMap[i] = (float)biomass;
            totalBio += biomass;
        }

        double meanNpp = vegCells > 0 ? totalNpp / vegCells : 0;
        _bus.Emit("VEGETATION_UPDATE", new { totalBiomass = totalBio, meanNpp, cellsWithVegetation = vegCells });
        if (fires > 0)
            _eventLog.Record(new GeoLogEntry { TimeMa = timeMa, Type = "FOREST_FIRE", Description = $"{fires} forest fire(s)" });

        return new VegetationTickResult { TotalBiomass = totalBio, MeanNpp = meanNpp, CellsWithVegetation = vegCells, FireCount = fires };
    }
}

public sealed class VegetationTickResult
{
    public double TotalBiomass { get; set; }
    public double MeanNpp { get; set; }
    public int CellsWithVegetation { get; set; }
    public int FireCount { get; set; }
}
