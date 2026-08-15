using GeoTime.Core;
using GeoTime.Core.Engines;
using GeoTime.Core.Kernel;
using GeoTime.Core.Models;
using GeoTime.Core.Proc;

namespace GeoTime.Tests;

/// <summary>P4 — Suture formation on continental collision.</summary>
public class PlateLandmassRealismP4Tests
{
    private static (SimulationState state, TectonicEngine engine, EventLog log) BuildCollidingContinents(int gs = 32)
    {
        var state = new SimulationState(gs);
        var log = new EventLog();

        for (var i = 0; i < state.CellCount; i++)
        {
            var col = i % gs;
            state.PlateMap[i] = (ushort)(col < gs / 2 ? 0 : 1);
            state.HeightMap[i] = 900f;
            state.CrustThicknessMap[i] = 35f;
            state.RockTypeMap[i] = (byte)RockType.IGN_GRANITE;
            state.TerraneMap[i] = (ushort)(col < gs / 2 ? 1 : 2);
            state.RockAgeMap[i] = 2000f;
        }

        var plates = new List<PlateInfo>
        {
            new()
            {
                Id = 0, CenterLat = 0, CenterLon = -45, IsOceanic = false, Area = 0.5,
                AngularVelocity = new AngularVelocity { Lat = 90, Lon = 0, Rate = 5 },
            },
            new()
            {
                Id = 1, CenterLat = 0, CenterLon = 45, IsOceanic = false, Area = 0.5,
                AngularVelocity = new AngularVelocity { Lat = 90, Lon = 0, Rate = -5 },
            },
        };

        var engine = new TectonicEngine(new EventBus(), log, 42, 0.1);
        engine.PlateTectonicsOnsetMa = -5000;
        engine.Initialize(plates, [], new AtmosphericComposition(), state);
        return (state, engine, log);
    }

    [Fact]
    public void P4_ContinentalCollision_RecordsSutureCells()
    {
        var (state, engine, log) = BuildCollidingContinents();

        engine.Tick(-3000, 25.0);

        Assert.Contains(state.SutureMap, s => s > 0);
        Assert.Contains(log.GetAll(), e => e.Type == "SUTURE_FORMATION");
    }

    [Fact]
    public void P4_ContinentalCollision_PreservesTerraneIds()
    {
        var (state, engine, _) = BuildCollidingContinents();
        var terranesBefore = state.TerraneMap.Where(t => t > 0).Distinct().OrderBy(t => t).ToList();

        engine.Tick(-3000, 10.0);

        var terranesAfter = state.TerraneMap.Where(t => t > 0).Distinct().OrderBy(t => t).ToList();
        Assert.Equal(terranesBefore, terranesAfter);
        Assert.Contains(terranesAfter, t => t == 1);
        Assert.Contains(terranesAfter, t => t == 2);
    }

    [Fact]
    public void P4_SustainedCollision_MergesPlateIds()
    {
        var (state, engine, log) = BuildCollidingContinents();

        for (var step = 0; step < 12; step++)
            engine.Tick(-3000 + step * 5, 5.0);

        var plateIds = state.PlateMap.Where(p => p is 0 or 1).Distinct().ToList();
        Assert.Single(plateIds);
        Assert.Contains(log.GetAll(), e => e.Type == "PLATE_MERGE");
    }

    [Fact]
    public void P4_SerializeState_PreservesSutureMap()
    {
        var sim = new SimulationOrchestrator(64);
        sim.GeneratePlanet(42, new SimulationPlanetConfig { PlateTectonicsOnsetMa = -5000 });
        sim.State.SutureMap[100] = 1;
        sim.State.SutureMap[200] = 1;

        var original = (byte[])sim.State.SutureMap.Clone();

        var data = sim.SerializeState();
        sim.AdvanceSimulation(5);
        sim.DeserializeState(data);

        Assert.Equal(original, sim.State.SutureMap);
    }
}
