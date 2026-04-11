using GeoTime.Core;
using GeoTime.Core.Models;
using GeoTime.Core.Engines;
using GeoTime.Core.Kernel;

namespace GeoTime.Tests;

public class PlateCoherenceTests
{
    private static SimulationState MakeState(int gs = 16)
    {
        var state = new SimulationState(gs);
        for (var i = 0; i < state.CellCount; i++)
        {
            state.HeightMap[i] = -4000f;
            state.CrustThicknessMap[i] = 7f;
            state.RockTypeMap[i] = (byte)RockType.IGN_BASALT;
            state.RockAgeMap[i] = 1000f;
        }
        return state;
    }

    private static (TectonicEngine engine, List<PlateInfo> plates) MakeEngine(
        SimulationState state, int numPlates, uint seed = 42)
    {
        var plates = new List<PlateInfo>();
        for (var p = 0; p < numPlates; p++)
        {
            plates.Add(new PlateInfo
            {
                Id = p,
                CenterLat = 0,
                CenterLon = -90 + p * (180.0 / numPlates),
                IsOceanic = true,
                Area = 1.0 / numPlates,
                AngularVelocity = new AngularVelocity { Lat = 90, Lon = 0, Rate = 1.0 },
            });
        }
        var bus = new EventBus();
        var log = new EventLog();
        var engine = new TectonicEngine(bus, log, seed, 0.1);
        engine.Initialize(plates, [], new AtmosphericComposition
            { N2 = 0.78, O2 = 0.21, CO2 = 0.0004, H2O = 0.01 }, state);
        return (engine, plates);
    }

    [Fact]
    public void ConsolidatePlateFragments_RemovesIsolatedCells()
    {
        // Setup: plate 0 fills the grid except for a few isolated plate-1 cells
        var gs = 16;
        var state = MakeState(gs);
        for (var i = 0; i < state.CellCount; i++)
            state.PlateMap[i] = 0;

        // Place a single isolated cell of plate 1 in the interior of plate 0
        var isolatedIdx = 5 * gs + 5; // row 5, col 5 — deep in plate 0 territory
        state.PlateMap[isolatedIdx] = 1;

        var (engine, _) = MakeEngine(state, 2);
        engine.ConsolidatePlateFragments(state);

        // The isolated cell should have been reassigned to plate 0
        Assert.Equal(0, state.PlateMap[isolatedIdx]);
    }

    [Fact]
    public void ConsolidatePlateFragments_PreservesLargeRegions()
    {
        // Setup: two plates each occupying half the grid (left/right)
        var gs = 16;
        var state = MakeState(gs);
        for (var row = 0; row < gs; row++)
            for (var col = 0; col < gs; col++)
                state.PlateMap[row * gs + col] = (ushort)(col < gs / 2 ? 0 : 1);

        var (engine, _) = MakeEngine(state, 2);

        var originalPlateMap = (ushort[])state.PlateMap.Clone();
        engine.ConsolidatePlateFragments(state);

        // Both plate halves are large, so no cells should be reassigned
        for (var i = 0; i < state.CellCount; i++)
            Assert.Equal(originalPlateMap[i], state.PlateMap[i]);
    }

    [Fact]
    public void ConsolidatePlateFragments_RemovesSmallCluster()
    {
        // Setup: plate 0 fills the grid, but a 2×2 block of plate 1 is in the middle
        var gs = 32; // Larger grid so 4 cells is definitely small relative to total
        var state = MakeState(gs);
        for (var i = 0; i < state.CellCount; i++)
            state.PlateMap[i] = 0;

        // Place a 2×2 cluster of plate 1 at (10,10)
        state.PlateMap[10 * gs + 10] = 1;
        state.PlateMap[10 * gs + 11] = 1;
        state.PlateMap[11 * gs + 10] = 1;
        state.PlateMap[11 * gs + 11] = 1;

        var (engine, _) = MakeEngine(state, 2);
        engine.ConsolidatePlateFragments(state);

        // All 4 cells should be reassigned to plate 0 (they're far too small)
        Assert.Equal(0, state.PlateMap[10 * gs + 10]);
        Assert.Equal(0, state.PlateMap[10 * gs + 11]);
        Assert.Equal(0, state.PlateMap[11 * gs + 10]);
        Assert.Equal(0, state.PlateMap[11 * gs + 11]);
    }

