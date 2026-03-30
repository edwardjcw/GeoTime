using GeoTime.Core;
using GeoTime.Core.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton<SimulationOrchestrator>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

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

app.Run();

// ── Request DTOs ──────────────────────────────────────────────────────────────

record GenerateRequest(uint Seed = 0);
record AdvanceRequest(double DeltaMa);
record PointDto(double Lat, double Lon);
record CrossSectionRequest(List<PointDto> Points);
