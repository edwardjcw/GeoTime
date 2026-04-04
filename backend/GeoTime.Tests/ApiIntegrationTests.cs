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

    [Fact]
    public async Task GetSoilMap_ReturnsArray()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/soilmap");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<int[]>();
        Assert.NotNull(data);
        Assert.True(data.Length > 0);
    }

    [Fact]
    public async Task GetSoilMap_ContainsValidSoilOrders()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/soilmap");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<int[]>();
        Assert.NotNull(data);
        // All soil order values should be in range [0, 12]
        Assert.All(data, v => Assert.InRange(v, 0, 12));
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

    // ── State Bundle Binary Endpoint ──────────────────────────────────────────

    [Fact]
    public async Task GetStateBundleBinary_ReturnsOctetStream()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/bundle/binary");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetStateBundleBinary_ReturnsThreeFloatArraysInOrder()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });

        // Fetch individual maps for comparison
        var heightJson = await _client.GetFromJsonAsync<float[]>("/api/state/heightmap");
        var tempJson   = await _client.GetFromJsonAsync<float[]>("/api/state/temperaturemap");
        var precipJson = await _client.GetFromJsonAsync<float[]>("/api/state/precipitationmap");
        Assert.NotNull(heightJson);
        Assert.NotNull(tempJson);
        Assert.NotNull(precipJson);

        // Fetch bundle
        var bundleResponse = await _client.GetAsync("/api/state/bundle/binary");
        bundleResponse.EnsureSuccessStatusCode();
        var bytes = await bundleResponse.Content.ReadAsByteArrayAsync();

        var cc = heightJson.Length;
        var floatBytes = cc * sizeof(float);
        Assert.Equal(floatBytes * 3, bytes.Length);

        // Decode each sub-array
        var heightOut = new float[cc];
        var tempOut   = new float[cc];
        var precipOut = new float[cc];
        Buffer.BlockCopy(bytes, 0,              heightOut, 0, floatBytes);
        Buffer.BlockCopy(bytes, floatBytes,     tempOut,   0, floatBytes);
        Buffer.BlockCopy(bytes, floatBytes * 2, precipOut, 0, floatBytes);

        // All three arrays should match the individual endpoints
        Assert.Equal(heightJson, heightOut);
        Assert.Equal(tempJson,   tempOut);
        Assert.Equal(precipJson, precipOut);
    }

    [Fact]
    public async Task GetStateBundleBinary_SizeMatchesCellCount()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var bytes = await (await _client.GetAsync("/api/state/bundle/binary"))
            .Content.ReadAsByteArrayAsync();

        // GeoTime uses a 512×512 grid (GridConstants.CELL_COUNT = 262_144)
        const int cc = GeoTime.Core.Models.GridConstants.CELL_COUNT;
        Assert.Equal(cc * sizeof(float) * 3, bytes.Length);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<double> GetTimeMa()
    {
        var response = await _client.GetAsync("/api/simulation/time");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("timeMa").GetDouble();
    }
}
