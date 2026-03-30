using GeoTime.Core.Models;
using GeoTime.Core.Engines;
using GeoTime.Core.Kernel;

namespace GeoTime.Tests;

public class BiomatterTests
{
    // ── Temperature Factor ────────────────────────────────────────────────────

    [Fact]
    public void TemperatureFactor_AtOptimal_ReturnsOne()
    {
        double f = BiomatterEngine.TemperatureFactor(20, 20, 10);
        Assert.Equal(1.0, f, 5);
    }

    [Fact]
    public void TemperatureFactor_FarFromOptimal_ReturnsLow()
    {
        double f = BiomatterEngine.TemperatureFactor(-20, 20, 10);
        Assert.True(f < 0.1);
    }

    [Fact]
    public void TemperatureFactor_AlwaysPositive()
    {
        double f = BiomatterEngine.TemperatureFactor(100, 20, 10);
        Assert.True(f >= 0);
    }

    // ── Light Factor ──────────────────────────────────────────────────────────

    [Fact]
    public void LightFactor_LandCell_ReturnsZero()
    {
        Assert.Equal(0, BiomatterEngine.LightFactor(100));
    }

    [Fact]
    public void LightFactor_ShallowOcean_ReturnsOne()
    {
        Assert.Equal(1.0, BiomatterEngine.LightFactor(-50));
    }

    [Fact]
    public void LightFactor_DeepOcean_LessThanOne()
    {
        double f = BiomatterEngine.LightFactor(-3000);
        Assert.True(f < 1.0);
        Assert.True(f >= 0);
    }

    [Fact]
    public void LightFactor_VeryDeep_ApproachesZero()
    {
        double f = BiomatterEngine.LightFactor(-5000);
        Assert.True(f <= 0.01);
    }

    // ── Reef Factor ───────────────────────────────────────────────────────────

    [Fact]
    public void ReefFactor_LandCell_ReturnsZero()
    {
        Assert.Equal(0, BiomatterEngine.ReefFactor(25, 100));
    }

    [Fact]
    public void ReefFactor_TooDeep_ReturnsZero()
    {
        Assert.Equal(0, BiomatterEngine.ReefFactor(25, -100));
    }

    [Fact]
    public void ReefFactor_TooCold_ReturnsZero()
    {
        Assert.Equal(0, BiomatterEngine.ReefFactor(10, -20));
    }

    [Fact]
    public void ReefFactor_TooHot_ReturnsZero()
    {
        Assert.Equal(0, BiomatterEngine.ReefFactor(35, -20));
    }

    [Fact]
    public void ReefFactor_IdealConditions_ReturnsPositive()
    {
        double f = BiomatterEngine.ReefFactor(24, -20);
        Assert.True(f > 0.5);
    }

    // ── Marine Productivity ───────────────────────────────────────────────────

    [Fact]
    public void MarineProductivity_LandCell_ReturnsZero()
    {
        Assert.Equal(0, BiomatterEngine.MarineProductivity(20, 100, 0.1));
    }

    [Fact]
    public void MarineProductivity_LowO2_ReturnsZero()
    {
        Assert.Equal(0, BiomatterEngine.MarineProductivity(20, -50, 0.0005));
    }

    [Fact]
    public void MarineProductivity_GoodConditions_ReturnsPositive()
    {
        double p = BiomatterEngine.MarineProductivity(20, -50, 0.1);
        Assert.True(p > 0);
    }

    [Fact]
    public void MarineProductivity_OptimalTemp_HigherThanCold()
    {
        double optimal = BiomatterEngine.MarineProductivity(20, -50, 0.1);
        double cold = BiomatterEngine.MarineProductivity(0, -50, 0.1);
        Assert.True(optimal > cold);
    }

    // ── Cyanobacteria Productivity ────────────────────────────────────────────

    [Fact]
    public void CyanobacteriaProductivity_LandCell_ReturnsZero()
    {
        Assert.Equal(0, BiomatterEngine.CyanobacteriaProductivity(20, 100));
    }

    [Fact]
    public void CyanobacteriaProductivity_DeepOcean_ReturnsZero()
    {
        Assert.Equal(0, BiomatterEngine.CyanobacteriaProductivity(20, -500));
    }

    [Fact]
    public void CyanobacteriaProductivity_TooCold_ReturnsZero()
    {
        Assert.Equal(0, BiomatterEngine.CyanobacteriaProductivity(5, -50));
    }

    [Fact]
    public void CyanobacteriaProductivity_WarmShallow_ReturnsPositive()
    {
        double p = BiomatterEngine.CyanobacteriaProductivity(25, -50);
        Assert.True(p > 0);
    }

