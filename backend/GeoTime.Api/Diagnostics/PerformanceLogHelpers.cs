using GeoTime.Core;
using GeoTime.Core.Compute;

namespace GeoTime.Api.Diagnostics;

internal static class PerformanceLogHelpers
{
    public static object ComputePayload(SimulationOrchestrator sim)
    {
        var info = sim.GetComputeInfo();
        return new
        {
            mode = info.Mode.ToString(),
            deviceName = info.DeviceName,
            acceleratorType = info.AcceleratorType,
            isGpu = info.Mode == ComputeMode.GPU,
            memoryMb = info.MemoryMb,
        };
    }

    public static object TickStatsPayload(SimulationTickStats stats) => new
    {
        deltaMa = stats.DeltaMa,
        timeMa = stats.TimeMa,
        gridSize = stats.GridSize,
        adaptiveResolutionUsed = stats.AdaptiveResolutionUsed,
        adaptiveDownsampleMs = stats.AdaptiveDownsampleMs,
        adaptiveUpsampleMs = stats.AdaptiveUpsampleMs,
        tectonicMs = stats.TectonicMs,
        tectonicTotalMs = stats.TectonicTotalMs,
        tectonicAdvectionMs = stats.TectonicAdvectionMs,
        tectonicCollisionMs = stats.TectonicCollisionMs,
        tectonicBoundaryMs = stats.TectonicBoundaryMs,
        tectonicDynamicsMs = stats.TectonicDynamicsMs,
        tectonicVolcanismMs = stats.TectonicVolcanismMs,
        surfaceMs = stats.SurfaceMs,
        atmosphereMs = stats.AtmosphereMs,
        vegetationMs = stats.VegetationMs,
        biomatterMs = stats.BiomatterMs,
        featureDetectionRan = stats.FeatureDetectionRan,
        featureDetectionMs = stats.FeatureDetectionMs,
        eventDepositionMs = stats.EventDepositionMs,
        eventsThisTick = stats.EventsThisTick,
        totalMs = stats.TotalMs,
        isGpuActive = stats.IsGpuActive,
    };
}
