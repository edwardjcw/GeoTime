using GeoTime.Api;
using GeoTime.Core;
using GeoTime.Core.Compute;
using GeoTime.Core.Engines;
using GeoTime.Core.Kernel;
using GeoTime.Core.Models;
using MessagePack;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton<SimulationOrchestrator>();
builder.Services.AddSingleton<CameraStateService>();
builder.Services.AddSignalR();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

// ── SignalR Hub ────────────────────────────────────────────────────────────────

app.MapHub<SimulationHub>("/hubs/simulation");

// ── API Endpoints ─────────────────────────────────────────────────────────────

app.MapPost("/api/planet/generate", (GenerateRequest req, SimulationOrchestrator sim) =>
{
    var seed = req.Seed > 0 ? req.Seed : (uint)Random.Shared.Next(1, int.MaxValue);
    var result = sim.GeneratePlanet(seed);
    return Results.Ok(new
    {
        seed = result.Seed,
        plateCount = result.Plates.Count,
        hotspotCount = result.Hotspots.Count,
        timeMa = sim.GetCurrentTime(),
    });
}).WithName("GeneratePlanet");

app.MapPost("/api/simulation/advance", async (AdvanceRequest req, SimulationOrchestrator sim, IHubContext<SimulationHub> hubContext) =>
{
    // Collect phase names in order during the synchronous simulation run.
    var phases = new List<string>();
    sim.AdvanceSimulation(req.DeltaMa, phase => phases.Add(phase));

    // Send progress events in order *before* returning the HTTP response so
    // the frontend receives them in the correct sequence.
    foreach (var phase in phases)
    {
        await hubContext.Clients.All.SendAsync("SimulationProgress", new
        {
            phase,
            timeMa = sim.GetCurrentTime(),
        });
    }

    var stats = sim.LastTickStats;
    return Results.Ok(new
    {
        timeMa = sim.GetCurrentTime(),
        stats = new
        {
            tectonicMs  = stats.TectonicMs,
            surfaceMs   = stats.SurfaceMs,
            atmosphereMs = stats.AtmosphereMs,
            vegetationMs = stats.VegetationMs,
            biomatterMs  = stats.BiomatterMs,
            totalMs      = stats.TotalMs,
        },
    });
}).WithName("AdvanceSimulation");

app.MapGet("/api/simulation/stats", (SimulationOrchestrator sim) =>
{
    var stats = sim.LastTickStats;
    return Results.Ok(new
    {
        tectonicMs  = stats.TectonicMs,
        surfaceMs   = stats.SurfaceMs,
        atmosphereMs = stats.AtmosphereMs,
        vegetationMs = stats.VegetationMs,
        biomatterMs  = stats.BiomatterMs,
        totalMs      = stats.TotalMs,
        timeMa       = stats.TimeMa,
    });
}).WithName("GetSimulationStats");

app.MapGet("/api/simulation/time", (SimulationOrchestrator sim) =>
    Results.Ok(new { timeMa = sim.GetCurrentTime(), seed = sim.GetCurrentSeed() })
).WithName("GetSimulationTime");

// ── Compute Backend Info (Recommendation 5-6: GPU/CPU indicator) ─────────────

app.MapGet("/api/simulation/compute-info", (SimulationOrchestrator sim) =>
{
    var info = sim.GetComputeInfo();
    return Results.Ok(new
    {
        mode           = info.Mode.ToString(),
        deviceName     = info.DeviceName,
        acceleratorType = info.AcceleratorType,
        isGpu          = info.Mode == ComputeMode.GPU,
        memoryMb       = info.MemoryMb,
    });
}).WithName("GetComputeInfo");

// ── Adaptive Resolution Toggle (Recommendation 7) ────────────────────────────

app.MapGet("/api/simulation/adaptive-resolution", (SimulationOrchestrator sim) =>
    Results.Ok(new { enabled = sim.AdaptiveResolutionEnabled })
).WithName("GetAdaptiveResolution");