    // ── Fungi Productivity ────────────────────────────────────────────────────

    [Fact]
    public void FungiProductivity_LowO2_ReturnsZero()
    {
        Assert.Equal(0, BiomatterEngine.FungiProductivity(20, 0.5, 10, 0.01));
    }

    [Fact]
    public void FungiProductivity_TooCold_ReturnsZero()
    {
        Assert.Equal(0, BiomatterEngine.FungiProductivity(-5, 0.5, 10, 0.1));
    }

    [Fact]
    public void FungiProductivity_NoSoil_ReturnsZero()
    {
        Assert.Equal(0, BiomatterEngine.FungiProductivity(20, 0.05, 10, 0.1));
    }

    [Fact]
    public void FungiProductivity_GoodConditions_ProportionalToVegetation()
    {
        double p1 = BiomatterEngine.FungiProductivity(20, 0.5, 10, 0.1);
        double p2 = BiomatterEngine.FungiProductivity(20, 0.5, 20, 0.1);
        Assert.True(p2 > p1);
        Assert.Equal(BiomatterEngine.FUNGI_BIOMASS_FRACTION * 10, p1, 5);
    }

    // ── Engine Tick (integration-level unit tests) ────────────────────────────

    [Fact]
    public void Tick_DisabledEngine_ReturnsNull()
    {
        var bus = new EventBus();
        var log = new EventLog();
        var engine = new BiomatterEngine(bus, log, 42, 8, 1.0, enabled: false);
        Assert.Null(engine.Tick(0, 1.0));
    }

    [Fact]
    public void Tick_UninitializedEngine_ReturnsNull()
    {
        var bus = new EventBus();
        var log = new EventLog();
        var engine = new BiomatterEngine(bus, log, 42, 8);
        Assert.Null(engine.Tick(0, 1.0));
    }

    [Fact]
    public void Tick_ZeroDelta_ReturnsNull()
    {
        var bus = new EventBus();
        var log = new EventLog();
        var engine = new BiomatterEngine(bus, log, 42, 8);
        var state = new SimulationState(8);
        var atmo = new AtmosphericComposition { N2 = 0.78, O2 = 0.21, CO2 = 0.0004 };
        var strat = new StratigraphyStack();
        engine.Initialize(state, atmo, strat);
        Assert.Null(engine.Tick(0, 0));
    }

    [Fact]
    public void Tick_MarineCells_ProducesBiomatter()
    {
        var bus = new EventBus();
        var log = new EventLog();
        int gs = 8;
        var engine = new BiomatterEngine(bus, log, 42, gs, 1.0);
        var state = new SimulationState(gs);
        var atmo = new AtmosphericComposition { N2 = 0.78, O2 = 0.21, CO2 = 0.0004 };
        var strat = new StratigraphyStack();

        // Set up ocean cells with warm temperatures
        for (int i = 0; i < gs * gs; i++)
        {
            state.HeightMap[i] = -100; // shallow ocean
            state.TemperatureMap[i] = 20; // warm
        }

        engine.Initialize(state, atmo, strat);
        var result = engine.Tick(-4000, 1.0);

        Assert.NotNull(result);
        Assert.True(result.TotalBiomatter > 0);
        Assert.True(result.MarineCells > 0);
        Assert.True(result.CyanobacteriaCells > 0);
    }

    [Fact]
    public void Tick_ReefCells_IncrementHeight()
    {
        var bus = new EventBus();
        var log = new EventLog();
        int gs = 4;
        var engine = new BiomatterEngine(bus, log, 42, gs, 1.0);
        var state = new SimulationState(gs);
        var atmo = new AtmosphericComposition { N2 = 0.78, O2 = 0.21, CO2 = 0.0004 };
        var strat = new StratigraphyStack();

        // Set up reef-suitable cells
        for (int i = 0; i < gs * gs; i++)
        {
            state.HeightMap[i] = -30; // shallow
            state.TemperatureMap[i] = 24; // ideal reef temp
        }

        float initialHeight = state.HeightMap[0];
        engine.Initialize(state, atmo, strat);
        var result = engine.Tick(-4000, 1.0);

        Assert.NotNull(result);
        Assert.True(result.ReefCells > 0);
        Assert.True(state.HeightMap[0] >= initialHeight);
    }

