using GeoTime.Core;
using GeoTime.Core.Models;
using GeoTime.Core.Services;

namespace GeoTime.Tests;

/// <summary>P6 — Craton placement heuristic.</summary>
public class PlateLandmassRealismP6Tests
{
    private const uint TestSeed = 6006u;

    private static (SimulationState state, List<PlateInfo> plates) BuildTwoPlateContinent(int gs = 32)
    {
        var state = new SimulationState(gs);
        var midCol = gs / 2;

        for (var i = 0; i < state.CellCount; i++)
        {
            var col = i % gs;
            state.PlateMap[i] = (ushort)(col < midCol ? 0 : 1);
            state.HeightMap[i] = -3000f;
            state.RockAgeMap[i] = 500f;
            state.CrustThicknessMap[i] = 7f;
            state.RockTypeMap[i] = (byte)RockType.IGN_BASALT;
        }

        // Continental block on plate 0 with interior + margin belt.
        for (var row = 4; row < gs - 4; row++)
        for (var col = 0; col < midCol; col++)
        {
            var i = row * gs + col;
            state.HeightMap[i] = 800f;
            state.CrustThicknessMap[i] = 35f;
            state.RockTypeMap[i] = (byte)RockType.IGN_GRANITE;
            state.RockAgeMap[i] = col < midCol - 4 ? 3000f : 1200f;
        }

        // Elevated collision belt on the plate boundary (both sides).
        for (var row = 4; row < gs - 4; row++)
        {
            var left = row * gs + (midCol - 1);
            var right = row * gs + midCol;
            state.HeightMap[left] = 1200f;
            state.HeightMap[right] = 1100f;
            state.RockAgeMap[left] = 1800f;
            state.RockAgeMap[right] = 1800f;
        }

        var plates = new List<PlateInfo>
        {
            new() { Id = 0, CenterLat = 0, CenterLon = -45, IsOceanic = false, Area = 0.5 },
            new() { Id = 1, CenterLat = 0, CenterLon = 45, IsOceanic = true, Area = 0.5 },
        };

        return (state, plates);
    }

    private static FeatureRegistry Detect(SimulationState state, List<PlateInfo> plates)
    {
        var detector = new FeatureDetectorService();
        detector.Detect(state, plates, [], [], TestSeed, 1L);
        return state.FeatureRegistry;
    }

    [Fact]
    public void P6_PlateInterior_OldRock_DetectsCraton()
    {
        var (state, plates) = BuildTwoPlateContinent();
        var registry = Detect(state, plates);

        var cratons = registry.Features.Values.Where(f => f.Type == FeatureType.Craton).ToList();
        Assert.NotEmpty(cratons);

        var interiorCell = 10 * state.GridSize + 4;
        Assert.Contains(cratons, c => c.CellIndices.Contains(interiorCell));
        Assert.All(cratons, c => Assert.True(c.Metrics["is_craton_core"] > 0f));
    }

    [Fact]
    public void P6_PlateBoundary_ElevatedCrust_DetectsOrogen()
    {
        var (state, plates) = BuildTwoPlateContinent();
        var registry = Detect(state, plates);

        var orogens = registry.Features.Values.Where(f => f.Type == FeatureType.Orogen).ToList();
        Assert.NotEmpty(orogens);

        var marginCell = 10 * state.GridSize + (state.GridSize / 2 - 1);
        Assert.Contains(orogens, o => o.CellIndices.Contains(marginCell));
        Assert.All(orogens, o => Assert.True(o.Metrics["is_orogen_margin"] > 0f));
    }

    [Fact]
    public void P6_YoungInteriorRock_NotClassifiedAsCraton()
    {
        var (state, plates) = BuildTwoPlateContinent();
        var midCol = state.GridSize / 2;

        // Young rock throughout the interior — only margin-adjacent cells remain young in fixture;
        // clear craton candidates by lowering age in deep interior.
        for (var row = 4; row < state.GridSize - 4; row++)
        for (var col = 0; col < midCol - 4; col++)
            state.RockAgeMap[row * state.GridSize + col] = 800f;

        var registry = Detect(state, plates);
        var interiorCell = 10 * state.GridSize + 4;

        Assert.DoesNotContain(registry.Features.Values,
            f => f.Type == FeatureType.Craton && f.CellIndices.Contains(interiorCell));
    }

    [Fact]
    public void P6_LowBoundary_NotClassifiedAsOrogen()
    {
        var (state, plates) = BuildTwoPlateContinent();
        var gs = state.GridSize;

        // Depress every plate-boundary cell so elevated-crust rule cannot fire.
        for (var i = 0; i < state.CellCount; i++)
        {
            if (state.HeightMap[i] < LandThresholdM) continue;
            var row = i / gs;
            var col = i % gs;
            var plateId = state.PlateMap[i];
            var isBoundary = (row > 0 && state.PlateMap[i - gs] != plateId)
                          || (row < gs - 1 && state.PlateMap[i + gs] != plateId)
                          || state.PlateMap[row * gs + (col - 1 + gs) % gs] != plateId
                          || state.PlateMap[row * gs + (col + 1) % gs] != plateId;
            if (isBoundary)
                state.HeightMap[i] = 200f;
        }

        var registry = Detect(state, plates);
        Assert.DoesNotContain(registry.Features.Values, f => f.Type == FeatureType.Orogen);
    }

    private const float LandThresholdM = -200f;

    [Fact]
    public async Task P6_ContextAssembler_ExposesCratonAndOrogenFlags()
    {
        var orchestrator = new SimulationOrchestrator(32);
        orchestrator.GeneratePlanet(TestSeed, new SimulationPlanetConfig { PlateTectonicsOnsetMa = -5000 });

        // Force Archean ages on a land interior cell for deterministic craton signal.
        var gs = orchestrator.State.GridSize;
        int landCell = -1;
        for (var i = 0; i < orchestrator.State.CellCount; i++)
        {
            if (orchestrator.State.HeightMap[i] < 0) continue;
            landCell = i;
            break;
        }
        Assert.True(landCell >= 0);

        for (var step = 0; step < 20 && landCell >= 0; step++)
        {
            orchestrator.State.RockAgeMap[landCell] = 3200f;
            orchestrator.AdvanceSimulation(1.0);
            var registry = orchestrator.GetFeatureRegistry();
            if (registry.Features.Values.Any(f => f.Type == FeatureType.Craton && f.CellIndices.Contains(landCell)))
                break;
        }

        var assembler = new GeologicalContextAssembler(orchestrator);
        var ctx = await assembler.AssembleAsync(landCell);
        Assert.NotNull(ctx);

        var hasCratonFeature = ctx.ContainingFeatures.Any(f => f.Type == FeatureType.Craton);
        if (hasCratonFeature)
            Assert.True(ctx.IsCratonCore);
        else
            Assert.False(ctx.IsCratonCore);

        var orogenCell = orchestrator.State.CellCount / 2;
        if (orchestrator.State.HeightMap[orogenCell] >= 500f)
        {
            orchestrator.State.HeightMap[orogenCell] = 1500f;
            orchestrator.AdvanceSimulation(0.1);
            var orogenCtx = await assembler.AssembleAsync(orogenCell);
            if (orogenCtx?.ContainingFeatures.Any(f => f.Type == FeatureType.Orogen) == true)
                Assert.True(orogenCtx.IsOrogenMargin);
        }
    }
}