app.MapPost("/api/simulation/adaptive-resolution", (AdaptiveResolutionRequest req, SimulationOrchestrator sim) =>
{
    sim.AdaptiveResolutionEnabled = req.Enabled;
    return Results.Ok(new { enabled = sim.AdaptiveResolutionEnabled });
}).WithName("SetAdaptiveResolution");

app.MapGet("/api/state/heightmap", (SimulationOrchestrator sim) =>
    Results.Ok(sim.State.HeightMap)
).WithName("GetHeightMap");

app.MapGet("/api/state/platemap", (SimulationOrchestrator sim) =>
    Results.Ok(sim.State.PlateMap)
).WithName("GetPlateMap");

app.MapGet("/api/state/temperaturemap", (SimulationOrchestrator sim) =>
    Results.Ok(sim.State.TemperatureMap)
).WithName("GetTemperatureMap");

app.MapGet("/api/state/precipitationmap", (SimulationOrchestrator sim) =>
    Results.Ok(sim.State.PrecipitationMap)
).WithName("GetPrecipitationMap");

app.MapGet("/api/state/biomassmap", (SimulationOrchestrator sim) =>
    Results.Ok(sim.State.BiomassMap)
).WithName("GetBiomassMap");

app.MapGet("/api/state/biomattermap", (SimulationOrchestrator sim) =>
    Results.Ok(sim.State.BiomatterMap)
).WithName("GetBiomatterMap");

app.MapGet("/api/state/organiccarbonmap", (SimulationOrchestrator sim) =>
    Results.Ok(sim.State.OrganicCarbonMap)
).WithName("GetOrganicCarbonMap");

app.MapGet("/api/state/soilmap", (SimulationOrchestrator sim) =>
    Results.Ok(sim.State.SoilTypeMap.Select(b => (int)b).ToArray())
).WithName("GetSoilMap");

app.MapGet("/api/state/plates", (SimulationOrchestrator sim) =>
    Results.Ok(sim.GetPlates())
).WithName("GetPlates");

app.MapGet("/api/state/hotspots", (SimulationOrchestrator sim) =>
    Results.Ok(sim.GetHotspots())
).WithName("GetHotspots");

app.MapGet("/api/state/atmosphere", (SimulationOrchestrator sim) =>
    Results.Ok(sim.GetAtmosphere())
).WithName("GetAtmosphere");

app.MapGet("/api/state/events", (SimulationOrchestrator sim, int? count) =>
{
    var events = count.HasValue ? sim.EventLog.GetRecent(count.Value) : sim.EventLog.GetAll();
    return Results.Ok(events);
}).WithName("GetEvents");

app.MapGet("/api/state/inspect/{cellIndex}", (int cellIndex, SimulationOrchestrator sim) =>
{
    var result = sim.InspectCell(cellIndex);
    return result != null ? Results.Ok(result) : Results.NotFound();
}).WithName("InspectCell");

app.MapPost("/api/crosssection", (CrossSectionRequest req, SimulationOrchestrator sim) =>
{
    var points = req.Points.Select(p => new LatLon(p.Lat, p.Lon)).ToList();
    var profile = sim.GetCrossSection(points);
    return profile != null ? Results.Ok(profile) : Results.BadRequest("No active simulation or insufficient path points");
}).WithName("GetCrossSection");

// ── MessagePack Binary Endpoints ──────────────────────────────────────────────

app.MapGet("/api/state/heightmap/binary", (SimulationOrchestrator sim) =>
{
    var packed = MessagePackSerializer.Serialize(sim.State.HeightMap);
    return Results.Bytes(packed, "application/x-msgpack");
}).WithName("GetHeightMapBinary");

app.MapGet("/api/state/platemap/binary", (SimulationOrchestrator sim) =>
{
    var packed = MessagePackSerializer.Serialize(sim.State.PlateMap);
    return Results.Bytes(packed, "application/x-msgpack");
}).WithName("GetPlateMapBinary");

