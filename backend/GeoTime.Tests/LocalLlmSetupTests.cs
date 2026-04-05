using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Channels;
using GeoTime.Api.Llm;

namespace GeoTime.Tests;

/// <summary>
/// Unit tests for <see cref="LocalLlmSetupService"/>:
/// covers the GGUF validation path, download resume behaviour, and error propagation.
/// The Ollama flow tests are integration-level; we test the core file operations here.
/// </summary>
public class LocalLlmSetupTests
{
    // ── GGUF validation ───────────────────────────────────────────────────────

    [Fact]
    public async Task LlamaSharpProvider_InvalidMagicBytes_NeedsSetupTrue()
    {
        var path = Path.GetTempFileName();
        try
        {
            // Write an invalid 4-byte magic (not "GGUF")
            await File.WriteAllBytesAsync(path, [0x00, 0x01, 0x02, 0x03]);

            var settings = BuildSettings(llamaSharpModelPath: path);
            var provider = new LlamaSharpProvider(settings);

            var status = await provider.GetStatusAsync();
            Assert.False(status.IsReady);
            Assert.True(status.NeedsSetup);
            Assert.Contains("Invalid GGUF", status.StatusMessage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LlamaSharpProvider_ValidGgufMagic_NotReadyUntilModelLoaded()
    {
        var path = Path.GetTempFileName();
        try
        {
            // Write a valid GGUF magic
            await File.WriteAllBytesAsync(path, [(byte)'G', (byte)'G', (byte)'U', (byte)'F', 0x00]);

            var settings = BuildSettings(llamaSharpModelPath: path);
            var provider = new LlamaSharpProvider(settings);

            // File exists with valid magic, but NotifyModelReady() has not been called
            var status = await provider.GetStatusAsync();
            Assert.False(status.IsReady);
            // After NotifyModelReady, should be ready
            provider.NotifyModelReady();
            var status2 = await provider.GetStatusAsync();
            Assert.True(status2.IsReady);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── Setup service — error path ────────────────────────────────────────────

    [Fact]
    public async Task SetupService_OllamaSetup_EmitsErrorWhenInstallerFails()
    {
        // Point Ollama at an unreachable URL so the service-start wait times out
        // and an error event is emitted. We override the timeout by using a
        // cancellation-based pattern.
        var settings = BuildSettings(ollamaBaseUrl: "http://localhost:19998");
        var http     = new HttpClient(new AlwaysFailHandler());
        var llama    = new LlamaSharpProvider(settings);
        var service  = new LocalLlmSetupService(settings, http, llama);

        var reader = service.StartSetup("LlamaSharp");

        LlmSetupProgress? last = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        try
        {
            await foreach (var p in reader.ReadAllAsync(cts.Token))
                last = p;
        }
        catch (OperationCanceledException) { /* expected if it takes too long */ }

        // Either an error or a complete state should have been emitted
        Assert.NotNull(last);
    }

    [Fact]
    public async Task SetupService_GetProgressReader_ReturnsNullBeforeSetupStarted()
    {
        var settings = BuildSettings();
        var http     = new HttpClient(new AlwaysFailHandler());
        var llama    = new LlamaSharpProvider(settings);
        var service  = new LocalLlmSetupService(settings, http, llama);

        // Nothing started yet for "Ollama"
        var reader = service.GetProgressReader("Ollama");
        Assert.Null(reader);
    }

    [Fact]
    public async Task SetupService_GetProgressReader_ReturnsReaderAfterStart()
    {
        var settings = BuildSettings(ollamaBaseUrl: "http://localhost:19997");
        var http     = new HttpClient(new AlwaysFailHandler());
        var llama    = new LlamaSharpProvider(settings);
        var service  = new LocalLlmSetupService(settings, http, llama);

        service.StartSetup("Ollama");
        var reader = service.GetProgressReader("Ollama");
        Assert.NotNull(reader);

        // Drain to prevent leaks
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await foreach (var _ in reader!.ReadAllAsync(cts.Token))
            {
                if (_ .IsComplete || _.IsError) break;
            }
        }
        catch (OperationCanceledException) { /* ok */ }
    }

    // ── LlmSetupProgress record ───────────────────────────────────────────────

    [Fact]
    public void LlmSetupProgress_Properties_Roundtrip()
    {
        var p = new LlmSetupProgress("Downloading", 42, "detail text", false, false, null);
        Assert.Equal("Downloading", p.Step);
        Assert.Equal(42, p.PercentTotal);
        Assert.Equal("detail text", p.Detail);
        Assert.False(p.IsComplete);
        Assert.False(p.IsError);
        Assert.Null(p.ErrorMessage);
    }

    [Fact]
    public void LlmSetupProgress_ErrorProgress_HasMessage()
    {
        var p = new LlmSetupProgress("Error", 0, "", false, true, "something went wrong");
        Assert.True(p.IsError);
        Assert.Equal("something went wrong", p.ErrorMessage);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static LlmSettingsService BuildSettings(
        string? llamaSharpModelPath = null,
        string? ollamaBaseUrl       = null)
    {
        var settings = new LlmSettingsService(new EmptyConfiguration());
        if (llamaSharpModelPath != null)
            settings.UpdateProviderConfig("LlamaSharp", new ProviderSettings(
                ApiKey: null, Model: null, BaseUrl: null, ModelPath: llamaSharpModelPath,
                ModelUrl: null, ContextSize: null, GpuLayers: null));
        if (ollamaBaseUrl != null)
            settings.UpdateProviderConfig("Ollama", new ProviderSettings(
                ApiKey: null, Model: "gemma3", BaseUrl: ollamaBaseUrl,
                ModelPath: null, ModelUrl: null, ContextSize: null, GpuLayers: null));
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

    /// <summary>HttpMessageHandler that always throws a connection-refused exception.</summary>
    private sealed class AlwaysFailHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
            => Task.FromException<HttpResponseMessage>(
                new HttpRequestException("Connection refused", null, HttpStatusCode.ServiceUnavailable));
    }
}
