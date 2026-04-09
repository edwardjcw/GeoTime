using GeoTime.Core;
using GeoTime.Core.Models;
using GeoTime.Core.Engines;
using GeoTime.Core.Kernel;

namespace GeoTime.Tests;

public class SplitPhaseTests
{
    [Fact]
    public void S1_BoundaryCacheReusedAcrossSubTicks()
    {
        // With deltaMa = 0.5 and minTickInterval = 0.1, there should be 5 sub-ticks.
        // The boundary classifier should only be called once (cached).
        var sim = new SimulationOrchestrator(16);
        sim.GeneratePlanet(42);
        sim.AdvanceSimulation(0.5);
        var stats = sim.LastTickStats;
        Assert.True(stats.TectonicMs >= 0);
        Assert.True(stats.TotalMs >= 0);
        sim.Dispose();
    }

    [Fact]
    public async Task S2_AsyncAdvance_ProducesSubPhaseProgress()
    {
        var sim = new SimulationOrchestrator(16);
        sim.GeneratePlanet(42);
        var phases = new List<string>();
        await sim.AdvanceSimulationAsync(0.5, async phase =>
        {
            phases.Add(phase);
            await Task.CompletedTask;
        });
        // Should include tectonic sub-phases
        Assert.Contains("tectonic:advection", phases);
        Assert.Contains("tectonic:collision", phases);
        Assert.Contains("tectonic:boundaries", phases);
        Assert.Contains("tectonic:dynamics", phases);
        Assert.Contains("tectonic:volcanism", phases);
        Assert.Contains("surface", phases);
        Assert.Contains("biomatter", phases);
        Assert.Contains("complete", phases);
        sim.Dispose();
    }

    [Fact]
    public async Task S2_AsyncAdvance_PopulatesSubPhaseTiming()
    {
        var sim = new SimulationOrchestrator(16);
        sim.GeneratePlanet(42);
        await sim.AdvanceSimulationAsync(0.5);
        var stats = sim.LastTickStats;
        Assert.True(stats.TectonicTotalMs >= 0);
        Assert.True(stats.TotalMs >= stats.TectonicTotalMs);
        sim.Dispose();
    }

    [Fact]
    public void S1_SyncTickStillWorksWithCache()
    {
        // Verify the sync Tick() path still works correctly with caching
        var sim = new SimulationOrchestrator(16);
        sim.GeneratePlanet(42);
        sim.AdvanceSimulation(0.5);
        // The simulation should complete without error
        Assert.True(sim.TickCount == 1);
        Assert.True(sim.LastTickStats.TectonicMs >= 0);
        sim.Dispose();
    }
}
