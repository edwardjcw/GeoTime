using GeoTime.Core;
using GeoTime.Core.Engines;
using GeoTime.Core.Kernel;
using GeoTime.Core.Models;
using GeoTime.Core.Proc;
using GeoTime.Core.Services;

namespace GeoTime.Tests;

/// <summary>P3 — Terrane ID / crust provenance tracking.</summary>
public class PlateLandmassRealismP3Tests
{
    private static SimulationState GeneratePostOnset(uint seed, int gridSize = 64)
    {
        var config = new SimulationPlanetConfig { PlateTectonicsOnsetMa = -5000 };
        var state = new SimulationState(gridSize);
        new PlanetGenerator(seed).Generate(state, config);
        return state;
    }

    [Fact]
    public void P3_ContinentalSeeds_AssignUniqueTerraneIds()
    {
        var state = GeneratePostOnset(42);

        var terraneIds = state.TerraneMap.Where(t => t > 0).Distinct().OrderBy(t => t).ToList();
        Assert.NotEmpty(terraneIds);

        foreach (var terraneId in terraneIds)
        {
            var cells = Enumerable.Range(0, state.CellCount)
                .Where(i => state.TerraneMap[i] == terraneId)
                .ToList();
            Assert.All(cells, i => Assert.True(state.HeightMap[i] > 0));
            Assert.All(cells, i => Assert.True(state.CrustThicknessMap[i] >= 20));
        }

        // Each terrane ID is unique (no duplicate assignment to separate seeds).
        Assert.Equal(terraneIds.Count, terraneIds.Distinct().Count());
    }

    [Fact]
    public void P3_OceanCells_HaveZeroTerrane()
    {
        var state = GeneratePostOnset(77);

        for (var i = 0; i < state.CellCount; i++)
        {
            if (state.HeightMap[i] <= 0)
                Assert.Equal(0, state.TerraneMap[i]);
        }
    }

    [Fact]
    public void P3_TerraneMap_AdvectsWithCells()
    {
        const int gs = 16;
        var state = new SimulationState(gs);
        for (var row = 0; row < gs; row++)
            for (var col = 0; col < gs; col++)
                state.PlateMap[row * gs + col] = (ushort)(col < gs / 2 ? 0 : 1);

        ushort terraneMarker = 7;
        for (var i = 0; i < state.CellCount; i++)
        {
            if (state.PlateMap[i] == 0)
            {
                state.HeightMap[i] = 500f;
                state.CrustThicknessMap[i] = 35f;
                state.RockTypeMap[i] = (byte)RockType.IGN_GRANITE;
                state.TerraneMap[i] = terraneMarker;
            }
            else
            {
                state.HeightMap[i] = -4000f;
                state.CrustThicknessMap[i] = 7f;
                state.RockTypeMap[i] = (byte)RockType.IGN_BASALT;
                state.TerraneMap[i] = 0;
            }
            state.RockAgeMap[i] = 1000f;
        }

        var originalTerraneCells = state.TerraneMap
            .Select((t, i) => (t, i))
            .Where(x => x.t == terraneMarker)
            .Select(x => x.i)
            .ToHashSet();

        var plates = new List<PlateInfo>
        {
            new()
            {
                Id = 0, CenterLat = 0, CenterLon = -45, IsOceanic = false, Area = 0.5,
                AngularVelocity = new AngularVelocity { Lat = 90, Lon = 0, Rate = 80 },
            },
            new()
            {
                Id = 1, CenterLat = 0, CenterLon = 45, IsOceanic = true, Area = 0.5,
                AngularVelocity = new AngularVelocity { Lat = 90, Lon = 0, Rate = -80 },
            },
        };

        var engine = new TectonicEngine(new EventBus(), new EventLog(), 42, 0.1);
        engine.PlateTectonicsOnsetMa = -5000;
        engine.Initialize(plates, [], new AtmosphericComposition(), state);
        // Sub-degree steps avoid ~360° wrap on the 16×16 periodic grid (deltaMa=5 at Rate=80
        // would land terrane markers back on the same indices).
        for (var i = 0; i < 10; i++)
            engine.Tick(100 + i * 0.5, 0.5);

        var newTerraneCells = state.TerraneMap
            .Select((t, i) => (t, i))
            .Where(x => x.t == terraneMarker)
            .Select(x => x.i)
            .ToHashSet();

        Assert.NotEqual(originalTerraneCells, newTerraneCells);
        Assert.NotEmpty(newTerraneCells);
        Assert.True(newTerraneCells.Count >= originalTerraneCells.Count * 0.9,
            "Most terrane-marked cells should survive advection");
    }

    [Fact]
    public void P3_FeatureDetector_IdentifiesSuturesWithinContinent()
    {
        const int gs = 32;
        var state = new SimulationState(gs);

        // Single connected landmass with two terranes meeting in the middle.
        for (var i = 0; i < state.CellCount; i++)
        {
            var row = i / gs;
            var col = i % gs;
            state.PlateMap[i] = (ushort)(col < gs / 2 ? 0 : 1);
            state.HeightMap[i] = 800f;
            state.CrustThicknessMap[i] = 35f;
            state.RockTypeMap[i] = (byte)RockType.IGN_GRANITE;
            state.TerraneMap[i] = (ushort)(col < gs / 2 ? 1 : 2);
            state.RockAgeMap[i] = 2000f;
        }

        var plates = new List<PlateInfo>
        {
            new() { Id = 0, IsOceanic = false, Area = 0.5 },
            new() { Id = 1, IsOceanic = false, Area = 0.5 },
        };

        var detector = new FeatureDetectorService();
        detector.Detect(state, plates, [], [], 99, currentTick: 0);

        var sutures = state.FeatureRegistry.Features.Values
            .Where(f => f.Type == FeatureType.Suture)
            .ToList();

        Assert.NotEmpty(sutures);
        Assert.All(sutures, s => Assert.True(s.Metrics["terrane_count"] >= 2));
        Assert.All(sutures, s => Assert.True(s.CellIndices.Count > 0));
    }

    [Fact]
    public void P3_SerializeState_PreservesTerraneMap()
    {
        var sim = new SimulationOrchestrator(64);
        sim.GeneratePlanet(42, new SimulationPlanetConfig { PlateTectonicsOnsetMa = -5000 });

        var original = (ushort[])sim.State.TerraneMap.Clone();
        Assert.Contains(original, t => t > 0);

        var data = sim.SerializeState();
        sim.AdvanceSimulation(5);
        sim.DeserializeState(data);

        Assert.Equal(original, sim.State.TerraneMap);
    }
}
