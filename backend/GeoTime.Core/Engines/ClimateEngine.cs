using GeoTime.Core.Compute;
using GeoTime.Core.Models;
using GeoTime.Core.Proc;

namespace GeoTime.Core.Engines;

/// <summary>General circulation model + ice-age forcing.</summary>
public sealed class ClimateEngine(int gridSize, GpuComputeService? gpu = null)
{
    private const double S0 = 1361;
    private const double LAMBDA = 3.0;
    private const double CO2_REF = 280;
    private const double LAPSE_RATE = 6.5;
    private const double ALBEDO_OCEAN = 0.06;
    private const double ALBEDO_LAND = 0.30;
    private const double ALBEDO_ICE = 0.85;
    private const double SNOWBALL_THRESHOLD = -10;

    /// <summary>
    /// Diffusion coefficient: fraction of the Laplacian spread per climate tick.
    /// Small value (0.02) keeps the diffusion numerically stable on a 512×512 grid.
    /// </summary>
    private const float DiffusionAlpha = 0.02f;

    public ClimateResult Tick(double timeMa, double deltaMa, SimulationState state,
        AtmosphericComposition atmo, Xoshiro256ss _rng)
    {
        var cc = gridSize * gridSize;
        var co2Ppm = atmo.CO2 * 1_000_000;
        var dT_ghg = co2Ppm > 0 ? LAMBDA * Math.Log(co2Ppm / CO2_REF) / Math.Log(2) : 0;
        var dT_milan = 2 * Math.Sin(timeMa * 2 * Math.PI / 100);
        var alpha = Math.Min(1, deltaMa * 0.5);

        // ── GPU path: offload temperature update to the accelerator ──────────
        if (gpu != null)
        {
            gpu.UpdateTemperature(state.TemperatureMap, state.HeightMap, gridSize,
                (float)alpha, (float)dT_ghg, (float)dT_milan);
        }
        else
        {
            // CPU fallback: Parallel temperature update
            Parallel.For(0, gridSize, row =>
            {
                var latDeg = 90.0 - (double)row / (gridSize - 1) * 180;
                var latRad = latDeg * Math.PI / 180;
                for (var col = 0; col < gridSize; col++)
                {
                    var i = row * gridSize + col;
                    double h = state.HeightMap[i];
                    var T_base = 30 * Math.Cos(latRad);
                    var hKm = Math.Max(0, h / 1000);
                    var T_final = T_base - hKm * LAPSE_RATE + dT_ghg + dT_milan;
                    state.TemperatureMap[i] = (float)(state.TemperatureMap[i] * (1 - alpha) + T_final * alpha);
                }
            });
        }

        // Aggregates stay on CPU (reporting only)
        double tempSum = 0, eqTempSum = 0;
        int eqCount = 0, iceCells = 0;
        for (var i = 0; i < cc; i++)
        {
            tempSum += state.TemperatureMap[i];
            if (state.TemperatureMap[i] < -5) iceCells++;
            var row = i / gridSize;
            var latDeg = 90.0 - (double)row / (gridSize - 1) * 180;
            if (Math.Abs(latDeg) < 10) { eqTempSum += state.TemperatureMap[i]; eqCount++; }
        }

        // ── Strategy B.3: GPU temperature diffusion ──────────────────────────
        // Smooth heat across neighbours once per tick using a 5-point Laplacian.
        // GPU path: ILGPU kernel; CPU path: Parallel.For equivalent.
        DiffuseTemperature(state.TemperatureMap);

        // ── GPU path: offload wind computation to the accelerator ────────────
        if (gpu != null)
        {
            gpu.ComputeWinds(state.WindUMap, state.WindVMap, gridSize);
        }
        else
        {
            // CPU fallback: Parallel 3-cell circulation winds (each cell is independent).
            Parallel.For(0, gridSize, row =>
            {
                var latDeg = 90.0 - (double)row / (gridSize - 1) * 180;
                var absLat = Math.Abs(latDeg);
                double sign = latDeg >= 0 ? 1 : -1;
                for (var col = 0; col < gridSize; col++)
                {
                    var i = row * gridSize + col;
                    double u, v;
                    switch (absLat)
                    {
                        case <= 30:
                            u = -Math.Cos(absLat * Math.PI / 30); v = sign * -0.3 * Math.Sin(absLat * Math.PI / 30);
                            break;
                        case <= 60:
                            u = Math.Cos((absLat - 45) * Math.PI / 30); v = sign * 0.1 * Math.Cos((absLat - 45) * Math.PI / 30);
                            break;
                        default:
                            u = -Math.Cos((absLat - 75) * Math.PI / 15); v = sign * -0.2 * Math.Sin((absLat - 75) * Math.PI / 15);
                            break;
                    }
                    state.WindUMap[i] = (float)u;
                    state.WindVMap[i] = (float)v;
                }
            });
        }

        var meanT = tempSum / cc;
        var eqT = eqCount > 0 ? eqTempSum / eqCount : meanT;

        return new ClimateResult
        {
            MeanTemperature = meanT, EquatorialTemperature = eqT,
            CO2Ppm = co2Ppm, IceAlbedoFeedback = (double)iceCells / cc,
            SnowballTriggered = eqT < SNOWBALL_THRESHOLD, IceCells = iceCells,
        };
    }

    /// <summary>
    /// Apply one pass of 5-point Laplacian heat diffusion to smooth temperature gradients.
    /// Uses ILGPU kernel when a <see cref="GpuComputeService"/> is injected; otherwise
    /// falls back to a thread-safe Parallel.For copy-then-write approach.
    /// </summary>
    private void DiffuseTemperature(float[] temp)
    {
        if (gpu != null)
        {
            gpu.DiffuseTemperature(temp, gridSize, DiffusionAlpha);
            return;
        }

        // CPU fallback: read-only pass into scratch buffer, write back
        var scratch = new float[temp.Length];
        var gs = gridSize;
        Parallel.For(0, gs, row =>
        {
            for (var col = 0; col < gs; col++)
            {
                var i     = row * gs + col;
                var up    = ((row - 1 + gs) % gs) * gs + col;
                var down  = ((row + 1) % gs)       * gs + col;
                var left  = row * gs + (col - 1 + gs) % gs;
                var right = row * gs + (col + 1) % gs;
                var lap   = temp[up] + temp[down] + temp[left] + temp[right] - 4f * temp[i];
                scratch[i] = temp[i] + DiffusionAlpha * lap;
            }
        });
        Array.Copy(scratch, temp, temp.Length);
    }
}

public sealed class ClimateResult
{
    public double MeanTemperature { get; set; }
    public double EquatorialTemperature { get; set; }
    public double CO2Ppm { get; set; }
    public double IceAlbedoFeedback { get; set; }
    public bool SnowballTriggered { get; set; }
    public int IceCells { get; set; }
}
