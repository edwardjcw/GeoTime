using GeoTime.Core;
using GeoTime.Core.Engines;
using GeoTime.Core.Models;
using GeoTime.Core.Services;

namespace GeoTime.Tests;

/// <summary>Tests for the Phase D2 GeologicalContextAssembler.</summary>
public class ContextAssemblerTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Helper: create a 32×32 planet, advance once, return orchestrator.
    // ─────────────────────────────────────────────────────────────────────────

    private static SimulationOrchestrator CreatePlanet(uint seed = 42u, int gs = 32)
    {
        var orchestrator = new SimulationOrchestrator(gs);
        orchestrator.GeneratePlanet(seed);
        orchestrator.AdvanceSimulation(0.5);
        return orchestrator;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Basic plumbing
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AssembleAsync_ReturnsNullForOutOfRangeCellIndex()
    {
        var orchestrator = CreatePlanet();
        var assembler    = new GeologicalContextAssembler(orchestrator);

        var ctx = await assembler.AssembleAsync(-1);
        Assert.Null(ctx);

        ctx = await assembler.AssembleAsync(orchestrator.State.CellCount);
        Assert.Null(ctx);
    }

    [Fact]
    public async Task AssembleAsync_ReturnsContextForValidCell()
    {
        var orchestrator = CreatePlanet();
        var assembler    = new GeologicalContextAssembler(orchestrator);

        var ctx = await assembler.AssembleAsync(0);
        Assert.NotNull(ctx);
    }

    [Fact]
    public async Task AssembleAsync_LatLonMatchCellIndex()
    {
        var orchestrator = CreatePlanet();
        var assembler    = new GeologicalContextAssembler(orchestrator);
        var gs           = orchestrator.State.GridSize;

        const int cellIndex = 100;
        int cellRow = cellIndex / gs;
        int cellCol = cellIndex % gs;
        float expectedLat = (float)(90.0 - (cellRow + 0.5) * 180.0 / gs);
        float expectedLon = (float)((cellCol + 0.5) * 360.0 / gs - 180.0);

        var ctx = await assembler.AssembleAsync(cellIndex);
        Assert.NotNull(ctx);
        Assert.Equal(expectedLat, ctx.Lat, 3);
        Assert.Equal(expectedLon, ctx.Lon, 3);
    }

    [Fact]
    public async Task AssembleAsync_SimAgeDescriptionIsPopulated()
    {
        var orchestrator = CreatePlanet();
        var assembler    = new GeologicalContextAssembler(orchestrator);

        var ctx = await assembler.AssembleAsync(0);
        Assert.NotNull(ctx);
        Assert.NotEmpty(ctx.SimAgeDescription);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tectonic context
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AssembleAsync_CurrentPlateIdMatchesCellPlateId()
    {
        var orchestrator = CreatePlanet();
        var assembler    = new GeologicalContextAssembler(orchestrator);

        int cellIndex = orchestrator.State.CellCount / 2;
        var ctx = await assembler.AssembleAsync(cellIndex);
        Assert.NotNull(ctx);
        Assert.Equal(ctx.Cell.PlateId, ctx.CurrentPlate.Id);
    }

    [Fact]
    public async Task AssembleAsync_ConvergentBoundaryCell_HasCorrectMarginType()
    {
        var orchestrator = CreatePlanet(seed: 99u, gs: 32);
        var assembler    = new GeologicalContextAssembler(orchestrator);
        var state        = orchestrator.State;
        var plates       = orchestrator.GetPlates()?.ToList() ?? [];
        var gs           = state.GridSize;

        if (plates.Count < 2) return;

        var boundaries = BoundaryClassifier.Classify(state.PlateMap, plates, gs);
        var convergent = boundaries.FirstOrDefault(b => b.Type == BoundaryType.CONVERGENT);
        if (convergent == null) return;

        var ctx = await assembler.AssembleAsync(convergent.CellIndex);
        Assert.NotNull(ctx);
        Assert.Equal(BoundaryType.CONVERGENT, ctx.NearestMarginType);
    }

    [Fact]
    public async Task AssembleAsync_NearSubductionZone_SubductingPlatePopulated()
    {
        var orchestrator = CreatePlanet(seed: 1u, gs: 32);
        var assembler    = new GeologicalContextAssembler(orchestrator);
        var state        = orchestrator.State;
        var plates       = orchestrator.GetPlates()?.ToList() ?? [];
        var gs           = state.GridSize;

        if (plates.Count < 2) return;

        var boundaries = BoundaryClassifier.Classify(state.PlateMap, plates, gs);
        foreach (var b in boundaries)
        {
            if (b.Type != BoundaryType.CONVERGENT) continue;
            int plateId1 = b.Plate1, plateId2 = b.Plate2;
            if (plateId1 >= plates.Count || plateId2 >= plates.Count) continue;
            var p1 = plates[plateId1];
            var p2 = plates[plateId2];
            if (!(p1.IsOceanic ^ p2.IsOceanic)) continue;

            var ctx = await assembler.AssembleAsync(b.CellIndex);
            Assert.NotNull(ctx);
            Assert.True(ctx.SubductingPlate != null || ctx.CollidingPlate != null,
                "Expected a converging neighbour plate near a mixed-type boundary");
            return;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Feature context
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AssembleAsync_ContainingFeaturesIncludesCellFeatures()
    {
        var orchestrator = CreatePlanet();
        var assembler    = new GeologicalContextAssembler(orchestrator);
        var registry     = orchestrator.GetFeatureRegistry();

        var feat = registry.Features.Values.FirstOrDefault(f => f.CellIndices.Count > 0);
        if (feat == null) return;

        int cellIndex = feat.CellIndices[0];
        var ctx = await assembler.AssembleAsync(cellIndex);
        Assert.NotNull(ctx);
        Assert.Contains(feat.Id, ctx.ContainingFeatures.Select(f => f.Id));
    }

    [Fact]
    public async Task AssembleAsync_ContainingFeaturesSortedByScale()
    {
        var orchestrator = CreatePlanet();
        var assembler    = new GeologicalContextAssembler(orchestrator);
        var registry     = orchestrator.GetFeatureRegistry();

        int? targetCell = null;
        foreach (var feat in registry.Features.Values)
        {
            if (feat.Type != FeatureType.TectonicPlate) continue;
            foreach (int ci in feat.CellIndices)
            {
                bool hasSmaller = registry.Features.Values
                    .Any(f => f.Type != FeatureType.TectonicPlate && f.CellIndices.Contains(ci));
                if (hasSmaller) { targetCell = ci; break; }
            }
            if (targetCell.HasValue) break;
        }
        if (!targetCell.HasValue) return;

        var ctx = await assembler.AssembleAsync(targetCell.Value);
        Assert.NotNull(ctx);
        Assert.True(ctx.ContainingFeatures.Count >= 2);
        Assert.Equal(FeatureType.TectonicPlate, ctx.ContainingFeatures[0].Type);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Extraordinary layers
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AssembleAsync_CellWithImpactEjectaLayer_ExtraordinaryLayersNonEmpty()
    {
        // Construct a GeologicalContext column manually for isolated layer testing.
        var col = new StratigraphicColumn();
        col.Layers.Add(new StratigraphicLayer { RockType = RockType.MET_GNEISS, Thickness = 5000 });
        col.Layers.Add(new StratigraphicLayer
        {
            RockType  = RockType.SED_CHERT,
            Thickness = 0.5,
            EventType = LayerEventType.ImpactEjecta,
            EventId   = "IMPACT_001",
            IsGlobal  = true,
        });

        var extraordinary = col.ExtraordinaryLayers.ToList();
        Assert.NotEmpty(extraordinary);
        Assert.Contains(extraordinary, l => l.EventType == LayerEventType.ImpactEjecta);
    }

    [Fact]
    public async Task AssembleAsync_ExtraordinaryLayersMatchColumnFilter()
    {
        var orchestrator = CreatePlanet();
        var assembler    = new GeologicalContextAssembler(orchestrator);

        var ctx = await assembler.AssembleAsync(0);
        Assert.NotNull(ctx);

        Assert.All(ctx.ExtraordinaryLayers,
            l => Assert.NotEqual(LayerEventType.Normal, l.EventType));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Hydrological context
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AssembleAsync_CellInRiver_RiverNamePopulated()
    {
        var orchestrator = CreatePlanet(seed: 42u, gs: 32);
        var assembler    = new GeologicalContextAssembler(orchestrator);
        var registry     = orchestrator.GetFeatureRegistry();

        var riverFeature = registry.Features.Values
            .FirstOrDefault(f => f.Type == FeatureType.River && f.CellIndices.Count > 0);
        if (riverFeature == null) return;

        int cellIndex = riverFeature.CellIndices[0];
        var ctx = await assembler.AssembleAsync(cellIndex);
        Assert.NotNull(ctx);
        Assert.NotNull(ctx.RiverName);
        Assert.Equal(riverFeature.Current.Name, ctx.RiverName);
    }

    [Fact]
    public async Task AssembleAsync_CellInRiverWithMetrics_RiverLengthKmPositive()
    {
        var orchestrator = CreatePlanet(seed: 42u, gs: 32);
        var assembler    = new GeologicalContextAssembler(orchestrator);
        var registry     = orchestrator.GetFeatureRegistry();

        var riverFeature = registry.Features.Values
            .FirstOrDefault(f => f.Type == FeatureType.River
                                 && f.CellIndices.Count > 0
                                 && f.Metrics.ContainsKey("river_length_km"));
        if (riverFeature == null) return;

        int cellIndex = riverFeature.CellIndices[0];
        var ctx = await assembler.AssembleAsync(cellIndex);
        Assert.NotNull(ctx);
        Assert.NotNull(ctx.RiverLengthKm);
        Assert.True(ctx.RiverLengthKm > 0, $"Expected RiverLengthKm > 0, got {ctx.RiverLengthKm}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Mountain context
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AssembleAsync_CellInMountainRange_IsInMountainRangeTrue()
    {
        var orchestrator = CreatePlanet(seed: 42u, gs: 32);
        var assembler    = new GeologicalContextAssembler(orchestrator);
        var registry     = orchestrator.GetFeatureRegistry();

        var mountainFeature = registry.Features.Values
            .FirstOrDefault(f => f.Type == FeatureType.MountainRange && f.CellIndices.Count > 0);
        if (mountainFeature == null) return;

        int cellIndex = mountainFeature.CellIndices[0];
        var ctx = await assembler.AssembleAsync(cellIndex);
        Assert.NotNull(ctx);
        Assert.True(ctx.IsInMountainRange);
        Assert.NotNull(ctx.RangeName);
        Assert.Equal(mountainFeature.Current.Name, ctx.RangeName);
    }

    [Fact]
    public async Task AssembleAsync_CellOutsideMountainRange_IsInMountainRangeFalse()
    {
        var orchestrator = CreatePlanet();
        var assembler    = new GeologicalContextAssembler(orchestrator);
        var registry     = orchestrator.GetFeatureRegistry();

        int oceanCell = -1;
        for (int i = 0; i < orchestrator.State.CellCount; i++)
        {
            if (orchestrator.State.HeightMap[i] < -500f)
            {
                bool inMountain = registry.Features.Values
                    .Any(f => f.Type == FeatureType.MountainRange && f.CellIndices.Contains(i));
                if (!inMountain) { oceanCell = i; break; }
            }
        }
        if (oceanCell < 0) return;

        var ctx = await assembler.AssembleAsync(oceanCell);
        Assert.NotNull(ctx);
        Assert.False(ctx.IsInMountainRange);
        Assert.Null(ctx.RangeName);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Nearby features
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AssembleAsync_NearbyFeaturesAtMostSix()
    {
        var orchestrator = CreatePlanet();
        var assembler    = new GeologicalContextAssembler(orchestrator);

        var ctx = await assembler.AssembleAsync(0);
        Assert.NotNull(ctx);
        Assert.True(ctx.NearbyFeatures.Count <= 6,
            $"NearbyFeatures should be at most 6, got {ctx.NearbyFeatures.Count}");
    }

    [Fact]
    public async Task AssembleAsync_NearbyFeaturesDoNotContainSelf()
    {
        var orchestrator = CreatePlanet();
        var assembler    = new GeologicalContextAssembler(orchestrator);
        var registry     = orchestrator.GetFeatureRegistry();

        var feat = registry.Features.Values.FirstOrDefault(f => f.CellIndices.Count > 0);
        if (feat == null) return;
        int cellIndex = feat.CellIndices[0];

        var ctx = await assembler.AssembleAsync(cellIndex);
        Assert.NotNull(ctx);

        var containingIds = ctx.ContainingFeatures.Select(f => f.Id).ToHashSet();
        foreach (var (nearby, _) in ctx.NearbyFeatures)
            Assert.DoesNotContain(nearby.Id, containingIds);
    }

    [Fact]
    public async Task AssembleAsync_NearbyFeaturesOrderedByDistanceAscending()
    {
        var orchestrator = CreatePlanet();
        var assembler    = new GeologicalContextAssembler(orchestrator);

        var ctx = await assembler.AssembleAsync(0);
        Assert.NotNull(ctx);

        for (int i = 1; i < ctx.NearbyFeatures.Count; i++)
        {
            Assert.True(ctx.NearbyFeatures[i].DistanceKm >= ctx.NearbyFeatures[i - 1].DistanceKm,
                "NearbyFeatures should be ordered by distance ascending");
        }
    }
}
