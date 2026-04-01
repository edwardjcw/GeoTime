using GeoTime.Core;
using Microsoft.AspNetCore.SignalR;

namespace GeoTime.Api;

/// <summary>
/// SignalR hub for real-time simulation streaming.
/// Clients can request planet generation, advance simulation, and receive
/// state updates pushed from the server.
/// </summary>
public sealed class SimulationHub(SimulationOrchestrator sim) : Hub
{
    /// <summary>Generate a new planet and broadcast the result to all clients.</summary>
    public async Task GeneratePlanet(uint seed)
    {
        var actualSeed = seed > 0 ? seed : (uint)Random.Shared.Next(1, int.MaxValue);
        var result = sim.GeneratePlanet(actualSeed);
        await Clients.All.SendAsync("PlanetGenerated", new
        {
            seed = result.Seed,
            plateCount = result.Plates.Count,
            hotspotCount = result.Hotspots.Count,
            timeMa = sim.GetCurrentTime(),
        });
    }

    /// <summary>
    /// Advance the simulation by deltaMa and stream state snapshots back to the caller.
    /// Breaks the advance into steps so the client receives incremental updates.
    /// </summary>
    public async Task AdvanceSimulation(double deltaMa, int steps = 1)
    {
        if (deltaMa <= 0 || steps < 1) return;

        var stepSize = deltaMa / steps;
        for (var i = 0; i < steps; i++)
        {
            sim.AdvanceSimulation(stepSize);
            await Clients.Caller.SendAsync("SimulationTick", new
            {
                timeMa = sim.GetCurrentTime(),
                step = i + 1,
                totalSteps = steps,
            });
        }

        await Clients.Caller.SendAsync("SimulationAdvanceComplete", new
        {
            timeMa = sim.GetCurrentTime(),
        });
    }

    /// <summary>
    /// Stream the current height map to the caller.
    /// Sends it in chunks to avoid overwhelming the connection.
    /// </summary>
    public async Task RequestHeightMap()
    {
        await Clients.Caller.SendAsync("HeightMapData", sim.State.HeightMap);
    }

    /// <summary>Stream the current temperature map to the caller.</summary>
    public async Task RequestTemperatureMap()
    {
        await Clients.Caller.SendAsync("TemperatureMapData", sim.State.TemperatureMap);
    }

    /// <summary>Stream the current precipitation map to the caller.</summary>
    public async Task RequestPrecipitationMap()
    {
        await Clients.Caller.SendAsync("PrecipitationMapData", sim.State.PrecipitationMap);
    }

    /// <summary>Stream the current biomass map to the caller.</summary>
    public async Task RequestBiomassMap()
    {
        await Clients.Caller.SendAsync("BiomassMapData", sim.State.BiomassMap);
    }

    /// <summary>Stream the current plate map to the caller.</summary>
    public async Task RequestPlateMap()
    {
        var plateMap = Array.ConvertAll(sim.State.PlateMap, v => (int)v);
        await Clients.Caller.SendAsync("PlateMapData", plateMap);
    }

    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("Connected", new
        {
            timeMa = sim.GetCurrentTime(),
            seed = sim.GetCurrentSeed(),
        });
        await base.OnConnectedAsync();
    }
}
