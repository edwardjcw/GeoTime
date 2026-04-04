using GeoTime.Core.Models;
using GeoTime.Core.Kernel;
using GeoTime.Core.Proc;

namespace GeoTime.Core.Engines;

/// <summary>Vegetation: Miami Model NPP, biomass, forest fires, albedo feedback.</summary>
public sealed class VegetationEngine(
    EventBus bus,
    EventLog log,
    uint seed,
    int gridSize,
    double minTick = 1.0,
    bool enabled = true)
{
    public const double MAX_BIOMASS = 40;
    public const double MIN_GRASS_PRECIP = 250;
    public const double MIN_GRASS_SOIL = 0.1;
    public const double BASE_FIRE_PROB = 0.05;
    public const double DRY_PRECIP = 400;
    public const double FIRE_BIOMASS_THRESH = 10;
    public const double FIRE_BURN_FRAC = 0.6;

    private readonly Xoshiro256ss _rng = new(seed);
    private SimulationState? _state;
    private double _accumulator;
    public bool Enabled { get; } = enabled;

    public void Initialize(SimulationState state) { _state = state; _accumulator = 0; }

    public VegetationTickResult? Tick(double timeMa, double deltaMa)
    {
        if (!Enabled || _state == null || deltaMa <= 0) return null;
        _accumulator += deltaMa;
        VegetationTickResult? last = null;
        while (_accumulator >= minTick) { _accumulator -= minTick; last = Process(timeMa - _accumulator, minTick); }
        return last;
    }

    public static double ComputeNPP(double tempC, double precipMm)
    {
        if (precipMm <= 0) return 0;
        var nppT = 3000 / (1 + Math.Exp(1.315 - 0.119 * tempC));
        var nppP = 3000 * (1 - Math.Exp(-0.000664 * precipMm));
        return Math.Max(0, Math.Min(nppT, nppP));
    }

    public static double NppToBiomassRate(double npp) => npp / 1000 * 0.5;

    public static double ComputeFireProbability(double precip, double biomass)
    {
        if (biomass < FIRE_BIOMASS_THRESH) return 0;
        var prob = BASE_FIRE_PROB;
        if (precip < DRY_PRECIP) prob *= 1 + (DRY_PRECIP - precip) / DRY_PRECIP;
        prob *= 0.5 + 0.5 * Math.Min(biomass / MAX_BIOMASS, 1);
        return Math.Min(prob, 1);
    }

    private VegetationTickResult Process(double timeMa, double deltaMa)
    {
        var sv = _state!;
        var cc = gridSize * gridSize;

        // Pre-generate per-cell fire random rolls sequentially to maintain
        // determinism while allowing the main loop to run in parallel.
        var fireRolls = new double[cc];
        for (var i = 0; i < cc; i++)
            fireRolls[i] = _rng.Next();

        var lockObj = new object();
        double totalBio = 0, totalNpp = 0;
        int vegCells = 0, fires = 0;

        Parallel.For(0, cc,
            () => (bio: 0.0, npp: 0.0, veg: 0, fire: 0),
            (i, _, local) =>
            {
                // Fast path: skip cells that are not dirty and have no biomass to maintain.
                if (!sv.DirtyMask[i] && sv.BiomassMap[i] < 1e-4f)
                    return local;

                double h = sv.HeightMap[i], temp = sv.TemperatureMap[i];
                double precip = sv.PrecipitationMap[i], soilD = sv.SoilDepthMap[i];
                double biomass = sv.BiomassMap[i];

                if (h <= 0 || temp < -10 || precip < 50) { sv.BiomassMap[i] = 0; return local; }

                var npp = ComputeNPP(temp, precip);
                var canGrow = precip >= MIN_GRASS_PRECIP && soilD >= MIN_GRASS_SOIL;
                if (canGrow && npp > 0)
                {
                    biomass = Math.Min(MAX_BIOMASS, biomass + NppToBiomassRate(npp) * deltaMa);
                    local.npp += npp; local.veg++;
                }

                var fp = ComputeFireProbability(precip, biomass);
                if (fp > 0 && fireRolls[i] < fp * deltaMa)
                {
                    var burned = biomass * FIRE_BURN_FRAC;
                    biomass -= burned; local.fire++;
                    // Note: bus.Emit is deferred to avoid concurrent access.
                }

                sv.BiomassMap[i] = (float)biomass;
                local.bio += biomass;
                return local;
            },
            local =>
            {
                lock (lockObj)
                {
                    totalBio += local.bio;
                    totalNpp += local.npp;
                    vegCells += local.veg;
                    fires += local.fire;
                }
            });

        // Emit fire events after the parallel loop (bus is not thread-safe).
        if (fires > 0)
        {
            bus.Emit("FOREST_FIRE_BATCH", new { fireCount = fires });
            log.Record(new GeoLogEntry { TimeMa = timeMa, Type = "FOREST_FIRE", Description = $"{fires} forest fire(s)" });
        }

        var meanNpp = vegCells > 0 ? totalNpp / vegCells : 0;
        bus.Emit("VEGETATION_UPDATE", new { totalBiomass = totalBio, meanNpp, cellsWithVegetation = vegCells });

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
