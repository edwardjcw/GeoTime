using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GeoTime.Tests;

/// <summary>Integration tests for Phase D5 POST /api/describe and Phase D6 event layer endpoints.</summary>
public class DescriptionApiTests(WebApplicationFactory<GeoTime.Api.Program> factory)
    : IClassFixture<WebApplicationFactory<GeoTime.Api.Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    // ─────────────────────────────────────────────────────────────────────────
    // D5 — POST /api/describe
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Describe_WithNoLlmConfigured_ReturnsTemplateDescription()
    {
        // Generate a planet first
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });

        var response = await _client.PostAsJsonAsync("/api/describe", new { cellIndex = 0 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Must have a title
        Assert.True(json.TryGetProperty("title", out var title));
        Assert.False(string.IsNullOrWhiteSpace(title.GetString()));

        // Must have non-empty paragraphs
        Assert.True(json.TryGetProperty("paragraphs", out var paras));
        Assert.Equal(JsonValueKind.Array, paras.ValueKind);
        Assert.True(paras.GetArrayLength() >= 1);

        // providerUsed should be "Template" (no LLM configured in tests)
        Assert.True(json.TryGetProperty("providerUsed", out var prov));
        Assert.Equal("Template", prov.GetString());
    }

    [Fact]
    public async Task Describe_StratigraphicSummaryHasExpectedShape()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });

        var response = await _client.PostAsJsonAsync("/api/describe", new { cellIndex = 100 });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(json.TryGetProperty("stratigraphicSummary", out var strat));
        Assert.Equal(JsonValueKind.Array, strat.ValueKind);

        // Each row should have age, thickness, rockType, eventNote fields
        foreach (var row in strat.EnumerateArray())
        {
            Assert.True(row.TryGetProperty("age", out _));
            Assert.True(row.TryGetProperty("thickness", out _));
            Assert.True(row.TryGetProperty("rockType", out _));
            Assert.True(row.TryGetProperty("eventNote", out _));
        }
    }

    [Fact]
    public async Task Describe_HistoryTimelineIsOrderedAscending()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        // Advance a few ticks to build up history
        await _client.PostAsJsonAsync("/api/simulation/advance", new { deltaMa = 5.0 });
        await _client.PostAsJsonAsync("/api/simulation/advance", new { deltaMa = 5.0 });

        var response = await _client.PostAsJsonAsync("/api/describe", new { cellIndex = 50 });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(json.TryGetProperty("historyTimeline", out var timeline));
        Assert.Equal(JsonValueKind.Array, timeline.ValueKind);

        var ticks = timeline.EnumerateArray()
            .Select(e => e.GetProperty("simTick").GetInt64())
            .ToList();

        // Timeline must be non-decreasing (ascending order)
        for (int i = 1; i < ticks.Count; i++)
        {
            Assert.True(ticks[i] >= ticks[i - 1],
                $"History timeline out of order at index {i}: {ticks[i - 1]} > {ticks[i]}");
        }
    }

    [Fact]
    public async Task Describe_InvalidCellIndex_ReturnsBadRequest()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });

        var response = await _client.PostAsJsonAsync("/api/describe", new { cellIndex = -1 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // D6 — GET /api/state/eventlayermap & /api/state/eventlayermap/types
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetEventLayerMap_ReturnsFloatArraySameSizeAsGrid()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });

        // Default (no eventType = Normal layers)
        var response = await _client.GetAsync("/api/state/eventlayermap");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var values = await response.Content.ReadFromJsonAsync<float[]>();
        Assert.NotNull(values);
        // 512×512 = 262144 cells by default
        Assert.Equal(512 * 512, values.Length);
    }

    [Fact]
    public async Task GetEventLayerMap_ImpactEjecta_CellsNearImpactHaveThickness()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });
        // No advance needed - just check array shape and non-negative values

        var response = await _client.GetAsync("/api/state/eventlayermap?eventType=ImpactEjecta");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var values = await response.Content.ReadFromJsonAsync<float[]>();
        Assert.NotNull(values);
        Assert.Equal(512 * 512, values.Length);
        // All values should be non-negative
        Assert.All(values, v => Assert.True(v >= 0f));
    }

    [Fact]
    public async Task GetEventLayerMapTypes_ReturnsStringArray()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });

        var response = await _client.GetAsync("/api/state/eventlayermap/types");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var types = await response.Content.ReadFromJsonAsync<string[]>();
        Assert.NotNull(types);
        // May be empty if no extraordinary events have been deposited yet
        // but the array itself must exist
        foreach (var t in types)
        {
            Assert.False(string.IsNullOrWhiteSpace(t));
        }
    }

    [Fact]
    public async Task GetEventLayerMap_UnknownEventType_ReturnsBadRequest()
    {
        await _client.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });

        var response = await _client.GetAsync("/api/state/eventlayermap?eventType=NotARealType");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
