using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using GeoTime.Api.Llm;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GeoTime.Tests;

/// <summary>
/// Unit and integration tests for Phase D3: LLM Provider Abstraction.
/// Covers provider availability logic, factory fallback behaviour, and
/// the /api/llm/* REST endpoints.
/// </summary>
public class LlmProviderTests
{
    // ── TemplateFallbackProvider ──────────────────────────────────────────────

    [Fact]
    public async Task TemplateFallback_IsAlwaysAvailable()
    {
        var p = new TemplateFallbackProvider();
        Assert.True(p.IsAvailable);
        var status = await p.GetStatusAsync();
        Assert.True(status.IsReady);
        Assert.False(status.NeedsSetup);
    }

    [Fact]
    public async Task TemplateFallback_ReturnsNonEmptyText()
    {
        var p = new TemplateFallbackProvider();
        var text = await p.GenerateAsync("system", "user");
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Fact]
    public async Task TemplateFallback_StreamAsyncYieldsAtLeastOneToken()
    {
        var p      = new TemplateFallbackProvider();
        var tokens = new List<string>();
        await foreach (var t in p.StreamAsync("sys", "user"))
            tokens.Add(t);
        Assert.NotEmpty(tokens);
    }

    // ── OllamaProvider with unreachable URL ───────────────────────────────────

    [Fact]
    public async Task OllamaProvider_UnreachableUrl_NeedsSetupTrue()
    {
        var settings = BuildSettings(active: "Ollama", ollamaBaseUrl: "http://localhost:19999");
        var http     = new HttpClient(); // no handler override — will get connection-refused
        var provider = new OllamaProvider(settings, http);

        var status = await provider.GetStatusAsync();
        Assert.False(status.IsReady);
        Assert.True(status.NeedsSetup);
    }

    // ── LlamaSharpProvider with missing model file ────────────────────────────

    [Fact]
    public async Task LlamaSharpProvider_MissingModelFile_NeedsSetupTrue()
    {
        var settings = BuildSettings(active: "LlamaSharp",
            llamaSharpModelPath: "/nonexistent/path/model.gguf");
        var provider = new LlamaSharpProvider(settings);

        Assert.False(provider.IsAvailable);
        var status = await provider.GetStatusAsync();
        Assert.False(status.IsReady);
        Assert.True(status.NeedsSetup);
    }

    [Fact]
    public async Task LlamaSharpProvider_NoModelPathConfigured_NeedsSetupTrue()
    {
        var settings = BuildSettings(active: "LlamaSharp", llamaSharpModelPath: null);
        var provider = new LlamaSharpProvider(settings);

        var status = await provider.GetStatusAsync();
        Assert.False(status.IsReady);
        Assert.True(status.NeedsSetup);
    }

    // ── LlmProviderFactory — fallback behaviour ───────────────────────────────

    [Fact]
    public void Factory_NoKeysConfigured_ReturnsFallback()
    {
        var settings  = BuildSettings(active: "Gemini");
        var providers = new ILlmProvider[]
        {
            new GeminiProvider(settings, new HttpClient()),
            new TemplateFallbackProvider(),
        };
        var factory  = new LlmProviderFactory(settings, providers);
        var active   = factory.GetActiveProvider();

        Assert.Equal("Template", active.Name);
    }

    [Fact]
    public void Factory_TemplateActiveByDefault_ReturnsTemplate()
    {
        var settings  = BuildSettings(active: "Template");
        var providers = new ILlmProvider[] { new TemplateFallbackProvider() };
        var factory   = new LlmProviderFactory(settings, providers);

        var active = factory.GetActiveProvider();
        Assert.Equal("Template", active.Name);
    }

    [Fact]
    public void Factory_GetAllProviders_Returns6Entries()
    {
        var settings  = BuildSettings(active: "Template");
        var providers = new ILlmProvider[]
        {
            new GeminiProvider(settings, new HttpClient()),
            new OpenAiProvider(settings, new HttpClient()),
            new AnthropicProvider(settings, new HttpClient()),
            new OllamaProvider(settings, new HttpClient()),
            new LlamaSharpProvider(settings),
            new TemplateFallbackProvider(),
        };
        var factory = new LlmProviderFactory(settings, providers);
        Assert.Equal(6, factory.GetAllProviders().Count);
    }