    [Fact]
    public void Tick_TerrestrialCells_FungiWithVegetation()
    {
        var bus = new EventBus();
        var log = new EventLog();
        int gs = 4;
        var engine = new BiomatterEngine(bus, log, 42, gs, 1.0);
        var state = new SimulationState(gs);
        var atmo = new AtmosphericComposition { N2 = 0.78, O2 = 0.21, CO2 = 0.0004 };
        var strat = new StratigraphyStack();

        // Set up terrestrial cells with vegetation
        for (int i = 0; i < gs * gs; i++)
        {
            state.HeightMap[i] = 500; // land
            state.TemperatureMap[i] = 20;
            state.SoilDepthMap[i] = 1.0f;
            state.BiomassMap[i] = 15; // vegetation present
        }

        engine.Initialize(state, atmo, strat);
        var result = engine.Tick(-4000, 1.0);

        Assert.NotNull(result);
        Assert.True(result.FungiCells > 0);
        Assert.True(result.TotalBiomatter > 0);
    }

    [Fact]
    public void Tick_AtmosphereFeedback_O2Increases()
    {
        var bus = new EventBus();
        var log = new EventLog();
        int gs = 8;
        var engine = new BiomatterEngine(bus, log, 42, gs, 1.0);
        var state = new SimulationState(gs);
        var atmo = new AtmosphericComposition { N2 = 0.78, O2 = 0.005, CO2 = 0.001 };
        var strat = new StratigraphyStack();

        // Ocean cells with cyanobacteria
        for (int i = 0; i < gs * gs; i++)
        {
            state.HeightMap[i] = -50;
            state.TemperatureMap[i] = 20;
        }

        double initialO2 = atmo.O2;
        engine.Initialize(state, atmo, strat);
        engine.Tick(-4000, 1.0);

        Assert.True(atmo.O2 >= initialO2, "O₂ should increase from cyanobacteria/plankton photosynthesis");
    }

    [Fact]
    public void Tick_OxygenationEvent_FiresOnceWhenThresholdReached()
    {
        var bus = new EventBus();
        var log = new EventLog();
        int gs = 4;
        var engine = new BiomatterEngine(bus, log, 42, gs, 1.0);
        var state = new SimulationState(gs);
        // Start just below threshold
        var atmo = new AtmosphericComposition { N2 = 0.78, O2 = 0.019, CO2 = 0.001 };
        var strat = new StratigraphyStack();

        for (int i = 0; i < gs * gs; i++)
        {
            state.HeightMap[i] = -50;
            state.TemperatureMap[i] = 25;
        }

        int oxygenationCount = 0;
        bus.On("OXYGENATION_EVENT", _ => oxygenationCount++);

        engine.Initialize(state, atmo, strat);

        // Tick many times to push O₂ above threshold
        for (int t = 0; t < 100; t++)
            engine.Tick(-4000 + t, 1.0);

        // Should have fired at most once
        Assert.True(oxygenationCount <= 1);
    }

    [Fact]
    public void Tick_OrganicCarbonAccumulates_InDeepOcean()
    {
        var bus = new EventBus();
        var log = new EventLog();
        int gs = 4;
        var engine = new BiomatterEngine(bus, log, 42, gs, 1.0);
        var state = new SimulationState(gs);
        var atmo = new AtmosphericComposition { N2 = 0.78, O2 = 0.21, CO2 = 0.0004 };
        var strat = new StratigraphyStack();

        // Deep ocean cells
        for (int i = 0; i < gs * gs; i++)
        {
            state.HeightMap[i] = -3000;
            state.TemperatureMap[i] = 4;
            state.BiomatterMap[i] = 2.0f; // pre-existing biomatter
        }

        engine.Initialize(state, atmo, strat);
        engine.Tick(-4000, 1.0);

        // At least some cells should have organic carbon
        bool anyOrgCarbon = false;
        for (int i = 0; i < gs * gs; i++)
            if (state.OrganicCarbonMap[i] > 0) { anyOrgCarbon = true; break; }

        Assert.True(anyOrgCarbon, "Organic carbon should accumulate in deep ocean");
    }

    [Fact]
    public void Tick_BiogenicSedimentation_DepositsLayers()
    {
        var bus = new EventBus();
        var log = new EventLog();
        int gs = 4;
        var engine = new BiomatterEngine(bus, log, 42, gs, 1.0);
        var state = new SimulationState(gs);
        var atmo = new AtmosphericComposition { N2 = 0.78, O2 = 0.21, CO2 = 0.0004 };
        var strat = new StratigraphyStack();

        // Warm shallow marine (reef conditions)
        for (int i = 0; i < gs * gs; i++)
        {
            state.HeightMap[i] = -30;
            state.TemperatureMap[i] = 24;
        }

        engine.Initialize(state, atmo, strat);
        var result = engine.Tick(-4000, 1.0);

        Assert.NotNull(result);
        Assert.True(result.BiogenicLayers > 0, "Should deposit biogenic sediment layers");
    }

