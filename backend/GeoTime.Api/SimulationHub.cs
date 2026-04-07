using GeoTime.Core;
using GeoTime.Core.Compute;
using Microsoft.AspNetCore.SignalR;

namespace GeoTime.Api;

/// <summary>
/// SignalR hub for real-time simulation streaming.
/// Clients can request planet generation, advance simulation, and receive
/// state updates pushed from the server.
///
/// Recommendation 8 (Strategy F): After each advance step the hub pushes a
/// binary state bundle (height + temperature + precipitation) directly to the
/// caller so the frontend can update its GPU textures without a separate REST
/// round-trip.
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
    /// Emits "SimulationProgress" events with the engine phase name as each phase completes.
    ///
    /// After each step a compact binary state bundle (height + temperature + precipitation
    /// as raw float32 bytes) is pushed via "StateBundleData" so the frontend can update
    /// its GPU textures without polling REST endpoints.
    /// </summary>
    public async Task AdvanceSimulation(double deltaMa, int steps = 1)
    {
        if (deltaMa <= 0 || steps < 1) return;

        var stepSize = deltaMa / steps;
        for (var i = 0; i < steps; i++)
        {
            var step = i; // capture for the lambda
            sim.AdvanceSimulation(stepSize, phase =>
            {
                // Fire-and-forget: SignalR queues async sends internally so this is safe.
                _ = Clients.Caller.SendAsync("SimulationProgress", new
                {
                    phase,
                    step = step + 1,
                    totalSteps = steps,
                    timeMa = sim.GetCurrentTime(),
                });
            });

            await Clients.Caller.SendAsync("SimulationTick", new
            {
                timeMa = sim.GetCurrentTime(),
                step = i + 1,
                totalSteps = steps,
            });

            // Strategy F: push binary state bundle directly to the caller after each step.
            // Layout: [height bytes | temp bytes | precip bytes] = 3 × cellCount × 4 bytes.
            await PushStateBundleAsync();

            // Phase L6: broadcast changed feature labels to all clients after each step.
            await PushFeaturesUpdatedAsync(currentTick: sim.State.FeatureRegistry.LastUpdatedTick);
        }

        await Clients.Caller.SendAsync("SimulationAdvanceComplete", new
        {
            timeMa = sim.GetCurrentTime(),
        });
    }

    /// <summary>
    /// Push the current state bundle (height + temperature + precipitation as raw float32
    /// bytes) to the calling client.  Clients should handle "StateBundleData" to receive it.
    /// </summary>
    public async Task RequestStateBundleBinary()
        => await PushStateBundleAsync();

    private async Task PushStateBundleAsync()
    {
        var height = sim.State.HeightMap;
        var temp   = sim.State.TemperatureMap;
        var precip = sim.State.PrecipitationMap;
        var floatBytes = height.Length * sizeof(float);
        var bundle = new byte[floatBytes * 3];
        Buffer.BlockCopy(height, 0, bundle, 0,              floatBytes);
        Buffer.BlockCopy(temp,   0, bundle, floatBytes,     floatBytes);
        Buffer.BlockCopy(precip, 0, bundle, floatBytes * 2, floatBytes);
        await Clients.Caller.SendAsync("StateBundleData", bundle);
    }

    /// <summary>
    /// Broadcast <c>FeaturesUpdated</c> to all clients with the labels of features that
    /// changed during the most recent tick (i.e. features whose latest snapshot was
    /// created at <paramref name="currentTick"/>).
    /// </summary>
    private async Task PushFeaturesUpdatedAsync(long currentTick)
    {
        var changedLabels = sim.State.FeatureRegistry.Features.Values
            .Where(f => f.History.Count > 0
                     && f.Current.SimTickCreated == currentTick
                     && f.Current.Status != GeoTime.Core.Models.FeatureStatus.Extinct)
            .Select(f => new
            {
                id        = f.Id,
                name      = f.Current.Name,
                type      = f.Type.ToString(),
                centerLat = f.Current.CenterLat,
                centerLon = f.Current.CenterLon,
                status    = f.Current.Status.ToString(),
                formerNames = f.FormerNames,
            })
            .ToList();

        if (changedLabels.Count > 0)
        {
            await Clients.All.SendAsync("FeaturesUpdated", new
            {
                tick   = currentTick,
                labels = changedLabels,
            });
        }
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
        var computeInfo = sim.GetComputeInfo();
        await Clients.Caller.SendAsync("Connected", new
        {
            timeMa = sim.GetCurrentTime(),
            seed = sim.GetCurrentSeed(),
            computeMode = computeInfo.Mode.ToString(),
            computeDevice = computeInfo.DeviceName,
            computeMemoryMb = computeInfo.MemoryMb,
        });
        await base.OnConnectedAsync();
    }
}
