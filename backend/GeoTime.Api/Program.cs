using GeoTime.Api;
using GeoTime.Api.Llm;
using GeoTime.Core;
using GeoTime.Core.Compute;
using GeoTime.Core.Engines;
using GeoTime.Core.Kernel;
using GeoTime.Core.Models;
using GeoTime.Core.Services;
using MessagePack;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton<SimulationOrchestrator>();
builder.Services.AddSingleton<CameraStateService>();
builder.Services.AddSingleton<GeologicalContextAssembler>(sp =>
    new GeologicalContextAssembler(sp.GetRequiredService<SimulationOrchestrator>()));
builder.Services.AddSignalR();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

// ── LLM Provider Services (Phase D3) ─────────────────────────────────────────
builder.Services.AddHttpClient();
builder.Services.AddSingleton<LlmSettingsService>();
builder.Services.AddSingleton<GeminiProvider>(sp =>
    new GeminiProvider(sp.GetRequiredService<LlmSettingsService>(),
                       sp.GetRequiredService<IHttpClientFactory>().CreateClient("Gemini")));
builder.Services.AddSingleton<OpenAiProvider>(sp =>
    new OpenAiProvider(sp.GetRequiredService<LlmSettingsService>(),
                       sp.GetRequiredService<IHttpClientFactory>().CreateClient("OpenAi")));
builder.Services.AddSingleton<AnthropicProvider>(sp =>
    new AnthropicProvider(sp.GetRequiredService<LlmSettingsService>(),
                          sp.GetRequiredService<IHttpClientFactory>().CreateClient("Anthropic")));
builder.Services.AddSingleton<OllamaProvider>(sp =>
    new OllamaProvider(sp.GetRequiredService<LlmSettingsService>(),
                       sp.GetRequiredService<IHttpClientFactory>().CreateClient("Ollama")));
builder.Services.AddSingleton<LlamaSharpProvider>(sp =>
    new LlamaSharpProvider(sp.GetRequiredService<LlmSettingsService>()));
builder.Services.AddSingleton<TemplateFallbackProvider>();
builder.Services.AddSingleton<LlmProviderFactory>(sp => new LlmProviderFactory(
    sp.GetRequiredService<LlmSettingsService>(),
    new ILlmProvider[]
    {
        sp.GetRequiredService<GeminiProvider>(),
        sp.GetRequiredService<OpenAiProvider>(),
        sp.GetRequiredService<AnthropicProvider>(),
        sp.GetRequiredService<OllamaProvider>(),
        sp.GetRequiredService<LlamaSharpProvider>(),
        sp.GetRequiredService<TemplateFallbackProvider>(),
    }));
builder.Services.AddSingleton<LocalLlmSetupService>(sp => new LocalLlmSetupService(
    sp.GetRequiredService<LlmSettingsService>(),
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("Setup"),
    sp.GetRequiredService<LlamaSharpProvider>()));

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
        tickCount = sim.TickCount,
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

// ── LLM Settings Endpoints (Phase D3) ────────────────────────────────────────

// GET /api/llm/providers — list all providers with current status
app.MapGet("/api/llm/providers", async (LlmProviderFactory factory, LlmSettingsService settings) =>
{
    var activeProvider = settings.ActiveProvider;
    var tasks = factory.GetAllProviders().Select(async p =>
    {
        var status = await p.GetStatusAsync();
        var cfg = settings.ProviderConfigs.TryGetValue(p.Name, out var c) ? c : new ProviderSettings();
        return new
        {
            name         = p.Name,
            displayName  = p.Name,
            isAvailable  = p.IsAvailable,
            needsSetup   = status.NeedsSetup,
            activeModel  = cfg.Model,
            statusMessage = status.StatusMessage,
            isActive     = p.Name.Equals(activeProvider, StringComparison.OrdinalIgnoreCase),
        };
    });
    var results = await Task.WhenAll(tasks);
    return Results.Ok(results);
}).WithName("GetLlmProviders");