    // ── LlmSettingsService ────────────────────────────────────────────────────

    [Fact]
    public void LlmSettingsService_SetActiveProvider_UpdatesActiveProvider()
    {
        var settings = BuildSettings(active: "Template");
        settings.SetActiveProvider("Ollama");
        Assert.Equal("Ollama", settings.ActiveProvider);
    }

    [Fact]
    public void LlmSettingsService_UpdateProviderConfig_StoresSettings()
    {
        var settings = BuildSettings(active: "Template");
        settings.UpdateProviderConfig("Gemini", new ProviderSettings(
            ApiKey: "my-key", Model: "gemini-2.0-flash", null, null, null, null, null));
        Assert.Equal("my-key", settings.ProviderConfigs["Gemini"].ApiKey);
    }

    // ── Integration: /api/llm/* endpoints ────────────────────────────────────

    [Fact]
    public async Task GetLlmProviders_ReturnsListIncludingTemplate()
    {
        using var factory = new WebApplicationFactory<GeoTime.Api.Program>();
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/llm/providers");
        response.EnsureSuccessStatusCode();

        var json  = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(json);
        Assert.True(json.Length >= 1);

        var template = json.FirstOrDefault(p =>
            p.GetProperty("name").GetString() == "Template");
        Assert.NotEqual(default, template);
        Assert.True(template.GetProperty("isAvailable").GetBoolean());
    }

    [Fact]
    public async Task GetLlmActive_ReturnsProviderName()
    {
        using var factory = new WebApplicationFactory<GeoTime.Api.Program>();
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/llm/active");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var provider = json.GetProperty("provider").GetString();
        Assert.False(string.IsNullOrWhiteSpace(provider));
    }

    [Fact]
    public async Task PutLlmActive_UpdatesActiveProvider()
    {
        using var factory = new WebApplicationFactory<GeoTime.Api.Program>();
        using var client  = factory.CreateClient();

        var body = new { provider = "Template", settings = (object?)null };
        var response = await client.PutAsJsonAsync("/api/llm/active", body);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Template", json.GetProperty("provider").GetString());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static LlmSettingsService BuildSettings(
        string active             = "Template",
        string? geminiApiKey      = null,
        string? ollamaBaseUrl     = null,
        string? llamaSharpModelPath = null)
    {
        // Use an empty configuration — configure provider settings directly via the service API.
        var settings = new LlmSettingsService(new EmptyConfiguration());
        settings.SetActiveProvider(active);
        if (geminiApiKey != null)
            settings.UpdateProviderConfig("Gemini", new ProviderSettings(
                ApiKey: geminiApiKey, Model: null, BaseUrl: null, ModelPath: null,
                ModelUrl: null, ContextSize: null, GpuLayers: null));
        if (ollamaBaseUrl != null)
            settings.UpdateProviderConfig("Ollama", new ProviderSettings(
                ApiKey: null, Model: "gemma3", BaseUrl: ollamaBaseUrl,
                ModelPath: null, ModelUrl: null, ContextSize: null, GpuLayers: null));
        if (llamaSharpModelPath != null)
            settings.UpdateProviderConfig("LlamaSharp", new ProviderSettings(
                ApiKey: null, Model: null, BaseUrl: null, ModelPath: llamaSharpModelPath,
                ModelUrl: null, ContextSize: null, GpuLayers: null));
        return settings;
    }

    /// <summary>Empty IConfiguration implementation for test use.</summary>
    private sealed class EmptyConfiguration : Microsoft.Extensions.Configuration.IConfiguration
    {
        public string? this[string key] { get => null; set { } }
        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => new NullChangeToken();
        public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren() => [];
        public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string key) => new EmptySection(key);

        private sealed class EmptySection(string key)
            : Microsoft.Extensions.Configuration.IConfigurationSection
        {
            public string? this[string k] { get => null; set { } }
            public string Key => key;
            public string Path => key;
            public string? Value { get => null; set { } }
            public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => new NullChangeToken();
            public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren() => [];
            public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string k) => new EmptySection(k);
        }

        private sealed class NullChangeToken : Microsoft.Extensions.Primitives.IChangeToken
        {
            public bool HasChanged => false;
            public bool ActiveChangeCallbacks => false;
            public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => NullDisposable.Instance;
        }

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
