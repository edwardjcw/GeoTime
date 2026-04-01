namespace GeoTime.Core.Kernel;

/// <summary>
/// Simulation clock managing geological time (Ma).
/// Starts at -4500 Ma (Earth's formation).
/// </summary>
public sealed class SimClock(EventBus bus, double maxFrameBudget = double.PositiveInfinity)
{
    private const double MIN_RATE = 0.001;
    private const double MAX_RATE = 100;
    public const double INITIAL_TIME = -4500;

    public double T { get; set; } = INITIAL_TIME;
    public double Rate { get; set; } = 1;
    public bool Paused { get; set; }
    public double LastDtReal { get; private set; }

    public void Advance(double dtReal)
    {
        if (Paused) return;
        var capped = Math.Min(dtReal, maxFrameBudget);
        LastDtReal = capped;
        var dtMa = capped * Rate;
        T += dtMa;
        bus.Emit("TICK", new { timeMa = T, deltaMa = dtMa });
    }

    public void SeekTo(double t) => T = t;
    public void Pause() => Paused = true;
    public void Resume() => Paused = false;
    public void TogglePause() => Paused = !Paused;
    public void SetRate(double rate) => Rate = Math.Clamp(rate, MIN_RATE, MAX_RATE);
}
