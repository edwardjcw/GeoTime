using GeoTime.Api;
using GeoTime.Core;
using GeoTime.Core.Kernel;
using GeoTime.Core.Models;
using MessagePack;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton<SimulationOrchestrator>();
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
    uint seed = req.Seed > 0 ? req.Seed : (uint)(Random.Shared.Next(1, int.MaxValue));
    var result = sim.GeneratePlanet(seed);
    return Results.Ok(new
    {
        seed = result.Seed,
        plateCount = result.Plates.Count,
        hotspotCount = result.Hotspots.Count,
        timeMa = sim.GetCurrentTime(),
    });
}).WithName("GeneratePlanet");

app.MapPost("/api/simulation/advance", (AdvanceRequest req, SimulationOrchestrator sim) =>
{
    sim.AdvanceSimulation(req.DeltaMa);
    return Results.Ok(new { timeMa = sim.GetCurrentTime() });
}).WithName("AdvanceSimulation");

app.MapGet("/api/simulation/time", (SimulationOrchestrator sim) =>
    Results.Ok(new { timeMa = sim.GetCurrentTime(), seed = sim.GetCurrentSeed() })
).WithName("GetSimulationTime");

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
    byte[] packed = MessagePackSerializer.Serialize(sim.State.HeightMap);
    return Results.Bytes(packed, "application/x-msgpack");
}).WithName("GetHeightMapBinary");

app.MapGet("/api/state/platemap/binary", (SimulationOrchestrator sim) =>
{
    byte[] packed = MessagePackSerializer.Serialize(sim.State.PlateMap);
    return Results.Bytes(packed, "application/x-msgpack");
}).WithName("GetPlateMapBinary");

app.MapGet("/api/state/temperaturemap/binary", (SimulationOrchestrator sim) =>
{
    byte[] packed = MessagePackSerializer.Serialize(sim.State.TemperatureMap);
    return Results.Bytes(packed, "application/x-msgpack");
}).WithName("GetTemperatureMapBinary");

app.MapGet("/api/state/precipitationmap/binary", (SimulationOrchestrator sim) =>
{
    byte[] packed = MessagePackSerializer.Serialize(sim.State.PrecipitationMap);
    return Results.Bytes(packed, "application/x-msgpack");
}).WithName("GetPrecipitationMapBinary");

app.MapGet("/api/state/biomassmap/binary", (SimulationOrchestrator sim) =>
{
    byte[] packed = MessagePackSerializer.Serialize(sim.State.BiomassMap);
    return Results.Bytes(packed, "application/x-msgpack");
}).WithName("GetBiomassMapBinary");

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
    byte[] packed = MessagePackSerializer.Serialize(delta);
    return Results.Bytes(packed, "application/x-msgpack");
}).WithName("GetSnapshotDelta");

app.Run();

// ── Request DTOs ──────────────────────────────────────────────────────────────

record GenerateRequest(uint Seed = 0);
record AdvanceRequest(double DeltaMa);
record PointDto(double Lat, double Lon);
record CrossSectionRequest(List<PointDto> Points);
record RestoreSnapshotRequest(double TargetTimeMa);

// ── Make Program class accessible for integration tests ──
namespace GeoTime.Api
{
    public partial class Program { }
}
