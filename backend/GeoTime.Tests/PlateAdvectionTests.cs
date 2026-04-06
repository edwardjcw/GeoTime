using GeoTime.Core;
using GeoTime.Core.Models;
using GeoTime.Core.Engines;
using GeoTime.Core.Kernel;

namespace GeoTime.Tests;

public class PlateAdvectionTests
{
    private static SimulationState MakeSmallState(int gs = 16)
    {
        var state = new SimulationState(gs);
        // Fill with two plates: left half = 0 (continental), right half = 1 (oceanic)
        for (var row = 0; row < gs; row++)
            for (var col = 0; col < gs; col++)
                state.PlateMap[row * gs + col] = (ushort)(col < gs / 2 ? 0 : 1);

        // Set heights: continental above sea level, oceanic below
        for (var i = 0; i < state.CellCount; i++)
        {
            if (state.PlateMap[i] == 0)
            {
                state.HeightMap[i] = 500f;
                state.CrustThicknessMap[i] = 35f;
                state.RockTypeMap[i] = (byte)RockType.IGN_GRANITE;
            }
            else
            {
                state.HeightMap[i] = -4000f;
                state.CrustThicknessMap[i] = 7f;
                state.RockTypeMap[i] = (byte)RockType.IGN_BASALT;
            }
            state.RockAgeMap[i] = 1000f;
        }

        return state;
    }

    private static List<PlateInfo> MakeTwoPlates(double rate0 = 2.0, double rate1 = 2.0)
    {
        return
        [
            new PlateInfo
            {
                Id = 0, CenterLat = 0, CenterLon = -45, IsOceanic = false, Area = 0.5,
                AngularVelocity = new AngularVelocity { Lat = 90, Lon = 0, Rate = rate0 },
            },
            new PlateInfo
            {
                Id = 1, CenterLat = 0, CenterLon = 45, IsOceanic = true, Area = 0.5,
                AngularVelocity = new AngularVelocity { Lat = 90, Lon = 0, Rate = rate1 },
            },
        ];
    }

    [Fact]
    public void AdvectPlates_PlateMapChangesAfterAdvection()
    {
        var gs = 16;
        var state = MakeSmallState(gs);
        var originalPlateMap = (ushort[])state.PlateMap.Clone();

        // High rotation rates needed on a 16-grid (22.5° per cell) to produce
        // visible cell-crossing movement: Rate=50 → ~25° per Tick(deltaMa=0.5).
        var plates = MakeTwoPlates(rate0: 50.0, rate1: -50.0);
        var bus = new EventBus();
        var log = new EventLog();
        var engine = new TectonicEngine(bus, log, 42, 0.1);
        engine.Initialize(plates, [], new AtmosphericComposition { N2 = 0.78, O2 = 0.21, CO2 = 0.0004, H2O = 0.01 }, state);

        // Run several ticks to accumulate movement
        for (var i = 0; i < 10; i++)
            engine.Tick(100 + i * 0.5, 0.5);

        // After advection, the plate map should have changed
        var anyMoved = false;
        for (var i = 0; i < state.CellCount; i++)
        {
            if (state.PlateMap[i] != originalPlateMap[i])
            {
                anyMoved = true;
                break;
            }
        }
        Assert.True(anyMoved, "Plate map should change after advection (plates should move)");
    }

    [Fact]
    public void AdvectPlates_HeightMapModifiedByAdvection()
    {
        var gs = 16;
        var state = MakeSmallState(gs);
        var originalHeights = (float[])state.HeightMap.Clone();

        var plates = MakeTwoPlates(rate0: 3.0, rate1: -3.0);
        var bus = new EventBus();
        var log = new EventLog();
        var engine = new TectonicEngine(bus, log, 42, 0.1);
        engine.Initialize(plates, [], new AtmosphericComposition { N2 = 0.78, O2 = 0.21, CO2 = 0.0004, H2O = 0.01 }, state);

        for (var i = 0; i < 10; i++)
            engine.Tick(100 + i * 0.5, 0.5);

        var anyChanged = false;
        for (var i = 0; i < state.CellCount; i++)
        {
            if (Math.Abs(state.HeightMap[i] - originalHeights[i]) > 0.1f)
            {
                anyChanged = true;
                break;
            }
        }
        Assert.True(anyChanged, "Heights should change after plate advection");
    }

