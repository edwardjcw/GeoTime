using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GeoTime.Tests;

public class ApiIntegrationTests(WebApplicationFactory<GeoTime.Api.Program> factory)
    : IClassFixture<WebApplicationFactory<GeoTime.Api.Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    // ── Planet Generation ─────────────────────────────────────────────────────

    [Fact]
    public async Task GeneratePlanet_ReturnsOkWithSeed()
    {
        var response = await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(42u, json.GetProperty("seed").GetUInt32());
        Assert.True(json.GetProperty("plateCount").GetInt32() > 0);
        Assert.True(json.GetProperty("hotspotCount").GetInt32() > 0);
    }

    [Fact]
    public async Task GeneratePlanet_RandomSeedWhenZero()
    {
        var response = await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 0u });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("seed").GetUInt32() > 0);
    }

    // ── Simulation Advance ────────────────────────────────────────────────────

    [Fact]
    public async Task AdvanceSimulation_MovesTimeForward()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var timeBefore = await GetTimeMa();

        var response = await _client.PostAsJsonAsync("/api/simulation/advance", new { deltaMa = 10.0 });
        response.EnsureSuccessStatusCode();

        var timeAfter = await GetTimeMa();
        Assert.True(timeAfter > timeBefore);
    }

    // ── Simulation Time ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetSimulationTime_ReturnsTimeAndSeed()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/simulation/time");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("timeMa", out _));
        Assert.True(json.TryGetProperty("seed", out _));
    }

    // ── State Maps ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHeightMap_ReturnsArray()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/heightmap");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<float[]>();
        Assert.NotNull(data);
        Assert.True(data.Length > 0);
    }

    [Fact]
    public async Task GetPlateMap_ReturnsArray()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/platemap");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<int[]>();
        Assert.NotNull(data);
        Assert.True(data.Length > 0);
    }

    [Fact]
    public async Task GetTemperatureMap_ReturnsArray()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/temperaturemap");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<float[]>();
        Assert.NotNull(data);
        Assert.True(data.Length > 0);
    }

    [Fact]
    public async Task GetPrecipitationMap_ReturnsArray()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/precipitationmap");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<float[]>();
        Assert.NotNull(data);
        Assert.True(data.Length > 0);
    }

    [Fact]
    public async Task GetBiomassMap_ReturnsArray()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/biomassmap");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<float[]>();
        Assert.NotNull(data);
        Assert.True(data.Length > 0);
    }

    [Fact]
    public async Task GetBiomatterMap_ReturnsArray()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/biomattermap");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<float[]>();
        Assert.NotNull(data);
        Assert.True(data.Length > 0);
    }

    [Fact]
    public async Task GetOrganicCarbonMap_ReturnsArray()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/organiccarbonmap");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<float[]>();
        Assert.NotNull(data);
        Assert.True(data.Length > 0);
    }

    // ── Plates, Hotspots, Atmosphere ──────────────────────────────────────────

    [Fact]
    public async Task GetPlates_ReturnsPlateData()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/plates");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, json.ValueKind);
        Assert.True(json.GetArrayLength() > 0);
    }

    [Fact]
    public async Task GetHotspots_ReturnsData()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/hotspots");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, json.ValueKind);
    }

    [Fact]
    public async Task GetAtmosphere_ReturnsComposition()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/atmosphere");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("n2", out _));
    }

    // ── Events ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetEvents_ReturnsArray()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/events");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, json.ValueKind);
    }

    [Fact]
    public async Task GetEvents_WithCount_LimitsResults()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        await _client.PostAsJsonAsync("/api/simulation/advance", new { deltaMa = 5.0 });
        var response = await _client.GetAsync("/api/state/events?count=2");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetArrayLength() <= 2);
    }

    // ── Cell Inspection ───────────────────────────────────────────────────────

    [Fact]
    public async Task InspectCell_ValidIndex_ReturnsData()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/inspect/0");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("cellIndex").GetInt32());
        Assert.True(json.TryGetProperty("biomatterDensity", out _));
        Assert.True(json.TryGetProperty("organicCarbon", out _));
        Assert.True(json.TryGetProperty("reefPresent", out _));
    }

    [Fact]
    public async Task InspectCell_InvalidIndex_ReturnsNotFound()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/inspect/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Cross-Section ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CrossSection_ValidPath_ReturnsProfile()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.PostAsJsonAsync("/api/crosssection", new
        {
            points = new[] { new { lat = 0.0, lon = 0.0 }, new { lat = 45.0, lon = 90.0 } }
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("totalDistanceKm").GetDouble() > 0);
    }

    // ── MessagePack Binary Endpoints ──────────────────────────────────────────

    [Fact]
    public async Task GetHeightMapBinary_ReturnsMsgpack()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/heightmap/binary");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/x-msgpack", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public async Task GetPlateMapBinary_ReturnsMsgpack()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/platemap/binary");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/x-msgpack", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetTemperatureMapBinary_ReturnsMsgpack()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/temperaturemap/binary");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/x-msgpack", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetPrecipitationMapBinary_ReturnsMsgpack()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/precipitationmap/binary");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/x-msgpack", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetBiomassMapBinary_ReturnsMsgpack()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/biomassmap/binary");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/x-msgpack", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetBiomatterMapBinary_ReturnsMsgpack()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/biomattermap/binary");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/x-msgpack", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetOrganicCarbonMapBinary_ReturnsMsgpack()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/organiccarbonmap/binary");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/x-msgpack", response.Content.Headers.ContentType?.MediaType);
    }

    // ── Snapshot Endpoints ────────────────────────────────────────────────────

    [Fact]
    public async Task TakeSnapshot_ReturnsSnapshotInfo()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.PostAsync("/api/snapshots/take", null);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("snapshotCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task ListSnapshots_ReturnsTimeList()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        await _client.PostAsync("/api/snapshots/take", null);
        var response = await _client.GetAsync("/api/snapshots");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("count").GetInt32() >= 1);
    }

    [Fact]
    public async Task RestoreSnapshot_RestoresState()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        await _client.PostAsync("/api/snapshots/take", null);

        var timeBefore = await GetTimeMa();
        await _client.PostAsJsonAsync("/api/simulation/advance", new { deltaMa = 10.0 });

        var response = await _client.PostAsJsonAsync("/api/snapshots/restore",
            new { targetTimeMa = timeBefore + 1 });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("restoredTimeMa", out _));
    }

    [Fact]
    public async Task RestoreSnapshot_NoSnapshot_ReturnsNotFound()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.PostAsJsonAsync("/api/snapshots/restore",
            new { targetTimeMa = -99999.0 });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<double> GetTimeMa()
    {
        var response = await _client.GetAsync("/api/simulation/time");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("timeMa").GetDouble();
    }
}
