using GeoTime.Core;
using GeoTime.Core.Engines;
using GeoTime.Core.Kernel;
using GeoTime.Core.Models;
using GeoTime.Core.Proc;

namespace GeoTime.Tests;

/// <summary>
/// BF-1 — Antimeridian seam continuity (col 0 vs col gs-1).
/// Root cause: sim_grid_wrap HeightMap cliffs (~5 km land/ocean isostasy) drive
/// temperature walls; renderer UV zipper was already OK.
/// </summary>
public class AntimeridianSeamBF1Tests
{
    /// <summary>
    /// Same-plate seam pairs must not retain a land/ocean isostatic cliff.
    /// ~500 m allows residual noise after averaging; far below ~5 km diagnosis cliff.
    /// </summary>
    private const float MaxSamePlateHeightDeltaM = 500f;

    /// <summary>
    /// Lapse rate is 6.5 °C/km; 500 m → ~3.25 °C plus climate diffusion noise.
    /// </summary>
    private const float MaxSamePlateTempDeltaC = 4.0f;

    private static SimulationPlanetConfig PostOnsetConfig() =>
        new() { PlateTectonicsOnsetMa = -5000 };

    private static (float maxSamePlate, float maxAny, int samePlateRows) MaxSeamHeightDelta(SimulationState state)
    {
        var gs = state.GridSize;
        float maxSame = 0f, maxAny = 0f;
        var sameRows = 0;
        for (var row = 0; row < gs; row++)
        {
            var i0 = row * gs;
            var i1 = row * gs + (gs - 1);
            var dH = Math.Abs(state.HeightMap[i0] - state.HeightMap[i1]);
            if (dH > maxAny) maxAny = dH;
            if (state.PlateMap[i0] != state.PlateMap[i1]) continue;
            sameRows++;
            if (dH > maxSame) maxSame = dH;
        }
        return (maxSame, maxAny, sameRows);
    }

    private static float MaxSamePlateTempDelta(SimulationState state)
    {
        var gs = state.GridSize;
        float max = 0f;
        for (var row = 0; row < gs; row++)
        {
            var i0 = row * gs;
            var i1 = row * gs + (gs - 1);
            if (state.PlateMap[i0] != state.PlateMap[i1]) continue;
            var dT = Math.Abs(state.TemperatureMap[i0] - state.TemperatureMap[i1]);
            if (dT > max) max = dT;
        }
        return max;
    }

    [Fact]
    public void BF1_GridSeamSync_AveragesSameDomainHeights()
    {
        var state = new SimulationState(8);
        for (var row = 0; row < 8; row++)
        {
            var i0 = row * 8;
            var i1 = row * 8 + 7;
            state.PlateMap[i0] = state.PlateMap[i1] = 1;
            state.HeightMap[i0] = -3000f;
            state.HeightMap[i1] = -4000f;
            state.CrustThicknessMap[i0] = 7f;
            state.CrustThicknessMap[i1] = 7f;
            state.RockTypeMap[i0] = state.RockTypeMap[i1] = (byte)RockType.IGN_BASALT;
        }

        GridSeamSync.SyncAntimeridian(state);

        for (var row = 0; row < 8; row++)
        {
            var i0 = row * 8;
            var i1 = row * 8 + 7;
            Assert.Equal(-3500f, state.HeightMap[i0], 1e-3);
            Assert.Equal(state.HeightMap[i0], state.HeightMap[i1]);
        }
    }

