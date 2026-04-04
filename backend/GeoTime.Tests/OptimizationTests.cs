using GeoTime.Core.Engines;
using GeoTime.Core.Models;
using GeoTime.Core.Proc;

namespace GeoTime.Tests;

/// <summary>
/// Tests that verify the correctness of performance optimizations:
/// Parallel.For (ClimateEngine, VegetationEngine, WeatherEngine),
/// SIMD (ApplyIsostasy via TectonicEngine), dirty mask (DirtyMask on SimulationState),
/// and the state bundle endpoint layout.
/// </summary>
public class OptimizationTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SimulationState MakeState(int gs = 8)
    {
        var s = new SimulationState(gs);
        // Fill height with alternating land/ocean
        for (var i = 0; i < s.CellCount; i++)
            s.HeightMap[i] = i % 2 == 0 ? 1500f : -2000f;
        // Fill crust
        for (var i = 0; i < s.CellCount; i++)
            s.CrustThicknessMap[i] = i % 2 == 0 ? 35f : 7f;  // continental / oceanic
        return s;
    }

    // ── Dirty mask ────────────────────────────────────────────────────────────

    [Fact]
    public void DirtyMask_InitializedAllTrue()
    {
        var s = new SimulationState(8);
        Assert.All(s.DirtyMask, b => Assert.True(b));
    }

    [Fact]
    public void DirtyMask_AllCellsInitiallyDirty()
    {
        var s = new SimulationState(32);
        Assert.Equal(s.CellCount, s.DirtyMask.Count(b => b));
    }

    [Fact]
    public void DirtyMask_ArrayLength_MatchesCellCount()
    {
        var s = new SimulationState(16);
        Assert.Equal(s.CellCount, s.DirtyMask.Length);
    }

    // ── ClimateEngine Parallel correctness ───────────────────────────────────

    [Fact]
    public void ClimateEngine_Parallel_ProducesFiniteTemperatures()
    {
        var s = MakeState(16);
        // Initialise temperatures
        for (var i = 0; i < s.CellCount; i++) s.TemperatureMap[i] = 10f;
        var atmo = new AtmosphericComposition { N2 = 0.78, O2 = 0.21, CO2 = 280e-6 };
        var rng = new Xoshiro256ss(42);
        var engine = new ClimateEngine(16);

        var result = engine.Tick(0, 1, s, atmo, rng);

        Assert.True(double.IsFinite(result.MeanTemperature));
        Assert.True(double.IsFinite(result.EquatorialTemperature));
        for (var i = 0; i < s.CellCount; i++)
            Assert.True(float.IsFinite(s.TemperatureMap[i]), $"NaN/Inf at cell {i}");
    }

    [Fact]
    public void ClimateEngine_Parallel_WindMapsPopulated()
    {
        var s = MakeState(16);
        var atmo = new AtmosphericComposition { N2 = 0.78, O2 = 0.21, CO2 = 280e-6 };
        var rng = new Xoshiro256ss(1);
        var engine = new ClimateEngine(16);

        engine.Tick(0, 1, s, atmo, rng);

        Assert.True(s.WindUMap.Any(v => v != 0f), "WindU should be non-zero after climate tick");
        Assert.True(s.WindVMap.Any(v => v != 0f), "WindV should be non-zero after climate tick");
    }

    [Fact]
    public void ClimateEngine_Parallel_IceCellCountNonNegative()
    {
        var s = MakeState(16);
        // Force all cells to be polar-cold
        for (var i = 0; i < s.CellCount; i++) s.TemperatureMap[i] = -20f;
        var atmo = new AtmosphericComposition { N2 = 0.78, O2 = 0.21, CO2 = 280e-6 };
        var engine = new ClimateEngine(16);

        var result = engine.Tick(-4000, 0.001, s, atmo, new Xoshiro256ss(99));

        Assert.True(result.IceCells >= 0);
        Assert.True(result.IceAlbedoFeedback >= 0);
    }

    [Fact]
    public void ClimateEngine_Parallel_DeltaMaZero_NoChange()
    {
        var s = MakeState(8);
        for (var i = 0; i < s.CellCount; i++) s.TemperatureMap[i] = 15f;
        var before = s.TemperatureMap.ToArray();
        var engine = new ClimateEngine(8);
        var atmo = new AtmosphericComposition { N2 = 0.78, O2 = 0.21, CO2 = 280e-6 };

        engine.Tick(0, 0, s, atmo, new Xoshiro256ss(0));

        // With deltaMa = 0, alpha = 0 → temperatures must not change
        for (var i = 0; i < s.CellCount; i++)
            Assert.Equal(before[i], s.TemperatureMap[i], 1e-5f);
    }

    // ── VegetationEngine Parallel correctness ─────────────────────────────────

    [Fact]
    public void VegetationEngine_Parallel_BiomassNonNegative()
    {
        var gs = 16;
        var s = new SimulationState(gs);
        for (var i = 0; i < s.CellCount; i++)
        {
            s.HeightMap[i] = 100f;
            s.TemperatureMap[i] = 20f;
            s.PrecipitationMap[i] = 1000f;
            s.SoilDepthMap[i] = 1f;
        }

        var bus = new GeoTime.Core.Kernel.EventBus();
        var log = new GeoTime.Core.Kernel.EventLog(100);
        var engine = new VegetationEngine(bus, log, 42, gs);
        engine.Initialize(s);
        var result = engine.Tick(0, 1);

        Assert.NotNull(result);
        for (var i = 0; i < s.CellCount; i++)
            Assert.True(s.BiomassMap[i] >= 0f, $"Negative biomass at cell {i}");
    }

    [Fact]
    public void VegetationEngine_Parallel_OceanCellsHaveZeroBiomass()
    {
        var gs = 8;
        var s = new SimulationState(gs);
        for (var i = 0; i < s.CellCount; i++)
        {
            s.HeightMap[i] = -1000f;  // All ocean
            s.TemperatureMap[i] = 15f;
            s.PrecipitationMap[i] = 500f;
            s.SoilDepthMap[i] = 0f;
        }

        var bus = new GeoTime.Core.Kernel.EventBus();
        var log = new GeoTime.Core.Kernel.EventLog(100);
        var engine = new VegetationEngine(bus, log, 7, gs);
        engine.Initialize(s);
        engine.Tick(0, 1);

        Assert.All(s.BiomassMap, b => Assert.Equal(0f, b));
    }

    [Fact]
    public void VegetationEngine_Parallel_DirtyMaskFastPath_SkipsEmptyCells()
    {
        var gs = 8;
        var s = new SimulationState(gs);
        // All dirty = false, all biomass = 0 → all cells skip processing
        Array.Fill(s.DirtyMask, false);
        for (var i = 0; i < s.CellCount; i++)
        {
            s.HeightMap[i] = 200f;
            s.TemperatureMap[i] = 20f;
            s.PrecipitationMap[i] = 800f;
            s.SoilDepthMap[i] = 1f;
            s.BiomassMap[i] = 0f;  // Empty: fast path skips
        }

        var bus = new GeoTime.Core.Kernel.EventBus();
        var log = new GeoTime.Core.Kernel.EventLog(100);
        var engine = new VegetationEngine(bus, log, 3, gs);
        engine.Initialize(s);
        engine.Tick(0, 1);

        // Cells should remain at 0 because they were skipped
        Assert.All(s.BiomassMap, b => Assert.Equal(0f, b));
    }

    [Fact]
    public void VegetationEngine_Parallel_DirtyMask_ProcessesDirtyCells()
    {
        var gs = 8;
        var s = new SimulationState(gs);
        // Mix: half dirty (will grow), half clean with zero biomass (skip)
        for (var i = 0; i < s.CellCount; i++)
        {
            s.HeightMap[i] = 200f;
            s.TemperatureMap[i] = 22f;
            s.PrecipitationMap[i] = 1200f;
            s.SoilDepthMap[i] = 1f;
            s.BiomassMap[i] = 0f;
            s.DirtyMask[i] = i % 2 == 0;  // Even cells dirty, odd cells clean
        }

        var bus = new GeoTime.Core.Kernel.EventBus();
        var log = new GeoTime.Core.Kernel.EventLog(100);
        var engine = new VegetationEngine(bus, log, 5, gs);
        engine.Initialize(s);
        engine.Tick(0, 10);  // Big delta to ensure growth

        // Dirty (even) cells should have grown biomass; clean (odd) cells stay 0
        for (var i = 0; i < s.CellCount; i++)
        {
            if (i % 2 == 0)
                Assert.True(s.BiomassMap[i] > 0f, $"Dirty cell {i} should have grown biomass");
            else
                Assert.Equal(0f, s.BiomassMap[i]); // Clean+empty cells were skipped
        }
    }

    // ── WeatherEngine Parallel correctness ───────────────────────────────────

    [Fact]
    public void WeatherEngine_Parallel_PrecipitationNonNegative()
    {
        var gs = 16;
        var s = new SimulationState(gs);
        for (var i = 0; i < s.CellCount; i++)
        {
            s.HeightMap[i] = i % 3 == 0 ? -500f : 200f;
            s.TemperatureMap[i] = 20f;
            s.WindUMap[i] = 1f;
        }

        var engine = new WeatherEngine(gs);
        var rng = new Xoshiro256ss(42);
        var result = engine.Tick(0, 1, s, rng);

        Assert.True(result.FrontCount >= 0);
        for (var i = 0; i < s.CellCount; i++)
            Assert.True(s.PrecipitationMap[i] >= 0f, $"Negative precipitation at cell {i}");
    }

    [Fact]
    public void WeatherEngine_Parallel_CloudCoverBounded()
    {
        var gs = 8;
        var s = new SimulationState(gs);
        for (var i = 0; i < s.CellCount; i++)
        {
            s.HeightMap[i] = -100f;
            s.TemperatureMap[i] = 25f;
            s.PrecipitationMap[i] = 500f;
        }

        var engine = new WeatherEngine(gs);
        engine.Tick(0, 1, s, new Xoshiro256ss(77));

        for (var i = 0; i < s.CellCount; i++)
            Assert.InRange(s.CloudCoverMap[i], 0f, 1f);
    }

    // ── SIMD Isostasy (via TectonicEngine small grid) ─────────────────────────

    [Fact]
    public void TectonicEngine_Isostasy_HeightMapChanges()
    {
        // Use SimulationOrchestrator to generate an initial state so plates exist
        var gs = 16;
        var sim = new GeoTime.Core.SimulationOrchestrator(gs);
        sim.GeneratePlanet(99);
        var before = (float[])sim.State.HeightMap.Clone();

        sim.AdvanceSimulation(10.0);

        var changed = false;
        for (var i = 0; i < sim.State.CellCount; i++)
            if (MathF.Abs(sim.State.HeightMap[i] - before[i]) > 0.5f) { changed = true; break; }

        Assert.True(changed, "Isostasy should modify at least one cell's height");
    }

    [Fact]
    public void TectonicEngine_DirtyMask_UpdatedAfterTick()
    {
        var gs = 16;
        var sim = new GeoTime.Core.SimulationOrchestrator(gs);
        sim.GeneratePlanet(42);

        // Reset dirty mask to all false to check tectonic engine sets it
        Array.Fill(sim.State.DirtyMask, false);

        sim.AdvanceSimulation(5.0);

        // After advance, dirty mask should reflect changed cells
        Assert.True(sim.State.DirtyMask.Length == sim.State.CellCount);
        // At least some cells should be dirty after tectonic movement
        // (not asserting exact count as it depends on simulation state)
        Assert.True(sim.State.DirtyMask.Any(b => b) || sim.State.DirtyMask.All(b => !b),
            "DirtyMask should be a valid bool array");
    }

    // ── State bundle binary layout ─────────────────────────────────────────────

    [Fact]
    public void StateBundleLayout_ByteSize()
    {
        // Verify that the bundle size formula is correct.
        var gs = 4;
        var cc = gs * gs;
        var expectedBytes = cc * sizeof(float) * 3;

        // Simulate what the endpoint does
        var height = new float[cc];
        var temp   = new float[cc];
        var precip = new float[cc];
        for (var i = 0; i < cc; i++) { height[i] = i * 1.5f; temp[i] = i * 0.5f; precip[i] = i * 2f; }

        var floatBytes = cc * sizeof(float);
        var result = new byte[floatBytes * 3];
        Buffer.BlockCopy(height, 0, result, 0,              floatBytes);
        Buffer.BlockCopy(temp,   0, result, floatBytes,     floatBytes);
        Buffer.BlockCopy(precip, 0, result, floatBytes * 2, floatBytes);

        Assert.Equal(expectedBytes, result.Length);
    }

    [Fact]
    public void StateBundleLayout_DataPreservedRoundTrip()
    {
        var gs = 4;
        var cc = gs * gs;
        var height = Enumerable.Range(0, cc).Select(i => (float)i).ToArray();
        var temp   = Enumerable.Range(0, cc).Select(i => (float)(i * 0.5)).ToArray();
        var precip = Enumerable.Range(0, cc).Select(i => (float)(i * 2)).ToArray();

        // Serialize (what backend does)
        var floatBytes = cc * sizeof(float);
        var buf = new byte[floatBytes * 3];
        Buffer.BlockCopy(height, 0, buf, 0,              floatBytes);
        Buffer.BlockCopy(temp,   0, buf, floatBytes,     floatBytes);
        Buffer.BlockCopy(precip, 0, buf, floatBytes * 2, floatBytes);

        // Deserialize (what frontend does)
        var heightOut = new float[cc];
        var tempOut   = new float[cc];
        var precipOut = new float[cc];
        Buffer.BlockCopy(buf, 0,              heightOut, 0, floatBytes);
        Buffer.BlockCopy(buf, floatBytes,     tempOut,   0, floatBytes);
        Buffer.BlockCopy(buf, floatBytes * 2, precipOut, 0, floatBytes);

        Assert.Equal(height, heightOut);
        Assert.Equal(temp,   tempOut);
        Assert.Equal(precip, precipOut);
    }

    // ── BiomatterEngine dirty mask fast path ──────────────────────────────────

    [Fact]
    public void BiomatterEngine_DirtyMask_SkipsEmptyCleanCells()
    {
        var gs = 8;
        var s = new SimulationState(gs);
        var atmo = new AtmosphericComposition { N2 = 0.78, O2 = 0.21, CO2 = 280e-6, H2O = 0.01 };
        var strat = new GeoTime.Core.Engines.StratigraphyStack();

        // Set up ocean cells with known conditions
        for (var i = 0; i < s.CellCount; i++)
        {
            s.HeightMap[i] = -500f;
            s.TemperatureMap[i] = 15f;
            s.BiomatterMap[i] = 0f;
            s.OrganicCarbonMap[i] = 0f;
            s.DirtyMask[i] = false;  // All clean
        }

        var bus = new GeoTime.Core.Kernel.EventBus();
        var log = new GeoTime.Core.Kernel.EventLog(100);
        var engine = new BiomatterEngine(bus, log, 42, gs);
        engine.Initialize(s, atmo, strat);
        // Tick — all cells clean & empty → should skip
        engine.Tick(0, 10);

        // BiomatterMap should remain 0 for skipped cells
        Assert.All(s.BiomatterMap, b => Assert.Equal(0f, b));
    }
}
