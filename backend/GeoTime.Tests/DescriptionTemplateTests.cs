using GeoTime.Core;
using GeoTime.Core.Engines;
using GeoTime.Core.Models;
using GeoTime.Core.Services;

namespace GeoTime.Tests;

/// <summary>Tests for the Phase D4 DescriptionTemplateEngine and DescriptionPromptComposer.</summary>
public class DescriptionTemplateTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Helper: create a small planet and assemble context for a given cell index.
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<GeologicalContext?> AssembleContext(
        uint seed = 42u, int gs = 32, int? forceCellIndex = null)
    {
        var orchestrator = new SimulationOrchestrator(gs);
        orchestrator.GeneratePlanet(seed);
        orchestrator.AdvanceSimulation(0.5);
        var assembler = new GeologicalContextAssembler(orchestrator);

        // Use specified cell or find a land cell with a mountain or a river
        if (forceCellIndex.HasValue)
            return await assembler.AssembleAsync(forceCellIndex.Value);

        for (int i = 0; i < orchestrator.State.CellCount; i++)
        {
            var ctx = await assembler.AssembleAsync(i);
            if (ctx != null) return ctx;
        }
        return null;
    }

    private static async Task<GeologicalContext?> AssembleContextWithSubduction(uint seed = 42u, int gs = 32)
    {
        var orchestrator = new SimulationOrchestrator(gs);
        orchestrator.GeneratePlanet(seed);
        orchestrator.AdvanceSimulation(2.0);
        var assembler = new GeologicalContextAssembler(orchestrator);

        for (int i = 0; i < orchestrator.State.CellCount; i++)
        {
            var ctx = await assembler.AssembleAsync(i);
            if (ctx?.NearestMarginType == BoundaryType.CONVERGENT || ctx?.SubductingPlate != null)
                return ctx;
        }
        return null;
    }

    private static async Task<GeologicalContext?> AssembleContextInMountainRange(uint seed = 42u, int gs = 32)
    {
        var orchestrator = new SimulationOrchestrator(gs);
        orchestrator.GeneratePlanet(seed);
        orchestrator.AdvanceSimulation(2.0);
        var assembler = new GeologicalContextAssembler(orchestrator);

        for (int i = 0; i < orchestrator.State.CellCount; i++)
        {
            var ctx = await assembler.AssembleAsync(i);
            if (ctx?.IsInMountainRange == true && ctx.RangeName != null)
                return ctx;
        }
        return null;
    }

    private static async Task<GeologicalContext?> AssembleContextWithRiver(uint seed = 42u, int gs = 32)
    {
        var orchestrator = new SimulationOrchestrator(gs);
        orchestrator.GeneratePlanet(seed);
        orchestrator.AdvanceSimulation(2.0);
        var assembler = new GeologicalContextAssembler(orchestrator);

        for (int i = 0; i < orchestrator.State.CellCount; i++)
        {
            var ctx = await assembler.AssembleAsync(i);
            if (ctx?.RiverName != null)
                return ctx;
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4.1 — DescriptionPromptComposer
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ComposeSystemPrompt_ContainsPlanetGeologistPersona()
    {
        var systemPrompt = DescriptionPromptComposer.ComposeSystemPrompt();

        Assert.False(string.IsNullOrWhiteSpace(systemPrompt));
        Assert.Contains("planetary geologist", systemPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ComposeUserPrompt_ContainsJsonContextBlock()
    {
        var ctx = await AssembleContext();
        Assert.NotNull(ctx);

        var userPrompt = DescriptionPromptComposer.ComposeUserPrompt(ctx);

        Assert.False(string.IsNullOrWhiteSpace(userPrompt));
        Assert.Contains("GEOLOGICAL CONTEXT", userPrompt, StringComparison.OrdinalIgnoreCase);
        // Should contain tectonic data
        Assert.Contains("tectonic", userPrompt, StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4.2 — DescriptionTemplateEngine
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Generate_SubductionContext_ContainsSubductionText()
    {
        var ctx = await AssembleContextWithSubduction(seed: 42u);
        if (ctx == null)
        {
            // Fallback: build a minimal context that has subduction set
            ctx = await AssembleContext(seed: 99u);
            Assert.NotNull(ctx);
        }

        // Force a context that has SubductingPlate set by creating one manually
        var manualCtx = new GeologicalContext
        {
            Lat = 30f, Lon = 60f, CurrentTick = 1, SimAgeDescription = "~500 million years",
            NearestMarginType = BoundaryType.CONVERGENT,
            SubductingPlate   = new PlateInfo { Id = 3, IsOceanic = true },
            CurrentPlate      = new PlateInfo { Id = 1, IsOceanic = false },
            Cell              = new CellInspection(),
            BiomeType         = "Temperate Forest",
            MeanTempC         = 12f,
            MeanPrecipMm      = 800f,
            MountainOriginType = "volcanic arc",
            PrimaryLandFeature = MakeMountainRangeFeature("Mt. Subductus"),
        };

        var paras = DescriptionTemplateEngine.Generate(manualCtx);

        Assert.NotEmpty(paras);
        var combined = string.Join(" ", paras);
        Assert.Contains("subduction", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3", combined); // SubductingPlate.Id
    }

    [Fact]
    public void Generate_ImpactEjectaLayer_MentionsImpactInStratigraphy()
    {
        var ctx = new GeologicalContext
        {
            Lat = 0f, Lon = 0f, CurrentTick = 5, SimAgeDescription = "~100 million years",
            NearestMarginType = BoundaryType.NONE,
            CurrentPlate = new PlateInfo { Id = 1 },
            Cell = new CellInspection { Height = 500f },
            BiomeType = "Grassland / Steppe",
            MeanTempC = 15f,
            MeanPrecipMm = 400f,
            ExtraordinaryLayers =
            [
                new StratigraphicLayer
                {
                    EventType  = LayerEventType.ImpactEjecta,
                    EventId    = "IMPACT_42",
                    Thickness  = 0.5f,
                    IsGlobal   = false,
                },
            ],
        };

        var paras = DescriptionTemplateEngine.Generate(ctx);
        var combined = string.Join(" ", paras);

        Assert.Contains("impact", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IMPACT_42", combined);
    }

    [Fact]
    public void Generate_RainShadowContext_MentionsRainShadow()
    {
        var ctx = new GeologicalContext
        {
            Lat = 40f, Lon = -110f, CurrentTick = 10, SimAgeDescription = "~200 million years",
            NearestMarginType = BoundaryType.CONVERGENT,
            CurrentPlate = new PlateInfo { Id = 2 },
            Cell = new CellInspection { Height = 3500f },
            BiomeType = "Alpine",
            MeanTempC = -5f,
            MeanPrecipMm = 1200f,
            IsInMountainRange = true,
            RangeName = "Rainshadow Range",
            HasRainShadow = true,
            IsOnWindwardSide = false,
            MountainOriginType = "fold-belt",
            PrimaryLandFeature = MakeMountainRangeFeature("Rainshadow Range"),
        };

        var paras = DescriptionTemplateEngine.Generate(ctx);
        var combined = string.Join(" ", paras);

        Assert.Contains("rain shadow", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("leeward", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Generate_RiverContext_ContainsRiverNameAndOutletAndDelta()
    {
        var ctx = await AssembleContextWithRiver(seed: 42u);
        if (ctx == null)
        {
            // Build a synthetic river context
            ctx = new GeologicalContext
            {
                Lat = 20f, Lon = 80f, CurrentTick = 3, SimAgeDescription = "~50 million years",
                NearestMarginType = BoundaryType.NONE,
                CurrentPlate = new PlateInfo { Id = 1 },
                Cell = new CellInspection { Height = 150f },
                BiomeType = "Tropical Seasonal Forest",
                MeanTempC = 25f,
                MeanPrecipMm = 1500f,
                RiverName = "Arkaros",
                RiverLengthKm = 2500f,
                CatchmentAreaKm2 = 450000f,
                RiverOutletOcean = "Veritian Ocean",
                DrainageGradient = 0.8f,
                PrimaryLandFeature = MakeRiverFeature("Arkaros"),
            };
        }

        var paras = DescriptionTemplateEngine.Generate(ctx);
        var combined = string.Join(" ", paras);

        // Must contain the river name
        Assert.Contains(ctx.RiverName!, combined, StringComparison.OrdinalIgnoreCase);

        // Must contain outlet ocean if set
        if (ctx.RiverOutletOcean != null)
            Assert.Contains(ctx.RiverOutletOcean, combined, StringComparison.OrdinalIgnoreCase);

        // Must mention drainage / channel concept
        Assert.True(
            combined.Contains("delta", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("gradient", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("drain", StringComparison.OrdinalIgnoreCase),
            "Expected delta/gradient/drain in output");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static DetectedFeature MakeMountainRangeFeature(string name) => new()
    {
        Id   = "feat_mtn",
        Type = FeatureType.MountainRange,
        History = [new FeatureSnapshot(0, -1, name, 40f, -110f, 50000f, FeatureStatus.Active, null, null, null)],
    };

    private static DetectedFeature MakeRiverFeature(string name) => new()
    {
        Id   = "feat_river",
        Type = FeatureType.River,
        History = [new FeatureSnapshot(0, -1, name, 20f, 80f, 500f, FeatureStatus.Active, null, null, null)],
    };
}