// GET /api/llm/active — return active provider name and its settings (key redacted)
app.MapGet("/api/llm/active", (LlmSettingsService settings) =>
{
    var name = settings.ActiveProvider;
    var cfg  = settings.ProviderConfigs.TryGetValue(name, out var c) ? c : new ProviderSettings();
    return Results.Ok(new
    {
        provider = name,
        model    = cfg.Model,
        baseUrl  = cfg.BaseUrl,
        hasApiKey = !string.IsNullOrWhiteSpace(cfg.ApiKey),
    });
}).WithName("GetActiveLlmProvider");

// PUT /api/llm/active — update active provider + config at runtime
app.MapPut("/api/llm/active", (LlmActiveRequest req, LlmSettingsService settings) =>
{
    settings.SetActiveProvider(req.Provider);
    if (req.Settings != null)
        settings.UpdateProviderConfig(req.Provider, req.Settings);
    settings.Save();
    return Results.Ok(new { provider = settings.ActiveProvider });
}).WithName("SetActiveLlmProvider");

// POST /api/llm/setup/{provider} — start local provider setup (Ollama or LlamaSharp)
app.MapPost("/api/llm/setup/{provider}", (string provider, LocalLlmSetupService setup) =>
{
    var localProviders = new[] { "Ollama", "LlamaSharp" };
    if (!localProviders.Any(p => p.Equals(provider, StringComparison.OrdinalIgnoreCase)))
        return Results.BadRequest($"Setup is only available for local providers: {string.Join(", ", localProviders)}");

    setup.StartSetup(provider);
    return Results.Accepted($"/api/llm/setup/{provider}/progress",
        new { provider, message = "Setup started. Monitor progress at the SSE endpoint." });
}).WithName("StartLlmSetup");

// GET /api/llm/setup/{provider}/progress — SSE stream of LlmSetupProgress events
app.MapGet("/api/llm/setup/{provider}/progress", async (string provider, LocalLlmSetupService setup,
    HttpContext ctx, CancellationToken ct) =>
{
    var reader = setup.GetProgressReader(provider);
    if (reader == null)
    {
        ctx.Response.StatusCode = 404;
        await ctx.Response.WriteAsync("No setup in progress for this provider.", ct);
        return;
    }

    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection   = "keep-alive";

    await foreach (var progress in reader.ReadAllAsync(ct))
    {
        var json = System.Text.Json.JsonSerializer.Serialize(progress);
        await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
        if (progress.IsComplete || progress.IsError) break;
    }
}).WithName("GetLlmSetupProgress");

// ── Description API (Phase D5) ────────────────────────────────────────────────

