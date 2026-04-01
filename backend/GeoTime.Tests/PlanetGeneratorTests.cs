using GeoTime.Core.Models;
using GeoTime.Core.Proc;

namespace GeoTime.Tests;

public class PlanetGeneratorTests
{
    [Fact]
    public void Generate_ProducesValidState()
    {
        var state = new SimulationState(64); // Small grid for speed
        var gen = new PlanetGenerator(42);
        var result = gen.Generate(state);

        Assert.InRange(result.Plates.Count, 10, 16);
        Assert.InRange(result.Hotspots.Count, 2, 5);
        Assert.Equal(42u, result.Seed);
        Assert.Equal(0.78, result.Atmosphere.N2);
    }

    [Fact]
    public void Generate_PlatesHaveValidProperties()
    {
        var state = new SimulationState(64);
        var gen = new PlanetGenerator(42);
        var result = gen.Generate(state);

        foreach (var plate in result.Plates)
        {
            Assert.InRange(plate.CenterLat, -90, 90);
            Assert.InRange(plate.CenterLon, -180, 180);
            Assert.True(plate.Area > 0);
        }
    }

    [Fact]
    public void Generate_HeightMapHasVariation()
    {
        var state = new SimulationState(64);
        var gen = new PlanetGenerator(42);
        gen.Generate(state);

        var min = state.HeightMap.Min();
        var max = state.HeightMap.Max();
        Assert.True(max > min, "Height map should have variation");
    }

    [Fact]
    public void Generate_PlateMapAssigned()
    {
        var state = new SimulationState(64);
        var gen = new PlanetGenerator(42);
        var result = gen.Generate(state);

        var usedPlates = state.PlateMap.Distinct().ToList();
        Assert.True(usedPlates.Count > 1, "Multiple plates should be assigned");
    }

    [Fact]
    public void Generate_IsDeterministic()
    {
        var state1 = new SimulationState(64);
        var state2 = new SimulationState(64);
        new PlanetGenerator(42).Generate(state1);
        new PlanetGenerator(42).Generate(state2);

        for (var i = 0; i < state1.CellCount; i++)
            Assert.Equal(state1.HeightMap[i], state2.HeightMap[i]);
    }
}