    [Fact]
    public void BF1_GridSeamSync_PreferLandWhenSamePlateCliff()
    {
        var state = new SimulationState(4);
        state.PlateMap[0] = state.PlateMap[3] = 2;
        state.HeightMap[0] = 800f;
        state.HeightMap[3] = -3500f;
        state.CrustThicknessMap[0] = 35f;
        state.CrustThicknessMap[3] = 7f;
        state.RockTypeMap[0] = (byte)RockType.IGN_GRANITE;
        state.RockTypeMap[3] = (byte)RockType.IGN_BASALT;
        state.TerraneMap[0] = 5;
        state.TerraneMap[3] = 0;

        GridSeamSync.SyncAntimeridian(state);

        Assert.Equal(800f, state.HeightMap[0]);
        Assert.Equal(800f, state.HeightMap[3]);
        Assert.Equal(35f, state.CrustThicknessMap[3]);
        Assert.Equal((byte)RockType.IGN_GRANITE, state.RockTypeMap[3]);
        Assert.Equal((ushort)5, state.TerraneMap[3]);
    }

    [Fact]
    public void BF1_GridSeamSync_LeavesDifferentPlateBoundary()
    {
        var state = new SimulationState(4);
        state.PlateMap[0] = 1;
        state.PlateMap[3] = 2;
        state.HeightMap[0] = 1000f;
        state.HeightMap[3] = -5000f;
        state.CrustThicknessMap[0] = 35f;
        state.CrustThicknessMap[3] = 7f;

        GridSeamSync.SyncAntimeridian(state);

        Assert.Equal(1000f, state.HeightMap[0]);
        Assert.Equal(-5000f, state.HeightMap[3]);
    }

    [Fact]
    public void BF1_AfterGenerate_SamePlateSeamHeightContinuous_Seed42()
    {
        var state = new SimulationState(64);
        new PlanetGenerator(42).Generate(state, PostOnsetConfig());

        var (maxSame, _, sameRows) = MaxSeamHeightDelta(state);
        Assert.True(sameRows > 0, "expected at least one same-plate antimeridian row");
        Assert.True(
            maxSame <= MaxSamePlateHeightDeltaM,
            $"same-plate seam |dH|_max={maxSame:F1} m exceeds {MaxSamePlateHeightDeltaM} m");
    }

    [Fact]
    public void BF1_AfterGenerate_SamePlateSeamTemperatureContinuous_Seed42()
    {
        var sim = new SimulationOrchestrator(64);
        sim.GeneratePlanet(42, PostOnsetConfig());

        var maxT = MaxSamePlateTempDelta(sim.State);
        Assert.True(
            maxT <= MaxSamePlateTempDeltaC,
            $"same-plate seam |dT|_max={maxT:F2} °C exceeds {MaxSamePlateTempDeltaC} °C");
    }

    [Fact]
    public void BF1_AfterAdvection_SamePlateSeamHeightContinuous_Seed42()
    {
        var state = new SimulationState(64);
        var result = new PlanetGenerator(42).Generate(state, PostOnsetConfig());

        var engine = new TectonicEngine(new EventBus(), new EventLog(), 42, 0.1);
        engine.PlateTectonicsOnsetMa = -5000;
        engine.Initialize(result.Plates, result.Hotspots, result.Atmosphere, state);

        // Several post-onset ticks so AdvectPlates scatter runs and re-syncs.
        for (var i = 0; i < 8; i++)
            engine.Tick(-3000 + i * 10, 10);

        var (maxSame, _, sameRows) = MaxSeamHeightDelta(state);
        Assert.True(sameRows > 0);
        Assert.True(
            maxSame <= MaxSamePlateHeightDeltaM,
            $"after advect same-plate seam |dH|_max={maxSame:F1} m exceeds {MaxSamePlateHeightDeltaM} m");
    }

    [Fact]
    public void BF1_AfterAdvance_SamePlateSeamTemperatureContinuous_Seed42()
    {
        var sim = new SimulationOrchestrator(64);
        sim.GeneratePlanet(42, PostOnsetConfig());
        sim.AdvanceSimulation(50);

        var maxT = MaxSamePlateTempDelta(sim.State);
        Assert.True(
            maxT <= MaxSamePlateTempDeltaC,
            $"after advance same-plate seam |dT|_max={maxT:F2} °C exceeds {MaxSamePlateTempDeltaC} °C");
    }
}
