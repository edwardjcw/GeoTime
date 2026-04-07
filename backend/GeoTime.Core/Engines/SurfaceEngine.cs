using GeoTime.Core.Compute;
using GeoTime.Core.Models;
using GeoTime.Core.Kernel;
using GeoTime.Core.Proc;

namespace GeoTime.Core.Engines;

/// <summary>Orchestrator for surface processes: erosion, glacial, weathering, pedogenesis.</summary>
public sealed class SurfaceEngine(EventBus bus, EventLog log, uint seed, int gridSize, double minTick = 0.5, GpuComputeService? gpu = null)
{
    private readonly ErosionEngine _erosion = new(gridSize);
    private readonly GlacialEngine _glacial = new(gridSize, gpu);
    private readonly WeatheringEngine _weathering = new(gridSize);
    private readonly PedogenesisEngine _pedogenesis = new(gridSize);
    private readonly Xoshiro256ss _rng = new(seed);

    private SimulationState? _state;
    private StratigraphyStack? _strat;
    private double _accumulator;
    private int _prevGlaciated;

    public void Initialize(SimulationState state, StratigraphyStack strat)
    {
        _state = state; _strat = strat; _glacial.Clear(); _prevGlaciated = 0;
    }

    public SurfaceTickResult? Tick(double timeMa, double deltaMa)
    {
        if (_state == null || _strat == null || deltaMa <= 0) return null;
        _accumulator += deltaMa;
        SurfaceTickResult? last = null;
        while (_accumulator >= minTick)
        {
            _accumulator -= minTick;
            last = Process(timeMa - _accumulator, minTick);
        }
        return last;
    }

    private SurfaceTickResult Process(double t, double dt)
    {
        var er = _erosion.Tick(t, dt, _state!, _strat!, _rng);
        var gl = _glacial.Tick(t, dt, _state!, _strat!, _rng);
        var we = _weathering.Tick(t, dt, _state!, _strat!, _rng);
        var pe = _pedogenesis.Tick(t, dt, _state!, _strat!);

        if (er.CellsAffected > 0)
            bus.Emit("EROSION_CYCLE", new { totalEroded = er.TotalEroded, totalDeposited = er.TotalDeposited, cellsAffected = er.CellsAffected });

        if (gl.GlaciatedCells > _prevGlaciated * 1.2 && gl.GlaciatedCells > 10)
        {
            bus.Emit("GLACIATION_ADVANCE", new { glaciatedCells = gl.GlaciatedCells, equilibriumLineAltitude = gl.EquilibriumLineAltitude });
            log.Record(new GeoLogEntry { TimeMa = t, Type = "ICE_AGE_ONSET", Description = $"Glaciation advancing: {gl.GlaciatedCells} cells" });
        }
        _prevGlaciated = gl.GlaciatedCells;

        return new SurfaceTickResult { Erosion = er, Glacial = gl, Weathering = we, Pedogenesis = pe };
    }

    public GlacialEngine GetGlacialEngine() => _glacial;
}

public sealed class SurfaceTickResult
{
    public ErosionResult Erosion { get; set; } = new();
    public GlacialResult Glacial { get; set; } = new();
    public WeatheringResult Weathering { get; set; } = new();
    public PedogenesisResult Pedogenesis { get; set; } = new();
}
