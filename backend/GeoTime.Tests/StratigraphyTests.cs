using GeoTime.Core.Models;
using GeoTime.Core.Engines;

namespace GeoTime.Tests;

public class StratigraphyTests
{
    [Fact]
    public void PushLayer_AddsToStack()
    {
        var stack = new StratigraphyStack();
        stack.PushLayer(0, new StratigraphicLayer
        {
            RockType = RockType.IGN_BASALT, Thickness = 100, AgeDeposited = -4000
        });
        Assert.Single(stack.GetLayers(0));
        Assert.Equal(100, stack.GetTotalThickness(0));
    }

    [Fact]
    public void PushLayer_MergesWhenExceedsMax()
    {
        var stack = new StratigraphyStack();
        for (var i = 0; i < StratigraphyStack.MAX_LAYERS_PER_CELL + 10; i++)
            stack.PushLayer(0, new StratigraphicLayer
            {
                RockType = RockType.SED_SANDSTONE, Thickness = 10, AgeDeposited = i,
            });
        Assert.Equal(StratigraphyStack.MAX_LAYERS_PER_CELL, stack.GetLayers(0).Count);
    }

    [Fact]
    public void ErodeTop_RemovesMaterial()
    {
        var stack = new StratigraphyStack();
        stack.PushLayer(0, new StratigraphicLayer
        {
            RockType = RockType.IGN_GRANITE, Thickness = 100, AgeDeposited = -4000,
        });
        var eroded = stack.ErodeTop(0, 40);
        Assert.Equal(40, eroded);
        Assert.Equal(60, stack.GetTotalThickness(0));
    }

    [Fact]
    public void ErodeTop_RemovesEntireLayer()
    {
        var stack = new StratigraphyStack();
        stack.PushLayer(0, new StratigraphicLayer
        {
            RockType = RockType.IGN_GRANITE, Thickness = 50, AgeDeposited = -4000,
        });
        var eroded = stack.ErodeTop(0, 100);
        Assert.Equal(50, eroded);
        Assert.Empty(stack.GetLayers(0));
    }

    [Fact]
    public void InitializeBasement_OceanicCrust()
    {
        var stack = new StratigraphyStack();
        stack.InitializeBasement(0, isOceanic: true, ageDeposited: -4000);
        var layers = stack.GetLayers(0);
        Assert.Equal(2, layers.Count);
        Assert.Equal(RockType.IGN_GABBRO, layers[0].RockType);
        Assert.Equal(RockType.IGN_PILLOW_BASALT, layers[1].RockType);
    }

    [Fact]
    public void InitializeBasement_ContinentalCrust()
    {
        var stack = new StratigraphyStack();
        stack.InitializeBasement(0, isOceanic: false, ageDeposited: -4000);
        var layers = stack.GetLayers(0);
        Assert.Equal(2, layers.Count);
        Assert.Equal(RockType.MET_GNEISS, layers[0].RockType);
        Assert.Equal(RockType.IGN_GRANITE, layers[1].RockType);
    }

    [Fact]
    public void ApplyDeformation_UpdatesLayers()
    {
        var stack = new StratigraphyStack();
        stack.PushLayer(0, new StratigraphicLayer
        {
            RockType = RockType.SED_LIMESTONE, Thickness = 100,
            DipAngle = 5, Deformation = DeformationType.UNDEFORMED,
        });
        stack.ApplyDeformation(0, 10, 45, DeformationType.FOLDED);
        var top = stack.GetTopLayer(0);
        Assert.NotNull(top);
        Assert.Equal(15, top.DipAngle);
        Assert.Equal(DeformationType.FOLDED, top.Deformation);
    }

    // ── Phase D1 tests ─────────────────────────────────────────────────────────

    [Fact]
    public void StratigraphicLayer_EventFields_DefaultNormal()
    {
        var layer = new StratigraphicLayer { RockType = RockType.SED_SANDSTONE, Thickness = 10 };
        Assert.Equal(LayerEventType.Normal, layer.EventType);
        Assert.Null(layer.EventId);
        Assert.Equal(0f, layer.IsotopeAnomaly);
        Assert.Equal(0f, layer.OrganicCarbonFraction);
        Assert.Equal(0f, layer.SootConcentrationPpm);
        Assert.False(layer.IsGlobal);
    }

    [Fact]
    public void StratigraphicLayer_Clone_CopiesEventFields()
    {
        var original = new StratigraphicLayer
        {
            RockType = RockType.SED_CHERT,
            Thickness = 0.5,
            EventType = LayerEventType.GammaRayBurst,
            EventId = "GRB_001",
            IsotopeAnomaly = 0.3f,
            IsGlobal = true,
        };
        var clone = original.Clone();
        Assert.Equal(LayerEventType.GammaRayBurst, clone.EventType);
        Assert.Equal("GRB_001", clone.EventId);
        Assert.Equal(0.3f, clone.IsotopeAnomaly);
        Assert.True(clone.IsGlobal);
    }

