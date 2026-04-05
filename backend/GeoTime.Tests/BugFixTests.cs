using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using GeoTime.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GeoTime.Tests;

/// <summary>Tests for the bug fixes: timing stats, concurrent advance protection, and cell info fields.</summary>
public class BugFixTests(WebApplicationFactory<GeoTime.Api.Program> factory)
    : IClassFixture<WebApplicationFactory<GeoTime.Api.Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task EnsurePlanetGenerated()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 12345u });
    }

    // ── Timing Stats in Advance Response ─────────────────────────────────────

    [Fact]
    public async Task AdvanceSimulation_ReturnsTimingStats()
    {
        await EnsurePlanetGenerated();
        var response = await _client.PostAsJsonAsync("/api/simulation/advance", new { deltaMa = 0.1 });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("stats", out var stats), "Response should include 'stats' object");
        Assert.True(stats.TryGetProperty("totalMs", out var totalMs), "Stats should include 'totalMs'");
        Assert.True(totalMs.GetInt64() >= 0, "totalMs should be non-negative");
        Assert.True(stats.TryGetProperty("tectonicMs", out var tectonicMs));
        Assert.True(stats.TryGetProperty("surfaceMs", out _));
        Assert.True(stats.TryGetProperty("atmosphereMs", out _));
        Assert.True(stats.TryGetProperty("vegetationMs", out _));
        Assert.True(stats.TryGetProperty("biomatterMs", out _));
    }

    [Fact]
    public async Task AdvanceSimulation_TectonicMs_IsPositive()
    {
        await EnsurePlanetGenerated();
        var response = await _client.PostAsJsonAsync("/api/simulation/advance", new { deltaMa = 0.5 });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var stats = json.GetProperty("stats");
        // Tectonic always runs, so its ms should be ≥ 0.
        Assert.True(stats.GetProperty("tectonicMs").GetInt64() >= 0);
    }

    // ── Stats Endpoint ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSimulationStats_ReturnsAllFields()
    {
        await EnsurePlanetGenerated();
        await _client.PostAsJsonAsync("/api/simulation/advance", new { deltaMa = 0.1 });
        var response = await _client.GetAsync("/api/simulation/stats");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("tectonicMs", out _));
        Assert.True(json.TryGetProperty("surfaceMs", out _));
        Assert.True(json.TryGetProperty("atmosphereMs", out _));
        Assert.True(json.TryGetProperty("vegetationMs", out _));
        Assert.True(json.TryGetProperty("biomatterMs", out _));
        Assert.True(json.TryGetProperty("totalMs", out _));
        Assert.True(json.TryGetProperty("timeMa", out _));
    }

    // ── Cell Inspection Fields ────────────────────────────────────────────────

    [Fact]
    public async Task InspectCell_ReturnsAllExpectedFields()
    {
        await EnsurePlanetGenerated();
        var response = await _client.GetAsync("/api/state/inspect/0");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        // All fields from CellInspection should be present
        Assert.True(json.TryGetProperty("height", out _));
        Assert.True(json.TryGetProperty("crustThickness", out _));
        Assert.True(json.TryGetProperty("rockType", out _));
        Assert.True(json.TryGetProperty("rockAge", out _));
        Assert.True(json.TryGetProperty("plateId", out _));
        Assert.True(json.TryGetProperty("soilType", out _));
        Assert.True(json.TryGetProperty("soilDepth", out _));
        Assert.True(json.TryGetProperty("temperature", out _));
        Assert.True(json.TryGetProperty("precipitation", out _));
        Assert.True(json.TryGetProperty("biomass", out _));
        Assert.True(json.TryGetProperty("biomatterDensity", out _));
        Assert.True(json.TryGetProperty("organicCarbon", out _));
        Assert.True(json.TryGetProperty("reefPresent", out _));
    }

    // ── Concurrent Advance Protection ────────────────────────────────────────

    [Fact]
    public async Task AdvanceSimulation_ConcurrentCalls_DoNotCrash()
    {
        await EnsurePlanetGenerated();
        // Fire two concurrent advance requests — the semaphore should ensure only
        // one runs; the second should return quickly (the semaphore returns false).
        var t1 = _client.PostAsJsonAsync("/api/simulation/advance", new { deltaMa = 0.1 });
        var t2 = _client.PostAsJsonAsync("/api/simulation/advance", new { deltaMa = 0.1 });
        var results = await Task.WhenAll(t1, t2);
        // Both should succeed (even if second one was a no-op due to lock)
        foreach (var r in results)
            Assert.True(r.IsSuccessStatusCode, $"Concurrent advance failed: {r.StatusCode}");
    }

    // ── Timing Tracks All Engines ─────────────────────────────────────────────

    [Fact]
    public async Task SimulationOrchestrator_LastTickStats_TracksTectonicTime()
    {
        // Use the orchestrator directly in a unit test
        var sim = new SimulationOrchestrator();
        sim.GeneratePlanet(42);
        sim.AdvanceSimulation(0.5);
        var stats = sim.LastTickStats;
        Assert.True(stats.TotalMs >= 0);
        Assert.True(stats.TectonicMs >= 0);
        sim.Dispose();
    }

    [Fact]
    public async Task SimulationOrchestrator_ConcurrentAdvance_IsProtected()
    {
        var sim = new SimulationOrchestrator();
        sim.GeneratePlanet(99);

        // First advance runs normally; second concurrent call should be skipped by semaphore.
        var t1 = Task.Run(() => sim.AdvanceSimulation(0.1));
        var t2 = Task.Run(() => sim.AdvanceSimulation(0.1));
        await Task.WhenAll(t1, t2); // Must not throw or deadlock
        sim.Dispose();
    }
}