app.MapGet("/api/state/temperaturemap/binary", (SimulationOrchestrator sim) =>
{
    var packed = MessagePackSerializer.Serialize(sim.State.TemperatureMap);
    return Results.Bytes(packed, "application/x-msgpack");
}).WithName("GetTemperatureMapBinary");

app.MapGet("/api/state/precipitationmap/binary", (SimulationOrchestrator sim) =>
{
    var packed = MessagePackSerializer.Serialize(sim.State.PrecipitationMap);
    return Results.Bytes(packed, "application/x-msgpack");
}).WithName("GetPrecipitationMapBinary");

app.MapGet("/api/state/biomassmap/binary", (SimulationOrchestrator sim) =>
{
    var packed = MessagePackSerializer.Serialize(sim.State.BiomassMap);
    return Results.Bytes(packed, "application/x-msgpack");
}).WithName("GetBiomassMapBinary");

app.MapGet("/api/state/biomattermap/binary", (SimulationOrchestrator sim) =>
{
    var packed = MessagePackSerializer.Serialize(sim.State.BiomatterMap);
    return Results.Bytes(packed, "application/x-msgpack");
}).WithName("GetBiomatterMapBinary");

app.MapGet("/api/state/organiccarbonmap/binary", (SimulationOrchestrator sim) =>
{
    var packed = MessagePackSerializer.Serialize(sim.State.OrganicCarbonMap);
    return Results.Bytes(packed, "application/x-msgpack");
}).WithName("GetOrganicCarbonMapBinary");

// Bundle endpoint: height + temperature + precipitation as raw float32 bytes in a single
// round-trip.  Layout: [height bytes | temp bytes | precip bytes], each array = CellCount * 4 bytes.
// This eliminates 2 of the 3 HTTP requests and avoids JSON parsing overhead.
app.MapGet("/api/state/bundle/binary", (SimulationOrchestrator sim) =>
{
    var height = sim.State.HeightMap;
    var temp   = sim.State.TemperatureMap;
    var precip = sim.State.PrecipitationMap;
    var floatBytes = height.Length * sizeof(float);
    var result = new byte[floatBytes * 3];
    Buffer.BlockCopy(height, 0, result, 0,              floatBytes);
    Buffer.BlockCopy(temp,   0, result, floatBytes,     floatBytes);
    Buffer.BlockCopy(precip, 0, result, floatBytes * 2, floatBytes);
    return Results.Bytes(result, "application/octet-stream");
}).WithName("GetStateBundleBinary");

// ── Snapshot Management Endpoints ─────────────────────────────────────────────

app.MapPost("/api/snapshots/take", (SimulationOrchestrator sim) =>
{
    var data = sim.SerializeState();
    sim.Snapshots.TakeSnapshot(sim.GetCurrentTime(), data);
    return Results.Ok(new
    {
        timeMa = sim.GetCurrentTime(),
        snapshotCount = sim.Snapshots.Count,
    });
}).WithName("TakeSnapshot");

app.MapGet("/api/snapshots", (SimulationOrchestrator sim) =>
{
    var times = sim.Snapshots.GetSnapshotTimes();
    return Results.Ok(new { count = times.Count, times });
}).WithName("ListSnapshots");

app.MapPost("/api/snapshots/restore", (RestoreSnapshotRequest req, SimulationOrchestrator sim) =>
{
    var snap = sim.Snapshots.FindNearestBefore(req.TargetTimeMa);
    if (snap == null) return Results.NotFound("No snapshot found before the target time");

    sim.DeserializeState(snap.BufferData);
    return Results.Ok(new
    {
        restoredTimeMa = snap.TimeMa,
        targetTimeMa = req.TargetTimeMa,
    });
}).WithName("RestoreSnapshot");

