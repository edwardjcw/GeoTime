using GeoTime.Core;
using GeoTime.Core.Models;

namespace GeoTime.Tests;

public class SimulationOrchestratorTests
{
    [Fact]
    public void GeneratePlanet_CreatesValidState()
    {
        var sim = new SimulationOrchestrator(64);
        var result = sim.GeneratePlanet(42);

        Assert.Equal(42u, sim.GetCurrentSeed());
        Assert.Equal(-4500, sim.GetCurrentTime());
        Assert.NotEmpty(result.Plates);
        Assert.NotEmpty(result.Hotspots);
    }

    [Fact]
    public void AdvanceSimulation_MovesTimeForward()
    {
        var sim = new SimulationOrchestrator(64);
        sim.GeneratePlanet(42);
        sim.AdvanceSimulation(10);
        Assert.True(sim.GetCurrentTime() > -4500);
    }

    [Fact]
    public void InspectCell_ReturnsValidData()
    {
        var sim = new SimulationOrchestrator(64);
        sim.GeneratePlanet(42);
        var cell = sim.InspectCell(0);
        Assert.NotNull(cell);
        Assert.Equal(0, cell.CellIndex);
    }

    [Fact]
    public void InspectCell_OutOfRange_ReturnsNull()
    {
        var sim = new SimulationOrchestrator(64);
        sim.GeneratePlanet(42);
        Assert.Null(sim.InspectCell(-1));
        Assert.Null(sim.InspectCell(999999));
    }

    [Fact]
    public void GetCrossSection_WithValidPath_ReturnsProfile()
    {
        var sim = new SimulationOrchestrator(64);
        sim.GeneratePlanet(42);
        var profile = sim.GetCrossSection(new List<LatLon>
        {
            new(0, 0), new(45, 90)
        });
        Assert.NotNull(profile);
        Assert.NotEmpty(profile.Samples);
        Assert.True(profile.TotalDistanceKm > 0);
    }

    [Fact]
    public void GetCrossSection_InsufficientPoints_ReturnsNull()
    {
        var sim = new SimulationOrchestrator(64);
        sim.GeneratePlanet(42);
        Assert.Null(sim.GetCrossSection(new List<LatLon> { new(0, 0) }));
    }

    [Fact]
    public void GetPlates_ReturnsPlateData()
    {
        var sim = new SimulationOrchestrator(64);
        sim.GeneratePlanet(42);
        var plates = sim.GetPlates();
        Assert.NotNull(plates);
        Assert.NotEmpty(plates);
    }

    [Fact]
    public void GetAtmosphere_ReturnsData()
    {
        var sim = new SimulationOrchestrator(64);
        sim.GeneratePlanet(42);
        var atmo = sim.GetAtmosphere();
        Assert.NotNull(atmo);
        Assert.Equal(0.78, atmo.N2);
    }

    [Fact]
    public void MultipleAdvances_ModifyState()
    {
        var sim = new SimulationOrchestrator(64);
        sim.GeneratePlanet(42);

        float[] initialHeights = (float[])sim.State.HeightMap.Clone();
        for (int i = 0; i < 5; i++)
            sim.AdvanceSimulation(1.0);

        // After several ticks, at least some cells should have changed
        bool anyChanged = false;
        for (int i = 0; i < sim.State.CellCount; i++)
            if (Math.Abs(sim.State.HeightMap[i] - initialHeights[i]) > 1e-6)
            { anyChanged = true; break; }
        Assert.True(anyChanged, "Simulation should modify height map");
    }
}