    [Fact]
    public void ConsolidatePlateFragments_ReducesBoundaryCount()
    {
        // Create a fragmented plate map with scattered cells
        var gs = 16;
        var state = MakeState(gs);
        for (var i = 0; i < state.CellCount; i++)
            state.PlateMap[i] = 0;

        // Scatter individual plate-1 cells throughout plate 0
        var rng = new Random(42);
        for (var i = 0; i < state.CellCount; i++)
        {
            if (rng.NextDouble() < 0.1) // 10% scattered cells
                state.PlateMap[i] = 1;
        }

        // Also give plate 1 a solid block so it has a main body
        for (var row = 0; row < 4; row++)
            for (var col = 12; col < gs; col++)
                state.PlateMap[row * gs + col] = 1;

        var plates = new List<PlateInfo>
        {
            new() { Id = 0, CenterLat = 0, CenterLon = -45, IsOceanic = true, Area = 0.5,
                AngularVelocity = new AngularVelocity { Lat = 90, Lon = 0, Rate = 1 } },
            new() { Id = 1, CenterLat = 0, CenterLon = 45, IsOceanic = true, Area = 0.5,
                AngularVelocity = new AngularVelocity { Lat = 90, Lon = 0, Rate = 1 } },
        };
        var bus = new EventBus();
        var log = new EventLog();
        var engine = new TectonicEngine(bus, log, 42, 0.1);
        engine.Initialize(plates, [], new AtmosphericComposition
            { N2 = 0.78, O2 = 0.21, CO2 = 0.0004, H2O = 0.01 }, state);

        // Count boundaries before consolidation
        var boundariesBefore = BoundaryClassifier.Classify(state.PlateMap, plates, gs);

        engine.ConsolidatePlateFragments(state);

        // Count boundaries after consolidation
        var boundariesAfter = BoundaryClassifier.Classify(state.PlateMap, plates, gs);

        // After consolidation, there should be fewer boundary cells
        Assert.True(boundariesAfter.Count < boundariesBefore.Count,
            $"Boundaries should decrease after consolidation: {boundariesBefore.Count} → {boundariesAfter.Count}");
    }

    [Fact]
    public void AbsorbTinyPlates_SmallPlateGetsAbsorbed()
    {
        // Setup: plate 0 fills most of the grid, plate 1 has just 2 cells
        var gs = 32;
        var state = MakeState(gs);
        for (var i = 0; i < state.CellCount; i++)
            state.PlateMap[i] = 0;

        // Give plate 1 just 3 cells (below MIN_PLATE_FRACTION threshold)
        state.PlateMap[15 * gs + 15] = 1;
        state.PlateMap[15 * gs + 16] = 1;
        state.PlateMap[16 * gs + 15] = 1;

        var (engine, plates) = MakeEngine(state, 2);
        engine.ManagePlateLifecycle(state, 100.0);

        // Plate 1 cells should now belong to plate 0
        Assert.Equal(0, state.PlateMap[15 * gs + 15]);
        Assert.Equal(0, state.PlateMap[15 * gs + 16]);
        Assert.Equal(0, state.PlateMap[16 * gs + 15]);
    }

    [Fact]
    public void AbsorbTinyPlates_LargePlatesSurvive()
    {
        // Two large plates, each occupying half the grid
        var gs = 16;
        var state = MakeState(gs);
        for (var row = 0; row < gs; row++)
            for (var col = 0; col < gs; col++)
                state.PlateMap[row * gs + col] = (ushort)(col < gs / 2 ? 0 : 1);

        var (engine, _) = MakeEngine(state, 2);
        var originalPlateMap = (ushort[])state.PlateMap.Clone();
        engine.ManagePlateLifecycle(state, 100.0);

        // Both plates should survive — no cells reassigned
        for (var i = 0; i < state.CellCount; i++)
            Assert.Equal(originalPlateMap[i], state.PlateMap[i]);
    }

    [Fact]
    public void NucleateRiftPlates_CreatesNewPlateAtRiftZone()
    {
        // Setup: create a large rift zone with young basalt at a boundary
        var gs = 32;
        var state = MakeState(gs);
        var cc = gs * gs;

        // Fill with plate 0 on top half, plate 1 on bottom half
        for (var row = 0; row < gs; row++)
            for (var col = 0; col < gs; col++)
                state.PlateMap[row * gs + col] = (ushort)(row < gs / 2 ? 0 : 1);

        var timeMa = 100.0;

        // Create a large rift zone along the boundary (rows 14-17) with young crust
        for (var row = 14; row <= 17; row++)
        {
            for (var col = 0; col < gs; col++)
            {
                var idx = row * gs + col;
                state.RockTypeMap[idx] = (byte)RockType.IGN_BASALT;
                state.CrustThicknessMap[idx] = 7f;
                state.HeightMap[idx] = -4000f;
                state.RockAgeMap[idx] = (float)timeMa; // Very young
            }
        }

        var (engine, plates) = MakeEngine(state, 2);
        var plateCountBefore = engine.GetPlates().Count;

        engine.ManagePlateLifecycle(state, timeMa);

        // A new plate should have been created
        var plateCountAfter = engine.GetPlates().Count;
        Assert.True(plateCountAfter > plateCountBefore,
            $"Expected new plate to nucleate at rift zone, but plates went from {plateCountBefore} to {plateCountAfter}");
    }