    [Fact]
    public void AdvectPlates_GapCellsFilledWithOceanicCrust()
    {
        var gs = 16;
        var state = MakeSmallState(gs);

        // Use divergent plates (opposite rotation) to create gaps
        var plates = MakeTwoPlates(rate0: 5.0, rate1: -5.0);
        var bus = new EventBus();
        var log = new EventLog();
        var engine = new TectonicEngine(bus, log, 42, 0.1);
        engine.Initialize(plates, [], new AtmosphericComposition { N2 = 0.78, O2 = 0.21, CO2 = 0.0004, H2O = 0.01 }, state);

        // Advance several ticks
        for (var i = 0; i < 5; i++)
            engine.Tick(100 + i * 0.5, 0.5);

        // Some cells should now have basalt (gap-fill oceanic crust)
        var hasBasalt = false;
        for (var i = 0; i < state.CellCount; i++)
        {
            if (state.RockTypeMap[i] == (byte)RockType.IGN_BASALT &&
                state.CrustThicknessMap[i] <= 8f)
            {
                hasBasalt = true;
                break;
            }
        }
        Assert.True(hasBasalt, "Gap cells should be filled with thin oceanic basalt crust");
    }

    [Fact]
    public void AdvectPlates_ZeroRateDoesNotMoveAnything()
    {
        var gs = 16;
        var state = MakeSmallState(gs);
        var originalPlateMap = (ushort[])state.PlateMap.Clone();
        var originalHeights = (float[])state.HeightMap.Clone();

        var plates = MakeTwoPlates(rate0: 0, rate1: 0);
        var bus = new EventBus();
        var log = new EventLog();
        var engine = new TectonicEngine(bus, log, 42, 0.1);
        engine.Initialize(plates, [], new AtmosphericComposition { N2 = 0.78, O2 = 0.21, CO2 = 0.0004, H2O = 0.01 }, state);

        // Run advection with zero rate — plate map and heights should remain
        // almost unchanged (isostasy + boundary processes still run but
        // advection itself should be a no-op identity rotation).
        engine.Tick(100, 0.1);

        var plateIdentical = true;
        for (var i = 0; i < state.CellCount; i++)
        {
            if (state.PlateMap[i] != originalPlateMap[i])
            {
                plateIdentical = false;
                break;
            }
        }
        Assert.True(plateIdentical, "Zero rotation rate should preserve plate map");
    }

    [Fact]
    public void AdvectPlates_StratigraphyRemapsWithCells()
    {
        var gs = 16;
        var state = MakeSmallState(gs);

        var plates = MakeTwoPlates(rate0: 5.0, rate1: -5.0);
        var bus = new EventBus();
        var log = new EventLog();
        var engine = new TectonicEngine(bus, log, 42, 0.1);
        engine.Initialize(plates, [], new AtmosphericComposition { N2 = 0.78, O2 = 0.21, CO2 = 0.0004, H2O = 0.01 }, state);

        // After initialisation every cell has stratigraphy
        Assert.True(engine.Stratigraphy.Size > 0, "Stratigraphy should be initialised");

        // After advection, stratigraphy should still cover most cells
        for (var i = 0; i < 5; i++)
            engine.Tick(100 + i * 0.5, 0.5);

        Assert.True(engine.Stratigraphy.Size > 0, "Stratigraphy should persist after advection");
    }

    [Fact]
    public void AdvectPlates_PlateCentersUpdateAfterAdvection()
    {
        var gs = 16;
        var state = MakeSmallState(gs);

        // Give plate 0 a strong rotation so its center should shift
        var plates = MakeTwoPlates(rate0: 10.0, rate1: 0);
        var originalCenter0Lon = plates[0].CenterLon;

        var bus = new EventBus();
        var log = new EventLog();
        var engine = new TectonicEngine(bus, log, 42, 0.1);
        engine.Initialize(plates, [], new AtmosphericComposition { N2 = 0.78, O2 = 0.21, CO2 = 0.0004, H2O = 0.01 }, state);

        for (var i = 0; i < 10; i++)
            engine.Tick(100 + i * 0.5, 0.5);

        var updatedPlates = engine.GetPlates();
        var newCenterLon = updatedPlates[0].CenterLon;

        // The center longitude should have shifted due to plate rotation
        Assert.NotEqual(originalCenter0Lon, newCenterLon, 3);
    }

