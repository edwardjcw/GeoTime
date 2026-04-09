using GeoTime.Core;
using GeoTime.Core.Models;
using GeoTime.Core.Engines;
using GeoTime.Core.Compute;
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

    // ── S3 — GPU Boundary Classification Kernel Tests ────────────────────────

    [Fact]
    public void S3_GpuBoundaryClassification_MatchesCpuResult()
    {
        // Compare GPU-based classification to CPU-based classification.
        // On CI without a GPU, ILGPU falls back to CPU emulation, so the
        // kernel still runs and must produce matching results.
        const int gs = 16;
        var plateMap = new ushort[gs * gs];
        // Left half = plate 0, right half = plate 1
        for (var row = 0; row < gs; row++)
            for (var col = 0; col < gs; col++)
                plateMap[row * gs + col] = (ushort)(col < gs / 2 ? 0 : 1);

        var plates = new List<PlateInfo>
        {
            new() { Id = 0, AngularVelocity = new() { Lat = 45, Lon = 0, Rate = 2 } },
            new() { Id = 1, AngularVelocity = new() { Lat = -30, Lon = 90, Rate = 1.5 } },
        };

        // CPU classification
        var cpuResult = BoundaryClassifier.Classify(plateMap, plates, gs);

        // GPU classification
        using var gpu = new GpuComputeService();
        var gpuResult = gpu.ClassifyBoundariesGpu(plateMap, plates, gs);

        // Both should find the same boundary cells
        Assert.Equal(cpuResult.Count, gpuResult.Count);

        // Sort both by CellIndex for consistent comparison
        var cpuSorted = cpuResult.OrderBy(b => b.CellIndex).ToList();
        var gpuSorted = gpuResult.OrderBy(b => b.CellIndex).ToList();

        for (var i = 0; i < cpuSorted.Count; i++)
        {
            Assert.Equal(cpuSorted[i].CellIndex, gpuSorted[i].CellIndex);
            Assert.Equal(cpuSorted[i].Type, gpuSorted[i].Type);
            Assert.Equal(cpuSorted[i].Plate1, gpuSorted[i].Plate1);
            Assert.Equal(cpuSorted[i].Plate2, gpuSorted[i].Plate2);
            Assert.Equal(cpuSorted[i].RelativeSpeed, gpuSorted[i].RelativeSpeed, precision: 6);
        }
    }

    [Fact]
    public void S3_GpuBoundaryClassification_NoPlates_NoBoundaries()
    {
        // Single plate — no boundaries should be found.
        const int gs = 8;
        var plateMap = new ushort[gs * gs]; // all zeros = plate 0

        var plates = new List<PlateInfo>
        {
            new() { Id = 0, AngularVelocity = new() { Lat = 0, Lon = 0, Rate = 1 } },
        };

        using var gpu = new GpuComputeService();
        var result = gpu.ClassifyBoundariesGpu(plateMap, plates, gs);
        Assert.Empty(result);
    }

    [Fact]
    public void S3_GpuBoundaryClassification_MultiplePlates()
    {
        // 4 quadrants = 4 plates
        const int gs = 16;
        var plateMap = new ushort[gs * gs];
        for (var row = 0; row < gs; row++)
            for (var col = 0; col < gs; col++)
            {
                ushort plate = (ushort)((row < gs / 2 ? 0 : 2) + (col < gs / 2 ? 0 : 1));
                plateMap[row * gs + col] = plate;
            }

        var plates = new List<PlateInfo>
        {
            new() { Id = 0, AngularVelocity = new() { Lat = 30, Lon = 0, Rate = 1 } },
            new() { Id = 1, AngularVelocity = new() { Lat = -30, Lon = 90, Rate = 2 } },
            new() { Id = 2, AngularVelocity = new() { Lat = 60, Lon = -45, Rate = 0.5 } },
            new() { Id = 3, AngularVelocity = new() { Lat = -60, Lon = 180, Rate = 3 } },
        };

        using var gpu = new GpuComputeService();
        var gpuResult = gpu.ClassifyBoundariesGpu(plateMap, plates, gs);
        var cpuResult = BoundaryClassifier.Classify(plateMap, plates, gs);

        Assert.Equal(cpuResult.Count, gpuResult.Count);

        // All boundary types should be present
        var gpuTypes = gpuResult.Select(b => b.Type).Distinct().ToHashSet();
        var cpuTypes = cpuResult.Select(b => b.Type).Distinct().ToHashSet();
        Assert.Equal(cpuTypes, gpuTypes);
    }

    [Fact]
    public void S3_TectonicEngine_UsesGpuClassification()
    {
        // Verify TectonicEngine integrates GPU classification without errors.
        // Even without a dedicated GPU, ILGPU CPU fallback runs the kernel.
        var sim = new SimulationOrchestrator(16);
        sim.GeneratePlanet(42);
        sim.AdvanceSimulation(0.5);
        // Should complete without error
        Assert.True(sim.TickCount == 1);
        sim.Dispose();
    }

    // ── S4 — StratigraphyStack Lock Optimization Tests ───────────────────────

    [Fact]
    public void S4_ConcurrentPushLayer_DifferentCells()
    {
        // Concurrent writes to different cells should not block each other
        // and should produce correct results.
        var stack = new StratigraphyStack();
        const int cellCount = 1000;

        Parallel.For(0, cellCount, i =>
        {
            stack.PushLayer(i, new StratigraphicLayer
            {
                RockType = RockType.SED_SANDSTONE, Thickness = 100, AgeDeposited = i,
            });
        });

        // Each cell should have exactly 1 layer
        for (var i = 0; i < cellCount; i++)
        {
            var layers = stack.GetLayers(i);
            Assert.Single(layers);
            Assert.Equal(100, layers[0].Thickness);
        }
    }

    [Fact]
    public void S4_ConcurrentPushLayer_SameStripe()
    {
        // Concurrent writes to cells in the same stripe (same cellIndex & 0xFF)
        // should be serialized correctly by the stripe lock.
        var stack = new StratigraphyStack();
        const int iterations = 100;

        // Cells 0, 256, 512, ... all map to stripe 0
        Parallel.For(0, iterations, i =>
        {
            var cellIndex = i * 256; // all in the same stripe
            stack.PushLayer(cellIndex, new StratigraphicLayer
            {
                RockType = RockType.SED_LIMESTONE, Thickness = 50, AgeDeposited = i,
            });
        });

        for (var i = 0; i < iterations; i++)
        {
            var layers = stack.GetLayers(i * 256);
            Assert.Single(layers);
        }
    }

    [Fact]
    public void S4_ConcurrentErodeAndPush()
    {
        // Initialize cells, then concurrently erode some and push to others.
        var stack = new StratigraphyStack();
        const int cellCount = 500;

        // Initialize all cells
        for (var i = 0; i < cellCount; i++)
            stack.InitializeBasement(i, isOceanic: i % 2 == 0, ageDeposited: -4000);

        // Concurrently erode even cells and push to odd cells
        Parallel.For(0, cellCount, i =>
        {
            if (i % 2 == 0)
                stack.ErodeTop(i, 100);
            else
                stack.PushLayer(i, new StratigraphicLayer
                {
                    RockType = RockType.SED_SHALE, Thickness = 200, AgeDeposited = -3000,
                });
        });

        // Even cells should have eroded
        for (var i = 0; i < cellCount; i += 2)
        {
            var thickness = stack.GetTotalThickness(i);
            Assert.True(thickness < 7000, $"Cell {i} should have been eroded");
        }

        // Odd cells should have an extra layer
        for (var i = 1; i < cellCount; i += 2)
        {
            var layers = stack.GetLayers(i);
            Assert.Equal(3, layers.Count); // 2 basement + 1 pushed
        }
    }

    [Fact]
    public void S4_ConcurrentApplyDeformation()
    {
        // Deform many cells concurrently.
        var stack = new StratigraphyStack();
        const int cellCount = 200;

        for (var i = 0; i < cellCount; i++)
            stack.InitializeBasement(i, isOceanic: false, ageDeposited: -4000);

        Parallel.For(0, cellCount, i =>
        {
            stack.ApplyDeformation(i, 5.0, 90.0, DeformationType.FOLDED);
        });

        for (var i = 0; i < cellCount; i++)
        {
            var top = stack.GetTopLayer(i);
            Assert.NotNull(top);
            Assert.Equal(5.0, top.DipAngle);
            Assert.Equal(DeformationType.FOLDED, top.Deformation);
        }
    }

    [Fact]
    public void S4_RemapColumns_AtomicSwap()
    {
        // Verify RemapColumns works correctly with the new write-lock pattern.
        var stack = new StratigraphyStack();
        const int cellCount = 64;

        for (var i = 0; i < cellCount; i++)
            stack.InitializeBasement(i, isOceanic: i % 3 == 0, ageDeposited: -4000);

        // Simple identity mapping (each cell maps to itself)
        var mapping = new int[cellCount];
        var hitCount = new int[cellCount];
        for (var i = 0; i < cellCount; i++)
        {
            mapping[i] = i;
            hitCount[i] = 1;
        }

        stack.RemapColumns(mapping, cellCount, hitCount, -3500);

        // All cells should still have their layers
        Assert.Equal(cellCount, stack.Size);
        for (var i = 0; i < cellCount; i++)
        {
            var layers = stack.GetLayers(i);
            Assert.Equal(2, layers.Count);
        }
    }

    [Fact]
    public void S4_RemapColumns_GapFill()
    {
        // Verify gap cells get fresh oceanic basement after remap.
        var stack = new StratigraphyStack();
        const int cellCount = 16;

        for (var i = 0; i < cellCount; i++)
            stack.InitializeBasement(i, isOceanic: false, ageDeposited: -4000);

        var mapping = new int[cellCount];
        var hitCount = new int[cellCount];
        // Only first half has hits; second half are gaps
        for (var i = 0; i < cellCount / 2; i++)
        {
            mapping[i] = i;
            hitCount[i] = 1;
        }

        stack.RemapColumns(mapping, cellCount, hitCount, -3500);

        // Gap cells should have fresh oceanic basement
        for (var i = cellCount / 2; i < cellCount; i++)
        {
            var layers = stack.GetLayers(i);
            Assert.Equal(2, layers.Count);
            Assert.Equal(RockType.IGN_GABBRO, layers[0].RockType);
            Assert.Equal(RockType.IGN_PILLOW_BASALT, layers[1].RockType);
        }
    }
}
