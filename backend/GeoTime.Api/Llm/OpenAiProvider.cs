using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace GeoTime.Api.Llm;

/// <summary>
/// LLM provider backed by the OpenAI Chat Completions API.
/// Config keys: <c>Llm:OpenAi:ApiKey</c>, <c>Llm:OpenAi:Model</c>, <c>Llm:OpenAi:BaseUrl</c>.
/// The <c>BaseUrl</c> can be overridden for Azure OpenAI or other compatible endpoints.
/// </summary>
public sealed class OpenAiProvider : ILlmProvider
{
    private const string DefaultModel  = "gpt-4o-mini";
    private const string DefaultBase   = "https://api.openai.com/v1";

    private readonly LlmSettingsService _settings;
    private readonly HttpClient _http;

    public string Name => "OpenAi";

    public bool IsAvailable
    {
        get
        {
            var cfg = GetConfig();
            return !string.IsNullOrWhiteSpace(cfg.ApiKey);
        }
    }

    public OpenAiProvider(LlmSettingsService settings, HttpClient http)
    {
        _settings = settings;
        _http     = http;
    }

    public async Task<LlmProviderStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var cfg = GetConfig();
        if (string.IsNullOrWhiteSpace(cfg.ApiKey))
            return new LlmProviderStatus(false, "API key missing", false);

        try
        {
            var baseUrl = cfg.BaseUrl ?? DefaultBase;
            var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/models");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cfg.ApiKey);
            var resp = await _http.SendAsync(request, ct);
            return resp.IsSuccessStatusCode
                ? new LlmProviderStatus(true, "Connected", false)
                : new LlmProviderStatus(false, $"HTTP {(int)resp.StatusCode}", false);
        }
        catch (Exception ex)
        {
            return new LlmProviderStatus(false, ex.Message, false);
        }
    }

    public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var cfg     = GetConfig();
        var baseUrl = cfg.BaseUrl ?? DefaultBase;
        var model   = cfg.Model  ?? DefaultModel;

        var body = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt   },
            },
        };

        var request = BuildRequest(HttpMethod.Post, $"{baseUrl}/chat/completions", cfg.ApiKey!);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var resp = await _http.SendAsync(request, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement
                  .GetProperty("choices")[0]
                  .GetProperty("message")
                  .GetProperty("content")
                  .GetString() ?? "";
    }

    public async IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userPrompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var cfg     = GetConfig();
        var baseUrl = cfg.BaseUrl ?? DefaultBase;
        var model   = cfg.Model  ?? DefaultModel;

        var body = new
        {
            model,
            stream = true,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt   },
            },
        };

        var request = BuildRequest(HttpMethod.Post, $"{baseUrl}/chat/completions", cfg.ApiKey!);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp   = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;
            var json = line["data: ".Length..];
            if (json == "[DONE]") break;
            using var doc = JsonDocument.Parse(json);
            var delta = doc.RootElement
                           .GetProperty("choices")[0]
                           .GetProperty("delta");
            if (delta.TryGetProperty("content", out var content))
            {
                var text = content.GetString() ?? "";
                if (!string.IsNullOrEmpty(text))
                    yield return text;
            }
        }
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string url, string apiKey)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        return req;
    }

    private ProviderSettings GetConfig() =>
        _settings.ProviderConfigs.TryGetValue("OpenAi", out var cfg) ? cfg : new ProviderSettings();
}
