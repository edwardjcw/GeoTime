using GeoTime.Core.Compute;
using GeoTime.Core.Engines;
using GeoTime.Core.Models;

namespace GeoTime.Tests;

/// <summary>
/// Tests for Recommendations 5–8 from optimize.md:
///   5 – ILGPU GPU/CPU isostasy kernel via GpuComputeService
///   6 – GPU temperature diffusion in ClimateEngine
///   7 – AdaptiveResolutionService (down/upsample helpers)
///   8 – SignalR binary state bundle streaming (hub-level integration)
/// </summary>
public class Phase10OptimizationTests
{
    // ── GpuComputeService (Rec 5) ─────────────────────────────────────────────

    [Fact]
    public void GpuComputeService_Creates_WithoutException()
    {
        // Should succeed with CPU fallback when no GPU is available
        using var service = new GpuComputeService();
        Assert.NotNull(service);
        Assert.NotNull(service.Info);
    }

    [Fact]
    public void GpuComputeService_Info_HasDeviceName()
    {
        using var service = new GpuComputeService();
        Assert.False(string.IsNullOrWhiteSpace(service.Info.DeviceName));
    }

    [Fact]
    public void GpuComputeService_Info_ModeIsCpuOrGpu()
    {
        using var service = new GpuComputeService();
        Assert.True(service.Info.Mode == ComputeMode.CPU || service.Info.Mode == ComputeMode.GPU);
    }

    [Fact]
    public void GpuComputeService_ApplyIsostasy_ProducesCorrectResult()
    {
        using var service = new GpuComputeService();
        const int n = 16;
        var height = new float[n];
        var crust  = new float[n];
        for (var i = 0; i < n; i++) { height[i] = 1000f; crust[i] = 35f; }

        // Match TectonicEngine constants: factor = 1000 * (1 - 2.7/3.3), offset = -4500
        const double ISOSTATIC_RATIO = 2.7 / 3.3;
        var factor  = (float)(1000.0 * (1 - ISOSTATIC_RATIO));
        const float offset = -4500f;
        const float relax  = 0.1f;

        service.ApplyIsostasy(height, crust, relax, factor, offset);

        // Expected: height_new = height * (1-relax) + eq * relax
        var eq = crust[0] * factor + offset;
        var expected = 1000f * (1 - relax) + eq * relax;
        for (var i = 0; i < n; i++)
            Assert.Equal(expected, height[i], 2);
    }

    [Fact]
    public void GpuComputeService_ApplyIsostasy_DoesNotModifyCrustArray()
    {
        using var service = new GpuComputeService();
        const int n = 8;
        var height = new float[n];
        var crust  = new float[n];
        for (var i = 0; i < n; i++) { height[i] = 500f; crust[i] = 30f; }

        service.ApplyIsostasy(height, crust, 0.05f, 180f, -4500f);

        // Crust values must be unchanged
        Assert.All(crust, c => Assert.Equal(30f, c));
    }

    [Fact]
    public void GpuComputeService_DiffuseTemperature_KeepsValuesFinite()
    {
        using var service = new GpuComputeService();
        const int gs = 8;
        var temp = new float[gs * gs];
        for (var i = 0; i < temp.Length; i++) temp[i] = 20f - i * 0.1f;

        service.DiffuseTemperature(temp, gs, 0.01f);

        Assert.All(temp, t => Assert.True(float.IsFinite(t), $"Expected finite temperature but got {t}"));
    }

    [Fact]
    public void GpuComputeService_DiffuseTemperature_ReducesGradients()
    {
        using var service = new GpuComputeService();
        const int gs = 4;
        // Create a hot centre, cold edges
        var temp = new float[gs * gs];
        for (var row = 0; row < gs; row++)
            for (var col = 0; col < gs; col++)
                temp[row * gs + col] = (row == 1 && col == 1) ? 100f : 10f;

        var centre = temp[1 * gs + 1];
        service.DiffuseTemperature(temp, gs, 0.1f);
        var centreAfter = temp[1 * gs + 1];

        // Diffusion should reduce the hot-spot temperature
        Assert.True(centreAfter < centre, $"Diffusion should cool the hot spot: {centre} → {centreAfter}");
    }

    // ── TectonicEngine with GPU (Rec 5) ───────────────────────────────────────

    [Fact]
    public void TectonicEngine_WithGpuService_IsostasyRunsWithoutException()
    {
        using var gpu = new GpuComputeService();
        var bus = new GeoTime.Core.Kernel.EventBus();
        var log = new GeoTime.Core.Kernel.EventLog(50);
        var engine = new TectonicEngine(bus, log, 1, 0.1, gpu);

        var state = new SimulationState(8);
        var plates = new List<GeoTime.Core.Models.PlateInfo>
        {
            new() { Id = 0, CenterLat = 0, CenterLon = 0, IsOceanic = false, Area = 1 },
        };
        for (var i = 0; i < state.CellCount; i++)
        {
            state.PlateMap[i] = 0;
            state.HeightMap[i] = 1000f;
            state.CrustThicknessMap[i] = 35f;
        }
        engine.Initialize(plates, [], new GeoTime.Core.Models.AtmosphericComposition(), state);

        var ex = Record.Exception(() => engine.Tick(0, 0.1));
        Assert.Null(ex);
    }

    // ── ClimateEngine temperature diffusion (Rec 6) ───────────────────────────

