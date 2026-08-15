using GeoTime.Core;
using GeoTime.Core.Models;
using GeoTime.Core.Engines;
using GeoTime.Core.Kernel;

namespace GeoTime.Tests;

/// <summary>P1 — Epoch-aware tectonic regime (plate-tectonics onset gating).</summary>
public class PlateLandmassRealismP1Tests
{
    private static SimulationState MakeSmallState(int gs = 16)
    {
        var state = new SimulationState(gs);
        for (var row = 0; row < gs; row++)
            for (var col = 0; col < gs; col++)
                state.PlateMap[row * gs + col] = (ushort)(col < gs / 2 ? 0 : 1);

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

    private static List<PlateInfo> MakeTwoPlates(double rate0 = 50.0, double rate1 = -50.0)
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
    public void P1_DefaultOnset_Is3800MaAgo()
    {
        Assert.Equal(-3800, SimulationPlanetConfig.DefaultPlateTectonicsOnsetMa);
        Assert.Equal(-3800, new SimulationPlanetConfig().PlateTectonicsOnsetMa);
    }

    [Fact]
    public void P1_PreOnset_SkipsPlateAdvection()
    {
        var state = MakeSmallState();
        var originalPlateMap = (ushort[])state.PlateMap.Clone();

        var engine = new TectonicEngine(new EventBus(), new EventLog(), 42, 0.1);
        engine.PlateTectonicsOnsetMa = -3800;
        engine.Initialize(MakeTwoPlates(), [], new AtmosphericComposition(), state);

        // Hadean time before default 3.8 Ga onset
        for (var i = 0; i < 10; i++)
            engine.Tick(-4500 + i * 0.5, 0.5);

        Assert.Equal(originalPlateMap, state.PlateMap);
        Assert.False(engine.IsPlateTectonicsActive(-4500));
    }

    [Fact]
    public void P1_PostOnset_RunsPlateAdvection()
    {
        var state = MakeSmallState();
        var originalPlateMap = (ushort[])state.PlateMap.Clone();

        var engine = new TectonicEngine(new EventBus(), new EventLog(), 42, 0.1);
        engine.PlateTectonicsOnsetMa = -5000; // onset before simulation start
        engine.Initialize(MakeTwoPlates(), [], new AtmosphericComposition(), state);

        for (var i = 0; i < 10; i++)
            engine.Tick(-4500 + i * 0.5, 0.5);

        var anyMoved = false;
        for (var i = 0; i < state.CellCount; i++)
        {
            if (state.PlateMap[i] != originalPlateMap[i])
            {
                anyMoved = true;
                break;
            }
        }

        Assert.True(anyMoved);
        Assert.True(engine.IsPlateTectonicsActive(-4500));
    }

    [Fact]
    public void P1_PreOnset_SkipsPlateCollisionEvents()
    {
        var state = MakeSmallState();
        var log = new EventLog();
        var engine = new TectonicEngine(new EventBus(), log, 42, 0.1);
        engine.PlateTectonicsOnsetMa = -3800;
        engine.Initialize(MakeTwoPlates(), [], new AtmosphericComposition(), state);

        for (var i = 0; i < 20; i++)
            engine.Tick(-4500 + i * 0.5, 0.5);

        Assert.DoesNotContain(log.GetAll(), e => e.Type == "PLATE_COLLISION");
    }

    [Fact]
    public void P1_Orchestrator_RespectsConfigurableOnset()
    {
        var sim = new SimulationOrchestrator(16);
        var config = new SimulationPlanetConfig { PlateTectonicsOnsetMa = -3200 };
        sim.GeneratePlanet(99, config);

        Assert.Equal(-3200, sim.GetPlateTectonicsOnsetMa());
        Assert.False(sim.IsPlateTectonicsActive());
    }

    [Fact]
    public void P1_AtOnsetBoundary_AdvectionIsActive()
    {
        var config = new SimulationPlanetConfig();
        Assert.True(config.IsPlateTectonicsActive(-3800));
        Assert.False(config.IsPlateTectonicsActive(-3800.1));
    }
}