    [Fact]
    public void IntegrationWithOrchestrator_PlatesRemainCoherent()
    {
        var sim = new SimulationOrchestrator(32);
        sim.GeneratePlanet(42);

        // Run 20 ticks to accumulate plate movement
        for (var i = 0; i < 20; i++)
            sim.AdvanceSimulation(0.5);

        // Count boundary cells — with consolidation, the boundary count
        // should be reasonable (not growing explosively)
        var plates = sim.State.PlateMap;
        var gs = sim.State.GridSize;
        var boundaryCount = 0;
        for (var row = 0; row < gs; row++)
        {
            for (var col = 0; col < gs; col++)
            {
                var idx = row * gs + col;
                int myPlate = plates[idx];
                // Check if this cell is at a plate boundary
                var isBoundary = false;
                if (row > 0 && plates[(row - 1) * gs + col] != myPlate) isBoundary = true;
                if (!isBoundary && row < gs - 1 && plates[(row + 1) * gs + col] != myPlate) isBoundary = true;
                if (!isBoundary && plates[row * gs + ((col - 1 + gs) % gs)] != myPlate) isBoundary = true;
                if (!isBoundary && plates[row * gs + ((col + 1) % gs)] != myPlate) isBoundary = true;
                if (isBoundary) boundaryCount++;
            }
        }

        // Boundary cells should be a reasonable fraction of total cells (< 40%)
        // On a small 32×32 grid with 10+ plates, boundaries naturally take up
        // more space proportionally. Without consolidation, lattice fragmentation
        // can push this above 60% on small grids.
        var fraction = (double)boundaryCount / sim.State.CellCount;
        Assert.True(fraction < 0.40,
            $"Boundary fraction should be < 40% with consolidation, but was {fraction:P1} ({boundaryCount} of {sim.State.CellCount})");
    }

    [Fact]
    public void ConsolidatePlateFragments_DoesNotChangeWithUniformPlate()
    {
        // If the entire grid is one plate, consolidation should be a no-op
        var gs = 16;
        var state = MakeState(gs);
        for (var i = 0; i < state.CellCount; i++)
            state.PlateMap[i] = 0;

        var (engine, _) = MakeEngine(state, 1);
        var originalPlateMap = (ushort[])state.PlateMap.Clone();
        engine.ConsolidatePlateFragments(state);

        for (var i = 0; i < state.CellCount; i++)
            Assert.Equal(originalPlateMap[i], state.PlateMap[i]);
    }

    [Fact]
    public void LongRunning_BoundaryCountStaysReasonable()
    {
        // Run many ticks and verify the boundary count doesn't explode
        var sim = new SimulationOrchestrator(32);
        sim.GeneratePlanet(123);

        var initialPlateMap = (ushort[])sim.State.PlateMap.Clone();
        var gs = sim.State.GridSize;

        // Compute initial boundary count
        int CountBoundaries(ushort[] pm)
        {
            var count = 0;
            for (var row = 0; row < gs; row++)
            for (var col = 0; col < gs; col++)
            {
                var idx = row * gs + col;
                int p = pm[idx];
                if (row > 0 && pm[(row - 1) * gs + col] != p) { count++; continue; }
                if (row < gs - 1 && pm[(row + 1) * gs + col] != p) { count++; continue; }
                if (pm[row * gs + ((col - 1 + gs) % gs)] != p) { count++; continue; }
                if (pm[row * gs + ((col + 1) % gs)] != p) count++;
            }
            return count;
        }

        var initialBoundaries = CountBoundaries(sim.State.PlateMap);

        // Run 30 ticks
        for (var i = 0; i < 30; i++)
            sim.AdvanceSimulation(0.5);

        var finalBoundaries = CountBoundaries(sim.State.PlateMap);

        // Boundary count should not grow by more than 3x from the initial state
        Assert.True(finalBoundaries < initialBoundaries * 3,
            $"Boundary count grew too much: {initialBoundaries} → {finalBoundaries} (more than 3x)");
    }
}