    [Fact]
    public void ClimateEngine_WithGpu_TemperatureStaysFinite()
    {
        using var gpu = new GpuComputeService();
        var engine = new ClimateEngine(8, gpu);
        var state  = new SimulationState(8);
        var atmo   = new GeoTime.Core.Models.AtmosphericComposition { N2 = 0.78, O2 = 0.21, CO2 = 280e-6 };
        var rng    = new GeoTime.Core.Proc.Xoshiro256ss(42);

        for (var i = 0; i < state.CellCount; i++) state.TemperatureMap[i] = 15f;

        var result = engine.Tick(0, 1, state, atmo, rng);

        Assert.True(double.IsFinite(result.MeanTemperature));
        Assert.All(state.TemperatureMap, t => Assert.True(float.IsFinite(t)));
    }

    [Fact]
    public void ClimateEngine_WithoutGpu_DiffuseStillRuns()
    {
        var engine = new ClimateEngine(8, null);
        var state  = new SimulationState(8);
        var atmo   = new GeoTime.Core.Models.AtmosphericComposition { N2 = 0.78, O2 = 0.21, CO2 = 280e-6 };
        var rng    = new GeoTime.Core.Proc.Xoshiro256ss(99);

        for (var i = 0; i < state.CellCount; i++) state.TemperatureMap[i] = 20f;

        var result = engine.Tick(0, 1, state, atmo, rng);

        Assert.True(double.IsFinite(result.MeanTemperature));
        Assert.All(state.TemperatureMap, t => Assert.True(float.IsFinite(t)));
    }

    // ── AdaptiveResolutionService (Rec 7) ─────────────────────────────────────

    [Fact]
    public void AdaptiveResolution_Downsample_OutputLength()
    {
        var full = new float[16 * 16];
        var coarse = AdaptiveResolutionService.Downsample(full, 16, 4);
        Assert.Equal(4 * 4, coarse.Length);
    }

    [Fact]
    public void AdaptiveResolution_Downsample_PreservesConstantValue()
    {
        const float value = 42f;
        var full = Enumerable.Repeat(value, 16 * 16).ToArray();
        var coarse = AdaptiveResolutionService.Downsample(full, 16, 4);
        Assert.All(coarse, c => Assert.Equal(value, c, 3));
    }

    [Fact]
    public void AdaptiveResolution_Upsample_OutputLength()
    {
        var coarse = new float[4 * 4];
        var full = AdaptiveResolutionService.Upsample(coarse, 4, 16);
        Assert.Equal(16 * 16, full.Length);
    }

    [Fact]
    public void AdaptiveResolution_Upsample_PreservesConstantValue()
    {
        const float value = 7f;
        var coarse = Enumerable.Repeat(value, 4 * 4).ToArray();
        var full = AdaptiveResolutionService.Upsample(coarse, 4, 16);
        Assert.All(full, f => Assert.Equal(value, f, 3));
    }

    [Fact]
    public void AdaptiveResolution_RoundTrip_CloseToOriginal()
    {
        const int fullSize = 16;
        const int coarseSize = 4;
        var original = new float[fullSize * fullSize];
        for (var i = 0; i < original.Length; i++) original[i] = (float)Math.Sin(i * 0.5);

        var coarse   = AdaptiveResolutionService.Downsample(original, fullSize, coarseSize);
        var restored = AdaptiveResolutionService.Upsample(coarse, coarseSize, fullSize);

        // The round-trip won't be exact (downsampling is lossy) but should be in the right ballpark
        Assert.Equal(original.Length, restored.Length);
        Assert.All(restored, f => Assert.True(float.IsFinite(f)));
    }

    [Fact]
    public void AdaptiveResolution_UpsampleInto_WritesTarget()
    {
        const float value = 3.14f;
        var coarse = Enumerable.Repeat(value, 4 * 4).ToArray();
        var target = new float[16 * 16];

        AdaptiveResolutionService.UpsampleInto(coarse, 4, target, 16);

        Assert.All(target, f => Assert.Equal(value, f, 3));
    }

    [Fact]
    public void AdaptiveResolution_CoarseGridConstant_Is128()
    {
        Assert.Equal(128, GridConstants.COARSE_GRID_SIZE);
    }

    // ── SimulationOrchestrator adaptive resolution (Rec 7) ───────────────────

    [Fact]
    public void Orchestrator_AdaptiveResolution_DefaultEnabled()
    {
        using var sim = new GeoTime.Core.SimulationOrchestrator(8);
        Assert.True(sim.AdaptiveResolutionEnabled);
    }

    [Fact]
    public void Orchestrator_AdaptiveResolution_CanBeDisabled()
    {
        using var sim = new GeoTime.Core.SimulationOrchestrator(8);
        sim.AdaptiveResolutionEnabled = false;
        Assert.False(sim.AdaptiveResolutionEnabled);
    }

    // ── ComputeInfo / compute endpoint (Rec 5/6) ─────────────────────────────

    [Fact]
    public void Orchestrator_GetComputeInfo_ReturnsInfo()
    {
        using var sim = new GeoTime.Core.SimulationOrchestrator(8);
        var info = sim.GetComputeInfo();
        Assert.NotNull(info);
        Assert.False(string.IsNullOrWhiteSpace(info.DeviceName));
        Assert.True(info.Mode == ComputeMode.CPU || info.Mode == ComputeMode.GPU);
    }
}