// POST /api/describe — assemble geological context and generate prose description
app.MapPost("/api/describe", async (
    DescriptionRequest req,
    GeologicalContextAssembler assembler,
    LlmProviderFactory factory,
    CancellationToken ct) =>
{
    var ctx = await assembler.AssembleAsync(req.CellIndex);
    if (ctx == null) return Results.BadRequest("Cell index out of range or planet not generated.");

    var primaryFeature = ctx.PrimaryLandFeature ?? ctx.PrimaryWaterFeature;
    var title    = primaryFeature?.Current.Name ?? $"Cell {req.CellIndex}";
    var subtitle = primaryFeature != null
        ? $"{primaryFeature.Type} — {ctx.SimAgeDescription}"
        : ctx.SimAgeDescription;

    // Build stats (no LLM needed)
    var stats = new List<DescriptionStat>
    {
        new() { Label = "Location",      Value = $"{ctx.Lat:F2}°, {ctx.Lon:F2}°" },
        new() { Label = "Elevation",     Value = $"{ctx.Cell.Height:F0} m" },
        new() { Label = "Rock Age",      Value = $"{ctx.Cell.RockAge:F1} Ma" },
        new() { Label = "Temperature",   Value = $"{ctx.MeanTempC:F1} °C" },
        new() { Label = "Precipitation", Value = $"{ctx.MeanPrecipMm:F0} mm/yr" },
        new() { Label = "Biome",         Value = ctx.BiomeType },
        new() { Label = "Plate",         Value = $"Plate {ctx.CurrentPlate?.Id.ToString() ?? "—"}" },
        new() { Label = "Margin Type",   Value = ctx.NearestMarginType.ToString() },
    };

    // Stratigraphic summary (no LLM needed)
    var strat = ctx.Column.Layers
        .OrderByDescending(l => l.AgeDeposited)
        .Take(10)
        .Select(l => new StratigraphicSummaryRow
        {
            Age       = $"{l.AgeDeposited:F1} Ma",
            Thickness = $"{l.Thickness:F2} m",
            RockType  = l.RockType.ToString(),
            EventNote = l.EventType != LayerEventType.Normal ? l.EventType.ToString() : "",
        })
        .ToList();

    // History timeline (no LLM needed), ordered by tick ascending
    var history = ctx.PrimaryFeatureHistory
        .OrderBy(s => s.SimTickCreated)
        .Select(s => new HistoryTimelineEntry
        {
            SimTick = s.SimTickCreated,
            Event   = s.SplitFromId != null ? "split" : s.MergedIntoId != null ? "merged" : "snapshot",
            Name    = s.Name,
        })
        .ToList();

    // Generate prose paragraphs
    string[] paragraphs;
    string providerUsed;

    var provider = factory.GetActiveProvider();
    if (provider is TemplateFallbackProvider || provider.Name == "Template")
    {
        // Use rich template engine (Phase D4) directly for best output
        var paras = DescriptionTemplateEngine.Generate(ctx);
        paragraphs  = paras.ToArray();
        providerUsed = "Template";
    }
    else
    {
        // Use configured LLM provider; fall back to template on any provider error
        try
        {
            var systemPrompt = DescriptionPromptComposer.ComposeSystemPrompt();
            var userPrompt   = DescriptionPromptComposer.ComposeUserPrompt(ctx);
            var prose        = await provider.GenerateAsync(systemPrompt, userPrompt, ct);

            // Try to parse as JSON paragraphs array; otherwise treat as single block
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(prose);
                if (doc.RootElement.TryGetProperty("paragraphs", out var arr))
                {
                    paragraphs = arr.EnumerateArray()
                        .Select(e => e.GetString() ?? "")
                        .Where(s => s.Length > 0)
                        .ToArray();
                }
                else
                {
                    paragraphs = [prose];
                }
            }
            catch
            {
                // Not JSON — split on double newlines as paragraph separator
                paragraphs = prose
                    .Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => p.Length > 0)
                    .ToArray();
                if (paragraphs.Length == 0) paragraphs = [prose];
            }
            providerUsed = provider.Name;
        }
        catch
        {
            // Provider unavailable at runtime — fall back to template engine
            var paras = DescriptionTemplateEngine.Generate(ctx);
            paragraphs  = paras.ToArray();
            providerUsed = "Template";
        }
    }

    return Results.Ok(new DescriptionResponse
    {
        Title                = title,
        Subtitle             = subtitle,
        Paragraphs           = paragraphs,
        Stats                = stats,
        StratigraphicSummary = strat,
        HistoryTimeline      = history,
        ProviderUsed         = providerUsed,
    });
}).WithName("DescribeCell");