app.MapGet("/api/snapshots/delta", (double fromTimeMa, double toTimeMa, SimulationOrchestrator sim) =>
{
    var fromSnap = sim.Snapshots.FindNearestBefore(fromTimeMa + 0.001);
    var toSnap = sim.Snapshots.FindNearestBefore(toTimeMa + 0.001);
    if (fromSnap == null || toSnap == null)
        return Results.NotFound("One or both snapshots not found");

    var delta = SnapshotDeltaCompressor.ComputeDelta(fromSnap.BufferData, toSnap.BufferData);
    var packed = MessagePackSerializer.Serialize(delta);
    return Results.Bytes(packed, "application/x-msgpack");
}).WithName("GetSnapshotDelta");

// ── Unreal Engine Integration Endpoints ──────────────────────────────────────

// Terrain metadata required by the UE Landscape system and camera manager.
app.MapGet("/api/unreal/terrain-meta", (SimulationOrchestrator sim) =>
{
    var meta = new TerrainMeta
    {
        GridSize         = sim.State.GridSize,
        CellCount        = sim.State.CellCount,
        CellSizeCm       = 60_000.0f,   // 600 m per cell → 60 000 cm in UE units
        MaxHeightCm      = 2_000_000.0f, // 20 km max elevation → 2 000 000 cm
        MinHeightCm      = -1_100_000.0f, // 11 km ocean depth → -1 100 000 cm
        FirstPersonThresholdKm = CameraMode.FirstPersonThresholdKm,
    };
    return Results.Ok(meta);
}).WithName("GetTerrainMeta");

// Raw float32 heightmap bytes – one float per cell, row-major, suitable for
// UE's Landscape bulk import (FLandscapeHeightmapFileFormat).
app.MapGet("/api/unreal/heightmap-raw", (SimulationOrchestrator sim) =>
{
    var bytes = new byte[sim.State.CellCount * sizeof(float)];
    Buffer.BlockCopy(sim.State.HeightMap, 0, bytes, 0, bytes.Length);
    return Results.Bytes(bytes, "application/octet-stream");
}).WithName("GetHeightMapRaw");

// Streaming terrain tile: tileX/tileY are tile indices (each tile is 64×64 cells).
// lod controls the sample step: 0 = full, 1 = half, 2 = quarter resolution.
app.MapGet("/api/unreal/terrain-tile/{tileX}/{tileY}/{lod}",
    (int tileX, int tileY, int lod, SimulationOrchestrator sim) =>
{
    const int TileSize = 64;
    var step     = 1 << Math.Clamp(lod, 0, 4);
    var gridSize = sim.State.GridSize;
    var originX  = tileX * TileSize;
    var originY  = tileY * TileSize;

    if (originX < 0 || originX >= gridSize || originY < 0 || originY >= gridSize)
        return Results.BadRequest("Tile coordinates out of range");

    var endX    = Math.Min(originX + TileSize, gridSize);
    var endY    = Math.Min(originY + TileSize, gridSize);
    var cols    = (int)Math.Ceiling((double)(endX - originX) / step);
    var rows    = (int)Math.Ceiling((double)(endY - originY) / step);
    var heights = new float[cols * rows];
    int idx     = 0;

    for (int y = originY; y < endY; y += step)
    {
        for (int x = originX; x < endX; x += step)
            heights[idx++] = sim.State.HeightMap[y * gridSize + x];
    }

    var bytes = new byte[idx * sizeof(float)];
    Buffer.BlockCopy(heights, 0, bytes, 0, bytes.Length);
    return Results.Bytes(bytes, "application/octet-stream");
}).WithName("GetTerrainTile");

// Camera state – GET returns current state, PUT updates it.
// The server automatically derives the view mode from the supplied altitude so
// the UE camera manager can query the expected mode on startup.
app.MapGet("/api/unreal/camera", (CameraStateService cam) =>
    Results.Ok(cam.State)
).WithName("GetCameraState");

