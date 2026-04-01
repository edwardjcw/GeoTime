using GeoTime.Core.Models;
using GeoTime.Core.Kernel;
using GeoTime.Core.Proc;

namespace GeoTime.Core.Engines;

/// <summary>
/// Main tectonic engine: plate motion, boundary processes, isostasy, volcanism.
/// </summary>
public sealed class TectonicEngine(EventBus bus, EventLog eventLog, uint seed, double minTickInterval = 0.1)
{
    private const double ISOSTATIC_RATIO = 2.7 / 3.3;

    public StratigraphyStack Stratigraphy { get; } = new();
    private readonly BoundaryClassifier _classifier = new();
    private readonly VolcanismEngine _volcanism = new();
    private Xoshiro256ss _rng = new(seed);

    private List<PlateInfo> _plates = [];
    private List<HotspotInfo> _hotspots = [];
    private AtmosphericComposition _atmosphere = new() { N2 = 0.78, O2 = 0.21, CO2 = 0.0004, H2O = 0.01 };
    private SimulationState? _state;

    private double _accumulator;

    public void Initialize(List<PlateInfo> plates, List<HotspotInfo> hotspots,
        AtmosphericComposition atmosphere, SimulationState state)
    {
        _plates = plates;
        _hotspots = hotspots;
        _atmosphere = atmosphere;
        _state = state;
        var cellCount = state.CellCount;
        for (var i = 0; i < cellCount; i++)
        {
            var plate = plates[state.PlateMap[i]];
            Stratigraphy.InitializeBasement(i, plate.IsOceanic, state.RockAgeMap[i]);
        }
    }

    public List<EruptionRecord> Tick(double timeMa, double deltaMa)
    {
        if (_state == null || deltaMa <= 0) return [];
        _accumulator += deltaMa;
        var all = new List<EruptionRecord>();
        while (_accumulator >= minTickInterval)
        {
            _accumulator -= minTickInterval;
            var subTime = timeMa - _accumulator;
            all.AddRange(ProcessTick(subTime, minTickInterval));
        }
        return all;
    }

    private List<EruptionRecord> ProcessTick(double timeMa, double deltaMa)
    {
        var state = _state!;
        var gs = state.GridSize;

        var boundaries = BoundaryClassifier.Classify(state.PlateMap, _plates, gs);
        ProcessConvergent(boundaries, deltaMa, timeMa, state);
        ProcessDivergent(boundaries, deltaMa, timeMa, state);
        ApplyIsostasy(state, deltaMa);

        var eruptions = VolcanismEngine.Tick(timeMa, deltaMa, boundaries, _hotspots, _plates, state, Stratigraphy, _rng);

        foreach (var e in eruptions.Where(e => e.Intensity > 0.5))
        {
            bus.Emit("VOLCANIC_ERUPTION", new { lat = e.Lat, lon = e.Lon, intensity = e.Intensity });
            eventLog.Record(new GeoLogEntry
            {
                TimeMa = timeMa, Type = "VOLCANIC_ERUPTION",
                Description = $"Eruption at ({e.Lat:F1}°, {e.Lon:F1}°), intensity {e.Intensity:F2}",
                Location = new LatLon(e.Lat, e.Lon),
            });
        }

        var (co2, _) = VolcanismEngine.TotalDegassing(eruptions);
        _atmosphere.CO2 += co2 * 1e-6;
        return eruptions;
    }

    private void ProcessConvergent(List<BoundaryCell> boundaries, double deltaMa, double timeMa, SimulationState state)
    {
        foreach (var b in boundaries.Where(b => b.Type == BoundaryType.CONVERGENT))
        {
            var p1 = _plates[b.Plate1];
            var p2 = _plates[b.Plate2];
            if (!p1.IsOceanic && !p2.IsOceanic)
                ApplyCollision(b, deltaMa, timeMa, state);
            else
                ApplySubduction(b.CellIndex, deltaMa, state);
        }
    }

    private static void ApplySubduction(int ci, double deltaMa, SimulationState state)
    {
        state.HeightMap[ci] += (float)(-50 * deltaMa);
        state.CrustThicknessMap[ci] = Math.Max(3, state.CrustThicknessMap[ci] - (float)(0.5 * deltaMa));
    }

    private void ApplyCollision(BoundaryCell b, double deltaMa, double timeMa, SimulationState state)
    {
        var ci = b.CellIndex;
        state.CrustThicknessMap[ci] += (float)(2 * deltaMa * b.RelativeSpeed);
        state.HeightMap[ci] += (float)(100 * deltaMa * b.RelativeSpeed);
        Stratigraphy.ApplyDeformation(ci, 2 * deltaMa, 0, DeformationType.FOLDED);

        if (!(b.RelativeSpeed > 2.0)) return;
        bus.Emit("PLATE_COLLISION", new { plate1 = b.Plate1, plate2 = b.Plate2 });
        eventLog.Record(new GeoLogEntry
        {
            TimeMa = timeMa, Type = "PLATE_COLLISION",
            Description = $"Collision between plates {b.Plate1} and {b.Plate2}",
        });
    }

    private static void ProcessDivergent(List<BoundaryCell> boundaries, double deltaMa, double timeMa, SimulationState state)
    {
        foreach (var b in boundaries.Where(b => b.Type == BoundaryType.DIVERGENT))
        {
            var thinning = 0.3 * deltaMa * b.RelativeSpeed;
            state.CrustThicknessMap[b.CellIndex] = Math.Max(3, state.CrustThicknessMap[b.CellIndex] - (float)thinning);
            if (!(state.CrustThicknessMap[b.CellIndex] < 10)) continue;
            state.RockTypeMap[b.CellIndex] = (byte)RockType.IGN_BASALT;
            state.RockAgeMap[b.CellIndex] = (float)timeMa;
        }
    }

    private static void ApplyIsostasy(SimulationState state, double deltaMa)
    {
        var cc = state.CellCount;
        var relax = Math.Min(1, 0.1 * deltaMa);
        for (var i = 0; i < cc; i++)
        {
            var eq = state.CrustThicknessMap[i] * 1000 * (1 - ISOSTATIC_RATIO) - 4500;
            state.HeightMap[i] += (float)((eq - state.HeightMap[i]) * relax);
        }
    }

    public IReadOnlyList<PlateInfo> GetPlates() => _plates;
    public IReadOnlyList<HotspotInfo> GetHotspots() => _hotspots;
    public AtmosphericComposition GetAtmosphere() => _atmosphere;
}
