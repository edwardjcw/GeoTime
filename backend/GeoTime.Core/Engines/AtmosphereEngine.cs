using GeoTime.Core.Models;
using GeoTime.Core.Kernel;
using GeoTime.Core.Proc;

namespace GeoTime.Core.Engines;

/// <summary>Phase 4 orchestrator: climate + weather pipeline.</summary>
public sealed class AtmosphereEngine(EventBus bus, EventLog log, uint seed, int gridSize, double minTick = 1.0)
{
    private readonly ClimateEngine _climate = new(gridSize);
    private readonly WeatherEngine _weather = new(gridSize);
    private readonly Xoshiro256ss _rng = new(seed);

    private SimulationState? _state;
    private AtmosphericComposition? _atmo;
    private double _accumulator;
    private bool _prevIceAge;

    public void Initialize(SimulationState state, AtmosphericComposition atmo)
    {
        _state = state; _atmo = atmo; _accumulator = 0; _prevIceAge = false;
    }

    public AtmosphereTickResult? Tick(double timeMa, double deltaMa)
    {
        if (_state == null || _atmo == null || deltaMa <= 0) return null;
        _accumulator += deltaMa;
        AtmosphereTickResult? last = null;
        while (_accumulator >= minTick) { _accumulator -= minTick; last = Process(timeMa - _accumulator, minTick); }
        return last;
    }

    private AtmosphereTickResult Process(double t, double dt)
    {
        var cl = _climate.Tick(t, dt, _state!, _atmo!, _rng);
        var we = _weather.Tick(t, dt, _state!, _rng);

        bus.Emit("CLIMATE_UPDATE", new { meanTemperature = cl.MeanTemperature, co2Ppm = cl.CO2Ppm, iceAlbedoFeedback = cl.IceAlbedoFeedback });

        foreach (var c in we.TropicalCyclones)
        {
            bus.Emit("TROPICAL_CYCLONE_FORMED", new { lat = c.Lat, lon = c.Lon, intensity = c.Intensity });
            log.Record(new GeoLogEntry { TimeMa = t, Type = "TROPICAL_CYCLONE_FORMED", Description = $"Cat {c.Intensity} at {c.Lat:F1}°, {c.Lon:F1}°" });
        }

        if (cl.SnowballTriggered)
        {
            bus.Emit("SNOWBALL_EARTH", new { equatorialTemp = cl.EquatorialTemperature });
            log.Record(new GeoLogEntry { TimeMa = t, Type = "SNOWBALL_EARTH", Description = $"Equatorial temp {cl.EquatorialTemperature:F1}°C" });
        }

        var iceAge = cl.MeanTemperature < -5;
        if (iceAge && !_prevIceAge)
        {
            bus.Emit("ICE_AGE_ONSET", new { severity = cl.IceAlbedoFeedback });
            log.Record(new GeoLogEntry { TimeMa = t, Type = "ICE_AGE_ONSET", Description = $"Mean temp {cl.MeanTemperature:F1}°C" });
        }
        else if (!iceAge && _prevIceAge)
        {
            bus.Emit("ICE_AGE_END", new { });
            log.Record(new GeoLogEntry { TimeMa = t, Type = "ICE_AGE_END", Description = $"Mean temp recovered to {cl.MeanTemperature:F1}°C" });
        }
        _prevIceAge = iceAge;

        return new AtmosphereTickResult { Climate = cl, Weather = we };
    }
}

public sealed class AtmosphereTickResult
{
    public ClimateResult Climate { get; set; } = new();
    public WeatherResult Weather { get; set; } = new();
}