app.MapPut("/api/unreal/camera", (CameraState state, CameraStateService cam) =>
{
    // Enforce the first-person threshold server-side so any client stays consistent.
    state.Mode = state.AltitudeKm < CameraMode.FirstPersonThresholdKm
        ? CameraMode.FirstPerson
        : CameraMode.Orbit;
    cam.State = state;
    return Results.Ok(cam.State);
}).WithName("UpdateCameraState");

app.MapGet("/api/weather/monthly", (int month, SimulationOrchestrator sim) =>
{
    var result = WeatherPatternService.ComputeMonthly(month, sim.State);
    return Results.Ok(result);
}).WithName("GetWeatherPattern");

// ── Feature Labels (Phase L2) ─────────────────────────────────────────────────

app.MapGet("/api/state/features", (SimulationOrchestrator sim, long? tick) =>
{
    var registry = sim.GetFeatureRegistry();
    if (tick.HasValue)
    {
        // Return feature state as of the requested tick (historical snapshot)
        var historical = new Dictionary<string, object>();
        foreach (var (id, feat) in registry.Features)
        {
            var snap = feat.History.LastOrDefault(s =>
                s.SimTickCreated <= tick.Value && s.SimTickExtinct > tick.Value);
            if (snap != null)
                historical[id] = new { feat.Id, feat.Type, snapshot = snap, feat.Metrics };
        }
        return Results.Ok(historical);
    }
    return Results.Ok(registry);
}).WithName("GetFeatures");

app.MapGet("/api/state/features/{id}", (string id, SimulationOrchestrator sim) =>
{
    var registry = sim.GetFeatureRegistry();
    return registry.Features.TryGetValue(id, out var feature)
        ? Results.Ok(feature)
        : Results.NotFound($"Feature '{id}' not found");
}).WithName("GetFeatureById");

app.MapGet("/api/state/features/{id}/history", (string id, SimulationOrchestrator sim) =>
{
    var registry = sim.GetFeatureRegistry();
    return registry.Features.TryGetValue(id, out var feature)
        ? Results.Ok(feature.History)
        : Results.NotFound($"Feature '{id}' not found");
}).WithName("GetFeatureHistory");

// ── Feature Labels compact list (Phase L5) ────────────────────────────────────
// Returns a minimal label payload for frontend rendering. zoomLevel is computed
// server-side from AreaKm2 so the frontend has zero business logic.
app.MapGet("/api/state/features/labels", (SimulationOrchestrator sim) =>
{
    var registry = sim.GetFeatureRegistry();
    var labels = registry.Features.Values
        .Where(f => f.History.Count > 0 && f.Current.Status != FeatureStatus.Extinct)
        .Select(f =>
        {
            var area = f.Current.AreaKm2;
            // zoomLevel = maximum camera distance at which this label is visible.
            // Camera starts at 3.0 (globe radius = 1); larger area → visible further away.
            var zoomLevel = area > 10_000_000f ? 4.0f
                          : area > 1_000_000f  ? 3.5f
                          : area > 100_000f    ? 2.5f
                          : area > 10_000f     ? 2.0f
                          : 1.8f;
            return new
            {
                id        = f.Id,
                name      = f.Current.Name,
                type      = f.Type.ToString(),
                centerLat = f.Current.CenterLat,
                centerLon = f.Current.CenterLon,
                zoomLevel,
                status    = f.Current.Status.ToString(),
            };
        })
        .ToList();
    return Results.Ok(labels);
}).WithName("GetFeatureLabels");

app.Run();

// ── Request DTOs ──────────────────────────────────────────────────────────────

record GenerateRequest(uint Seed = 0);
record AdvanceRequest(double DeltaMa);
record PointDto(double Lat, double Lon);
record CrossSectionRequest(List<PointDto> Points);
record RestoreSnapshotRequest(double TargetTimeMa);
record AdaptiveResolutionRequest(bool Enabled);

// ── Make Program class accessible for integration tests ──
namespace GeoTime.Api
{
    public partial class Program { }
}
