using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace GeoTime.Api.Llm;

/// <summary>
/// LLM provider backed by a locally-running Ollama instance.
/// Config keys: <c>Llm:Ollama:BaseUrl</c>, <c>Llm:Ollama:Model</c>.
/// Uses <c>/api/chat</c> for both batch and streaming requests.
/// Fails gracefully if the Ollama service is not running.
/// </summary>
public sealed class OllamaProvider : ILlmProvider
{
    private const string DefaultModel  = "gemma3";
    private const string DefaultBase   = "http://localhost:11434";

    private readonly LlmSettingsService _settings;
    private readonly HttpClient _http;

    public string Name => "Ollama";

    public bool IsAvailable
    {
        get
        {
            // Optimistic fast check — assume available if a base URL is configured.
            var cfg = GetConfig();
            return !string.IsNullOrWhiteSpace(cfg.BaseUrl ?? DefaultBase);
        }
    }

    public OllamaProvider(LlmSettingsService settings, HttpClient http)
    {
        _settings = settings;
        _http     = http;
    }

    public async Task<LlmProviderStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var baseUrl = GetBaseUrl();
        try
        {
            var resp = await _http.GetAsync($"{baseUrl}/api/tags", ct);
            if (!resp.IsSuccessStatusCode)
                return new LlmProviderStatus(false, "Ollama not running", true);

            // Check whether the configured model has been pulled
            var model = GetModel();
            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var models = doc.RootElement.GetProperty("models");
            foreach (var m in models.EnumerateArray())
            {
                var name = m.GetProperty("name").GetString() ?? "";
                if (name.StartsWith(model, StringComparison.OrdinalIgnoreCase))
                    return new LlmProviderStatus(true, "Connected", false);
            }
            return new LlmProviderStatus(false, $"Model '{model}' not downloaded", true);
        }
        catch
        {
            return new LlmProviderStatus(false, "Ollama not running", true);
        }
    }

    public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var url   = $"{GetBaseUrl()}/api/chat";
        var model = GetModel();

        var body = new
        {
            model,
            stream   = false,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt   },
            },
        };

        var request  = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        var resp = await _http.SendAsync(request, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement
                  .GetProperty("message")
                  .GetProperty("content")
                  .GetString() ?? "";
    }

    public async IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userPrompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var url   = $"{GetBaseUrl()}/api/chat";
        var model = GetModel();

        var body = new
        {
            model,
            stream   = true,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt   },
            },
        };

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };

        using var resp   = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;
            using var doc = JsonDocument.Parse(line);
            var text = doc.RootElement
                          .GetProperty("message")
                          .GetProperty("content")
                          .GetString() ?? "";
            if (!string.IsNullOrEmpty(text))
                yield return text;
            if (doc.RootElement.TryGetProperty("done", out var done) && done.GetBoolean())
                break;
        }
    }

    private string GetBaseUrl()
    {
        var cfg = GetConfig();
        return string.IsNullOrWhiteSpace(cfg.BaseUrl) ? DefaultBase : cfg.BaseUrl;
    }

    private string GetModel()
    {
        var cfg = GetConfig();
        return string.IsNullOrWhiteSpace(cfg.Model) ? DefaultModel : cfg.Model;
    }

    private ProviderSettings GetConfig() =>
        _settings.ProviderConfigs.TryGetValue("Ollama", out var cfg) ? cfg : new ProviderSettings();
}
