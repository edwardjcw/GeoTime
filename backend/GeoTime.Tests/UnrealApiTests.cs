using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GeoTime.Core.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GeoTime.Tests;

public class UnrealApiTests(WebApplicationFactory<GeoTime.Api.Program> factory)
    : IClassFixture<WebApplicationFactory<GeoTime.Api.Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    // ── Terrain Meta ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTerrainMeta_BeforeGenerate_ReturnsDefaultGridSize()
    {
        var response = await _client.GetAsync("/api/unreal/terrain-meta");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Grid size is always set (default = 512).
        Assert.True(json.GetProperty("gridSize").GetInt32() > 0);
        Assert.True(json.GetProperty("cellCount").GetInt32() > 0);
        Assert.True(json.GetProperty("cellSizeCm").GetSingle() > 0);
        Assert.True(json.GetProperty("maxHeightCm").GetSingle() > 0);
        Assert.True(json.GetProperty("minHeightCm").GetSingle() < 0);
        Assert.True(json.GetProperty("firstPersonThresholdKm").GetSingle() > 0);
    }

    [Fact]
    public async Task GetTerrainMeta_AfterGenerate_MatchesGridSize()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 99u });
        var response = await _client.GetAsync("/api/unreal/terrain-meta");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(512, json.GetProperty("gridSize").GetInt32());
        Assert.Equal(512 * 512, json.GetProperty("cellCount").GetInt32());
    }

    // ── Heightmap Raw ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHeightMapRaw_AfterGenerate_ReturnsBinaryFloats()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/unreal/heightmap-raw");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        // 512×512 cells × 4 bytes per float
        Assert.Equal(512 * 512 * sizeof(float), bytes.Length);
    }

    // ── Terrain Tile ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTerrainTile_ValidTile_ReturnsBinaryFloats()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var response = await _client.GetAsync("/api/unreal/terrain-tile/0/0/0");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        // Tile 0,0 LOD 0: 64×64 floats
        Assert.Equal(64 * 64 * sizeof(float), bytes.Length);
    }

    [Fact]
    public async Task GetTerrainTile_Lod1_ReturnsHalfResolution()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        var lod0 = await _client.GetAsync("/api/unreal/terrain-tile/0/0/0");
        var lod1 = await _client.GetAsync("/api/unreal/terrain-tile/0/0/1");
        lod0.EnsureSuccessStatusCode();
        lod1.EnsureSuccessStatusCode();
        var bytes0 = await lod0.Content.ReadAsByteArrayAsync();
        var bytes1 = await lod1.Content.ReadAsByteArrayAsync();
        // LOD 1 should have fewer bytes than LOD 0.
        Assert.True(bytes1.Length < bytes0.Length);
    }

    [Fact]
    public async Task GetTerrainTile_OutOfRange_ReturnsBadRequest()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        // tileX=100 → originX=6400, well beyond 512.
        var response = await _client.GetAsync("/api/unreal/terrain-tile/100/100/0");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Camera State ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCameraState_ReturnsValidShape()
    {
        var response = await _client.GetAsync("/api/unreal/camera");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("mode", out var modeProp));
        var mode = modeProp.GetString();
        Assert.True(mode is "orbit" or "firstperson");
        Assert.True(json.TryGetProperty("altitudeKm", out _));
        Assert.True(json.TryGetProperty("lat", out _));
        Assert.True(json.TryGetProperty("lon", out _));
        Assert.True(json.TryGetProperty("heading", out _));
        Assert.True(json.TryGetProperty("pitch", out _));
    }

    [Fact]
    public async Task UpdateCameraState_OrbitAltitude_KeepsOrbitMode()
    {
        var response = await _client.PutAsJsonAsync("/api/unreal/camera", new
        {
            mode = "orbit",
            lat = 10.0,
            lon = 20.0,
            altitudeKm = 500.0,
            heading = 45.0,
            pitch = -30.0,
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("orbit", json.GetProperty("mode").GetString());
        Assert.Equal(500.0, json.GetProperty("altitudeKm").GetDouble(), precision: 3);
    }

    [Fact]
    public async Task UpdateCameraState_BelowThreshold_SwitchesToFirstPerson()
    {
        // Altitude below 0.1 km (100 m) should trigger first-person mode.
        var response = await _client.PutAsJsonAsync("/api/unreal/camera", new
        {
            mode = "orbit", // client sends "orbit" but server should override
            lat = 0.0,
            lon = 0.0,
            altitudeKm = 0.05, // 50 m – below threshold
            heading = 0.0,
            pitch = 0.0,
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("firstperson", json.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task UpdateCameraState_AtExactThreshold_KeepsOrbitMode()
    {
        var response = await _client.PutAsJsonAsync("/api/unreal/camera", new
        {
            mode = "orbit",
            lat = 0.0,
            lon = 0.0,
            altitudeKm = 0.1, // exactly at the 0.1 km threshold → stays orbit (not strictly less)
            heading = 0.0,
            pitch = 0.0,
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        // 0.1 is NOT strictly less than threshold (0.1), so mode must be orbit.
        Assert.Equal("orbit", json.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task CameraState_Persists_BetweenRequests()
    {
        await _client.PutAsJsonAsync("/api/unreal/camera", new
        {
            lat = 42.0,
            lon = -71.0,
            altitudeKm = 300.0,
            heading = 90.0,
            pitch = -60.0,
        });

        var response = await _client.GetAsync("/api/unreal/camera");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(42.0, json.GetProperty("lat").GetDouble(), precision: 3);
        Assert.Equal(-71.0, json.GetProperty("lon").GetDouble(), precision: 3);
    }
}