    [Fact]
    public void AdvectPlates_CollisionBuildsMountains()
    {
        var gs = 16;
        var state = MakeSmallState(gs);

        // Both plates continental, moving towards each other
        var plates = new List<PlateInfo>
        {
            new()
            {
                Id = 0, CenterLat = 0, CenterLon = -45, IsOceanic = false, Area = 0.5,
                AngularVelocity = new AngularVelocity { Lat = 90, Lon = 0, Rate = 8.0 },
            },
            new()
            {
                Id = 1, CenterLat = 0, CenterLon = 45, IsOceanic = false, Area = 0.5,
                AngularVelocity = new AngularVelocity { Lat = 90, Lon = 0, Rate = -8.0 },
            },
        };

        // Make both plates continental
        for (var i = 0; i < state.CellCount; i++)
        {
            state.HeightMap[i] = 200f;
            state.CrustThicknessMap[i] = 35f;
        }

        var bus = new EventBus();
        var log = new EventLog();
        var engine = new TectonicEngine(bus, log, 42, 0.1);
        engine.Initialize(plates, [], new AtmosphericComposition { N2 = 0.78, O2 = 0.21, CO2 = 0.0004, H2O = 0.01 }, state);

        for (var i = 0; i < 10; i++)
            engine.Tick(100 + i * 0.5, 0.5);

        // Some cells should be higher than the original 200m due to collision
        var maxHeight = state.HeightMap.Max();
        Assert.True(maxHeight > 200f, $"Collision should build mountains (max height={maxHeight})");
    }

    [Fact]
    public void AdvectPlates_IntegrationWithOrchestrator()
    {
        var sim = new SimulationOrchestrator(32);
        sim.GeneratePlanet(42);

        var initialPlateMap = (ushort[])sim.State.PlateMap.Clone();
        var initialHeights = (float[])sim.State.HeightMap.Clone();

        // Run 10 ticks to accumulate plate movement
        for (var i = 0; i < 10; i++)
            sim.AdvanceSimulation(0.5);

        // Plate map and/or heights should have changed
        var anyPlateChange = false;
        var anyHeightChange = false;
        for (var i = 0; i < sim.State.CellCount; i++)
        {
            if (sim.State.PlateMap[i] != initialPlateMap[i])
                anyPlateChange = true;
            if (Math.Abs(sim.State.HeightMap[i] - initialHeights[i]) > 0.1f)
                anyHeightChange = true;
            if (anyPlateChange && anyHeightChange) break;
        }
        Assert.True(anyHeightChange, "Heights should change after advancing with plate advection");
    }

    [Fact]
    public void RemapColumns_MovesStratigraphyToNewPositions()
    {
        var stack = new StratigraphyStack();
        var cc = 4; // 4 cells

        // Initialize cell 0 and 1 with stratigraphy
        stack.InitializeBasement(0, false, 1000);
        stack.InitializeBasement(1, true, 500);

        // Mapping: cell 0 → cell 2, cell 1 → cell 3
        var mapping = new int[] { 2, 3, 0, 1 };
        var hitCount = new int[] { 1, 1, 1, 1 };

        stack.RemapColumns(mapping, cc, hitCount, 100);

        // Cell 2 should now have the continental stratigraphy from cell 0
        var layers2 = stack.GetLayers(2);
        Assert.NotEmpty(layers2);

        // Cell 3 should now have the oceanic stratigraphy from cell 1
        var layers3 = stack.GetLayers(3);
        Assert.NotEmpty(layers3);
    }

    [Fact]
    public void RemapColumns_GapCellsGetFreshOceanicBasement()
    {
        var stack = new StratigraphyStack();
        var cc = 4;

        stack.InitializeBasement(0, false, 1000);

        // Mapping: cell 0 → cell 1; cells 2, 3 are gaps
        var mapping = new int[] { 1, 1, 2, 3 };
        var hitCount = new int[] { 0, 2, 1, 1 };

        stack.RemapColumns(mapping, cc, hitCount, 100);

        // Cell 0 should have fresh oceanic basement (gap fill)
        var layers0 = stack.GetLayers(0);
        Assert.NotEmpty(layers0);
        Assert.Equal(RockType.IGN_GABBRO, layers0[0].RockType);
    }
}