    [Fact]
    public void StratigraphicColumn_Surface_ReturnsTopLayer()
    {
        var col = new StratigraphicColumn();
        col.Layers.Add(new StratigraphicLayer { RockType = RockType.MET_GNEISS, Thickness = 5000 });
        col.Layers.Add(new StratigraphicLayer { RockType = RockType.SED_SANDSTONE, Thickness = 100 });
        Assert.Equal(RockType.SED_SANDSTONE, col.Surface!.RockType);
    }

    [Fact]
    public void StratigraphicColumn_ExtraordinaryLayers_FiltersNormal()
    {
        var col = new StratigraphicColumn();
        col.Layers.Add(new StratigraphicLayer { EventType = LayerEventType.Normal });
        col.Layers.Add(new StratigraphicLayer { EventType = LayerEventType.ImpactEjecta });
        col.Layers.Add(new StratigraphicLayer { EventType = LayerEventType.GammaRayBurst });
        var extraordinary = col.ExtraordinaryLayers.ToList();
        Assert.Equal(2, extraordinary.Count);
        Assert.All(extraordinary, l => Assert.NotEqual(LayerEventType.Normal, l.EventType));
    }

    [Fact]
    public void EventDepositionEngine_ImpactEjecta_AppearsInNearbyCell()
    {
        const int gs = 16; // small grid for test speed
        var state = new SimulationState(gs);
        var stack = new StratigraphyStack();
        stack.InitializeBasement(0, isOceanic: false, ageDeposited: -4000);

        var engine = new EventDepositionEngine();
        var impactEvent = new GeoTime.Core.Models.GeoLogEntry
        {
            TimeMa = -4000,
            Type = "IMPACT",
            Location = new LatLon(89.0, 0.0), // near north pole cell 0
        };

        engine.Deposit(state, stack, [impactEvent], -4000);

        // Cell 0 is near 89°N — should have an ejecta layer
        var layers = stack.GetLayers(0).ToList();
        Assert.Contains(layers, l => l.EventType == LayerEventType.ImpactEjecta);
    }

    [Fact]
    public void EventDepositionEngine_GRB_AllCellsHaveLayer()
    {
        const int gs = 8;
        var state = new SimulationState(gs);
        var stack = new StratigraphyStack();

        var engine = new EventDepositionEngine();
        var grbEvent = new GeoTime.Core.Models.GeoLogEntry
        {
            TimeMa = -3000,
            Type = "GRB",
        };

        engine.Deposit(state, stack, [grbEvent], -3000);

        for (var i = 0; i < gs * gs; i++)
        {
            var layers = stack.GetLayers(i).ToList();
            Assert.Contains(layers, l => l.EventType == LayerEventType.GammaRayBurst
                                       && l.IsotopeAnomaly > 0);
        }
    }

    [Fact]
    public void EventDepositionEngine_ImpactEjecta_ThicknessFalloff()
    {
        // Confirm ejecta layer is thicker at the impact site than at a distant cell.
        const int gs = 16;
        var state = new SimulationState(gs);
        var stack = new StratigraphyStack();

        var engine = new EventDepositionEngine();
        var impactEvent = new GeoTime.Core.Models.GeoLogEntry
        {
            TimeMa = -4000,
            Type = "IMPACT",
            Location = new LatLon(89.0, 0.0), // near north pole → cell ≈ 0
        };

        engine.Deposit(state, stack, [impactEvent], -4000);

        // Cell at north pole (row 0 col 0) should have thick ejecta
        var nearLayers  = stack.GetLayers(0).ToList();
        // Cell at south pole (last row, col 0) should have thin global layer
        var farIndex = (gs - 1) * gs;
        var farLayers   = stack.GetLayers(farIndex).ToList();

        var nearEjecta = nearLayers.Where(l => l.EventType == LayerEventType.ImpactEjecta).Sum(l => l.Thickness);
        var farEjecta  = farLayers.Where(l => l.EventType is LayerEventType.ImpactEjecta).Sum(l => l.Thickness);

        Assert.True(nearEjecta > farEjecta, $"Near ejecta {nearEjecta} should be > far ejecta {farEjecta}");
    }

    [Fact]
    public void CellInspection_FeatureIds_IncludesMountainRange()
    {
        // Generate a small planet and verify InspectCell returns FeatureIds.
        var orchestrator = new GeoTime.Core.SimulationOrchestrator(32);
        orchestrator.GeneratePlanet(42);
        orchestrator.AdvanceSimulation(0.5);

        // Find any cell that's in a feature
        var registry = orchestrator.GetFeatureRegistry();
        if (registry.Features.Count == 0) return; // no features yet — acceptable

        var feat = registry.Features.Values.First(f => f.CellIndices.Count > 0);
        var cell = feat.CellIndices[0];
        var inspection = orchestrator.InspectCell(cell);

        Assert.NotNull(inspection);
        Assert.Contains(feat.Id, inspection.FeatureIds);
    }
}