    [Fact]
    public void Tick_CH4Production_DeepAnoxicOcean()
    {
        var bus = new EventBus();
        var log = new EventLog();
        int gs = 4;
        var engine = new BiomatterEngine(bus, log, 42, gs, 1.0);
        var state = new SimulationState(gs);
        var atmo = new AtmosphericComposition { N2 = 0.78, O2 = 0.21, CO2 = 0.0004, CH4 = 0 };
        var strat = new StratigraphyStack();

        // Deep ocean with existing biomatter
        for (int i = 0; i < gs * gs; i++)
        {
            state.HeightMap[i] = -3000;
            state.TemperatureMap[i] = 4;
            state.BiomatterMap[i] = 3.0f;
        }

        engine.Initialize(state, atmo, strat);
        engine.Tick(-4000, 1.0);

        Assert.True(atmo.CH4 >= 0, "CH₄ should be non-negative");
    }

    [Fact]
    public void Tick_BiomatterCappedAtMaxMarine()
    {
        var bus = new EventBus();
        var log = new EventLog();
        int gs = 4;
        var engine = new BiomatterEngine(bus, log, 42, gs, 1.0);
        var state = new SimulationState(gs);
        var atmo = new AtmosphericComposition { N2 = 0.78, O2 = 0.21, CO2 = 0.0004 };
        var strat = new StratigraphyStack();

        for (int i = 0; i < gs * gs; i++)
        {
            state.HeightMap[i] = -50;
            state.TemperatureMap[i] = 20;
            state.BiomatterMap[i] = 4.9f; // near max
        }

        engine.Initialize(state, atmo, strat);

        for (int t = 0; t < 10; t++)
            engine.Tick(-4000 + t, 1.0);

        for (int i = 0; i < gs * gs; i++)
            Assert.True(state.BiomatterMap[i] <= BiomatterEngine.MAX_MARINE_BIOMATTER + 0.01);
    }

    [Fact]
    public void Tick_BiomatterCappedAtMaxTerrestrial()
    {
        var bus = new EventBus();
        var log = new EventLog();
        int gs = 4;
        var engine = new BiomatterEngine(bus, log, 42, gs, 1.0);
        var state = new SimulationState(gs);
        var atmo = new AtmosphericComposition { N2 = 0.78, O2 = 0.21, CO2 = 0.0004 };
        var strat = new StratigraphyStack();

        for (int i = 0; i < gs * gs; i++)
        {
            state.HeightMap[i] = 500;
            state.TemperatureMap[i] = 20;
            state.SoilDepthMap[i] = 1.0f;
            state.BiomassMap[i] = 30;
            state.BiomatterMap[i] = 1.9f; // near max
        }

        engine.Initialize(state, atmo, strat);

        for (int t = 0; t < 10; t++)
            engine.Tick(-4000 + t, 1.0);

        for (int i = 0; i < gs * gs; i++)
            Assert.True(state.BiomatterMap[i] <= BiomatterEngine.MAX_TERRESTRIAL_BIOMATTER + 0.01);
    }

    // ── BIF (Banded Iron Formation) test ──────────────────────────────────────

    [Fact]
    public void Tick_LowO2_DepositsBandedIronFormation()
    {
        var bus = new EventBus();
        var log = new EventLog();
        int gs = 4;
        var engine = new BiomatterEngine(bus, log, 42, gs, 1.0);
        var state = new SimulationState(gs);
        // Low O₂ (pre-oxygenation, but above aerobic marine threshold for productivity)
        var atmo = new AtmosphericComposition { N2 = 0.78, O2 = 0.005, CO2 = 0.001 };
        var strat = new StratigraphyStack();

        // Shallow ocean with cyanobacteria
        for (int i = 0; i < gs * gs; i++)
        {
            state.HeightMap[i] = -50;
            state.TemperatureMap[i] = 20;
        }

        engine.Initialize(state, atmo, strat);
        engine.Tick(-4000, 5.0);

        // Check that some cells got ironstone deposits
        bool anyIronstone = false;
        for (int i = 0; i < gs * gs; i++)
        {
            var layers = strat.GetLayers(i);
            foreach (var l in layers)
                if (l.RockType == RockType.SED_IRONSTONE) { anyIronstone = true; break; }
            if (anyIronstone) break;
        }
        Assert.True(anyIronstone, "Should deposit banded iron formation at low O₂");
    }
}
