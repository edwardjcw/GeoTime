using GeoTime.Core;

namespace GeoTime.Tests;

public class StateSerializationTests
{
    [Fact]
    public void SerializeState_ProducesNonEmptyData()
    {
        var sim = new SimulationOrchestrator(64);
        sim.GeneratePlanet(42);

        var data = sim.SerializeState();
        Assert.NotNull(data);
        Assert.True(data.Length > 0);
    }

    [Fact]
    public void DeserializeState_RestoresHeightMap()
    {
        var sim = new SimulationOrchestrator(64);
        sim.GeneratePlanet(42);

        // Capture state
        var data = sim.SerializeState();
        var originalHeights = (float[])sim.State.HeightMap.Clone();
        var originalTime = sim.GetCurrentTime();

        // Advance to change state
        sim.AdvanceSimulation(10);
        Assert.NotEqual(originalTime, sim.GetCurrentTime());

        // Restore
        sim.DeserializeState(data);

        Assert.Equal(originalTime, sim.GetCurrentTime(), 6);
        for (var i = 0; i < sim.State.CellCount; i++)
            Assert.Equal(originalHeights[i], sim.State.HeightMap[i]);
    }

    [Fact]
    public void SerializeDeserialize_PreservesAllArrays()
    {
        var sim = new SimulationOrchestrator(64);
        sim.GeneratePlanet(42);

        var data = sim.SerializeState();

        var origTemp = (float[])sim.State.TemperatureMap.Clone();
        var origPrecip = (float[])sim.State.PrecipitationMap.Clone();
        var origRock = (byte[])sim.State.RockTypeMap.Clone();
        var origPlate = (ushort[])sim.State.PlateMap.Clone();

        // Modify current state
        sim.AdvanceSimulation(5);

        // Restore
        sim.DeserializeState(data);

        Assert.Equal(origTemp, sim.State.TemperatureMap);
        Assert.Equal(origPrecip, sim.State.PrecipitationMap);
        Assert.Equal(origRock, sim.State.RockTypeMap);
        Assert.Equal(origPlate, sim.State.PlateMap);
    }

    [Fact]
    public void ParallelAdvance_ProducesValidState()
    {
        var sim = new SimulationOrchestrator(64);
        sim.GeneratePlanet(42);

        // Run multiple advances to exercise parallel engine ticks
        for (var i = 0; i < 3; i++)
            sim.AdvanceSimulation(1.0);

        // State should still be valid after parallel processing
        Assert.True(sim.GetCurrentTime() > -4500);
        var cell = sim.InspectCell(0);
        Assert.NotNull(cell);
        Assert.True(float.IsFinite(cell.Height));
        Assert.True(float.IsFinite(cell.Temperature));
    }
}
