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

    // ── Planet Status ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPlanetStatus_ReturnsFalseBeforeGeneration()
    {
        var response = await _client.GetAsync("/api/planet/status");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("exists").GetBoolean());
    }

    [Fact]
    public async Task GetPlanetStatus_ReturnsTrueAfterGeneration()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/planet/status");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("exists").GetBoolean());
        Assert.Equal(42u, json.GetProperty("seed").GetUInt32());
        Assert.True(json.TryGetProperty("timeMa", out _));
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

    // ── Compute Info Endpoint (Rec 5-6) ──────────────────────────────────────

    [Fact]
    public async Task GetComputeInfo_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/simulation/compute-info");
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetComputeInfo_ContainsExpectedFields()
    {
        var response = await _client.GetAsync("/api/simulation/compute-info");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(json.TryGetProperty("mode", out var mode), "Should have 'mode' field");
        Assert.True(json.TryGetProperty("deviceName", out var device), "Should have 'deviceName' field");
        Assert.True(json.TryGetProperty("isGpu", out _), "Should have 'isGpu' field");
        Assert.False(string.IsNullOrEmpty(mode.GetString()), "mode should not be empty");
        Assert.False(string.IsNullOrEmpty(device.GetString()), "deviceName should not be empty");
    }

    [Fact]
    public async Task GetComputeInfo_ModeIsCpuOrGpu()
    {
        var response = await _client.GetAsync("/api/simulation/compute-info");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var mode = json.GetProperty("mode").GetString();
        Assert.True(mode is "CPU" or "GPU", $"mode should be CPU or GPU, got: {mode}");
    }

    // ── Adaptive Resolution Endpoints (Rec 7) ─────────────────────────────────

    [Fact]
    public async Task GetAdaptiveResolution_ReturnsEnabled()
    {
        var response = await _client.GetAsync("/api/simulation/adaptive-resolution");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("enabled", out _), "Should have 'enabled' field");
    }

    [Fact]
    public async Task SetAdaptiveResolution_CanToggle()
    {
        // Disable
        var disableResp = await _client.PostAsJsonAsync(
            "/api/simulation/adaptive-resolution", new { enabled = false });
        disableResp.EnsureSuccessStatusCode();
        var disableJson = await disableResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(disableJson.GetProperty("enabled").GetBoolean());

        // Re-enable
        var enableResp = await _client.PostAsJsonAsync(
            "/api/simulation/adaptive-resolution", new { enabled = true });
        enableResp.EnsureSuccessStatusCode();
        var enableJson = await enableResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(enableJson.GetProperty("enabled").GetBoolean());
    }

    // ── Feature Labels API (L2) ───────────────────────────────────────────────

    [Fact]
    public async Task GetFeatures_AfterGenerate_ReturnsRegistry()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/features");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("features", out var features));
        Assert.Equal(JsonValueKind.Object, features.ValueKind);
        Assert.True(features.EnumerateObject().Any(), "Expected at least one feature in registry");
    }

    [Fact]
    public async Task GetFeatures_ContainsLastUpdatedTick()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/features");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("lastUpdatedTick", out _));
    }

    [Fact]
    public async Task GetFeatureById_ReturnsFeature()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var regResp = await _client.GetAsync("/api/state/features");
        regResp.EnsureSuccessStatusCode();
        var registry = await regResp.Content.ReadFromJsonAsync<JsonElement>();
        var firstId = registry.GetProperty("features").EnumerateObject().First().Name;

        var featureResp = await _client.GetAsync($"/api/state/features/{firstId}");
        featureResp.EnsureSuccessStatusCode();
        var feature = await featureResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(feature.TryGetProperty("id", out _));
        Assert.True(feature.TryGetProperty("type", out _));
    }

    [Fact]
    public async Task GetFeatureById_UnknownId_ReturnsNotFound()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/features/nonexistent_id_xyz");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Phase L5: Feature Labels Endpoint ────────────────────────────────────

    [Fact]
    public async Task GetFeatureLabels_ReturnsCompactList()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/state/features/labels");
        response.EnsureSuccessStatusCode();
        var labels = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(labels);
        Assert.True(labels.Length > 0, "Should return at least one feature label");
        var first = labels[0];
        Assert.True(first.TryGetProperty("id", out _),        "Label must have id");
        Assert.True(first.TryGetProperty("name", out _),       "Label must have name");
        Assert.True(first.TryGetProperty("type", out _),       "Label must have type");
        Assert.True(first.TryGetProperty("centerLat", out _),  "Label must have centerLat");
        Assert.True(first.TryGetProperty("centerLon", out _),  "Label must have centerLon");
        Assert.True(first.TryGetProperty("zoomLevel", out _),  "Label must have zoomLevel");
        Assert.True(first.TryGetProperty("status", out _),     "Label must have status");
    }

    // ── Phase L6: Snapshot Persistence of FeatureRegistry ────────────────────

    [Fact]
    public async Task SaveAndRestoreSnapshot_PreservesFeatureNames()
    {
        // Generate a planet and capture the feature names.
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });

        var labelsBefore = await _client.GetAsync("/api/state/features/labels");
        labelsBefore.EnsureSuccessStatusCode();
        var beforeArray = await labelsBefore.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(beforeArray);
        Assert.True(beforeArray.Length > 0);

        // Collect feature names before save.
        var namesBefore = beforeArray
            .Select(l => l.GetProperty("name").GetString())
            .Where(n => n != null)
            .OrderBy(n => n)
            .ToArray();

        // Save snapshot.
        var saveResp = await _client.PostAsJsonAsync("/api/snapshots/take", new { });
        saveResp.EnsureSuccessStatusCode();

        // Advance simulation to change state.
        await _client.PostAsJsonAsync("/api/simulation/advance", new { deltaMa = 5.0 });

        // Restore the snapshot.
        var info = await _client.GetAsync("/api/snapshots");
        var infoJson = await info.Content.ReadFromJsonAsync<JsonElement>();
        var latestTime = infoJson.GetProperty("times").EnumerateArray().Max(t => t.GetDouble());
        var restoreResp = await _client.PostAsJsonAsync(
            "/api/snapshots/restore", new { targetTimeMa = latestTime });
        restoreResp.EnsureSuccessStatusCode();

        // Fetch labels after restore.
        var labelsAfter = await _client.GetAsync("/api/state/features/labels");
        labelsAfter.EnsureSuccessStatusCode();
        var afterArray = await labelsAfter.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(afterArray);

        var namesAfter = afterArray
            .Select(l => l.GetProperty("name").GetString())
            .Where(n => n != null)
            .OrderBy(n => n)
            .ToArray();

        // The restored registry should have the same feature names as before the save.
        Assert.Equal(namesBefore.Length, namesAfter.Length);
        for (int i = 0; i < namesBefore.Length; i++)
        {
            Assert.Equal(namesBefore[i], namesAfter[i]);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<double> GetTimeMa()
    {
        var response = await _client.GetAsync("/api/simulation/time");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("timeMa").GetDouble();
    }
}