// POST /api/describe/stream — SSE streaming variant for LLM providers that support it
app.MapPost("/api/describe/stream", async (
    DescriptionRequest req,
    GeologicalContextAssembler assembler,
    LlmProviderFactory factory,
    HttpContext httpCtx,
    CancellationToken ct) =>
{
    var ctx = await assembler.AssembleAsync(req.CellIndex);
    if (ctx == null)
    {
        httpCtx.Response.StatusCode = 400;
        await httpCtx.Response.WriteAsync("Cell index out of range.", ct);
        return;
    }

    httpCtx.Response.ContentType     = "text/event-stream";
    httpCtx.Response.Headers.CacheControl = "no-cache";
    httpCtx.Response.Headers.Connection   = "keep-alive";

    var provider = factory.GetActiveProvider();
    IAsyncEnumerable<string> tokens;

    if (provider is TemplateFallbackProvider || provider.Name == "Template")
    {
        var paras = DescriptionTemplateEngine.Generate(ctx);
        tokens = StringsToAsyncEnumerable(paras);
    }
    else
    {
        var systemPrompt = DescriptionPromptComposer.ComposeSystemPrompt();
        var userPrompt   = DescriptionPromptComposer.ComposeUserPrompt(ctx);
        tokens = provider.StreamAsync(systemPrompt, userPrompt, ct);
    }

    await foreach (var token in tokens.WithCancellation(ct))
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { token });
        await httpCtx.Response.WriteAsync($"data: {json}\n\n", ct);
        await httpCtx.Response.Body.FlushAsync(ct);
    }

    await httpCtx.Response.WriteAsync("data: {\"done\":true}\n\n", ct);
    await httpCtx.Response.Body.FlushAsync(ct);
}).WithName("DescribeCellStream");

// ── Event Layer Map API (Phase D6) ────────────────────────────────────────────

// GET /api/state/eventlayermap?eventType=ImpactEjecta&tick=N
// Returns float[] (one value per cell = total thickness of that event type up to tick N)
app.MapGet("/api/state/eventlayermap", (
    string? eventType,
    long? tick,
    SimulationOrchestrator sim) =>
{
    if (sim.State.CellCount == 0) return Results.BadRequest("Planet not generated.");

    var filterType = LayerEventType.Normal;
    if (!string.IsNullOrWhiteSpace(eventType))
    {
        if (!Enum.TryParse<LayerEventType>(eventType, ignoreCase: true, out filterType))
            return Results.BadRequest($"Unknown event type: {eventType}");
    }

    var strat = sim.GetStratigraphicColumns();
    if (strat == null) return Results.Ok(new float[sim.State.CellCount]);

    var result = new float[sim.State.CellCount];
    for (int i = 0; i < sim.State.CellCount; i++)
    {
        var col = strat[i];
        if (col == null) continue;
        result[i] = (float)col.Layers
            .Where(l => l.EventType == filterType)
            .Sum(l => l.Thickness);
    }
    return Results.Ok(result);
}).WithName("GetEventLayerMap");

// GET /api/state/eventlayermap/types
// Returns LayerEventType enum values that have at least one layer anywhere on the planet
app.MapGet("/api/state/eventlayermap/types", (SimulationOrchestrator sim) =>
{
    var strat = sim.GetStratigraphicColumns();
    if (strat == null) return Results.Ok(Array.Empty<string>());

    var present = new HashSet<string>();
    foreach (var col in strat)
    {
        if (col == null) continue;
        foreach (var layer in col.Layers)
        {
            if (layer.EventType != LayerEventType.Normal)
                present.Add(layer.EventType.ToString());
        }
    }
    return Results.Ok(present.OrderBy(x => x));
}).WithName("GetEventLayerTypes");

app.Run();

// Helper: yield strings as IAsyncEnumerable<string>
static async IAsyncEnumerable<string> StringsToAsyncEnumerable(IEnumerable<string> items)
{
    foreach (var s in items)
    {
        yield return s;
        await Task.Yield();
    }
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

record GenerateRequest(uint Seed = 0);
record AdvanceRequest(double DeltaMa);
record PointDto(double Lat, double Lon);
record CrossSectionRequest(List<PointDto> Points);
record RestoreSnapshotRequest(double TargetTimeMa);
record AdaptiveResolutionRequest(bool Enabled);
record LlmActiveRequest(string Provider, GeoTime.Api.Llm.ProviderSettings? Settings);

// ── Make Program class accessible for integration tests ──
namespace GeoTime.Api
{
    public partial class Program { }
}
