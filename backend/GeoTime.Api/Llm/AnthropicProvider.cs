using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace GeoTime.Api.Llm;

/// <summary>
/// LLM provider backed by the Anthropic Messages API.
/// Config keys: <c>Llm:Anthropic:ApiKey</c>, <c>Llm:Anthropic:Model</c>.
/// </summary>
public sealed class AnthropicProvider : ILlmProvider
{
    private const string DefaultModel = "claude-haiku-4-5";
    private const string BaseUrl      = "https://api.anthropic.com/v1";
    private const string ApiVersion   = "2023-06-01";

    private readonly LlmSettingsService _settings;
    private readonly HttpClient _http;

    public string Name => "Anthropic";

    public bool IsAvailable
    {
        get
        {
            var cfg = GetConfig();
            return !string.IsNullOrWhiteSpace(cfg.ApiKey);
        }
    }

    public AnthropicProvider(LlmSettingsService settings, HttpClient http)
    {
        _settings = settings;
        _http     = http;
    }

    public async Task<LlmProviderStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var cfg = GetConfig();
        if (string.IsNullOrWhiteSpace(cfg.ApiKey))
            return new LlmProviderStatus(false, "API key missing", false);

        // Anthropic doesn't expose a free health endpoint, so we do a minimal
        // messages call with max_tokens=1 to verify the key is valid.
        try
        {
            var body = new
            {
                model      = cfg.Model ?? DefaultModel,
                max_tokens = 1,
                messages   = new[] { new { role = "user", content = "ping" } },
            };
            using var resp = await _http.SendAsync(BuildRequest(cfg.ApiKey!, body), ct);
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
        var cfg = GetConfig();
        var body = new
        {
            model      = cfg.Model ?? DefaultModel,
            max_tokens = 4096,
            system     = systemPrompt,
            messages   = new[] { new { role = "user", content = userPrompt } },
        };

        using var resp = await _http.SendAsync(BuildRequest(cfg.ApiKey!, body), ct);
        resp.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement
                  .GetProperty("content")[0]
                  .GetProperty("text")
                  .GetString() ?? "";
    }

    public async IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userPrompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var cfg = GetConfig();
        var body = new
        {
            model      = cfg.Model ?? DefaultModel,
            max_tokens = 4096,
            stream     = true,
            system     = systemPrompt,
            messages   = new[] { new { role = "user", content = userPrompt } },
        };

        var request  = BuildRequest(cfg.ApiKey!, body);
        using var resp   = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null && !ct.IsCancellationRequested)
        {
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;
            var json = line["data: ".Length..];
            if (json.Contains("\"type\":\"message_stop\"")) break;
            if (!json.Contains("\"type\":\"content_block_delta\"")) continue;
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement
                          .GetProperty("delta")
                          .GetProperty("text")
                          .GetString() ?? "";
            if (!string.IsNullOrEmpty(text))
                yield return text;
        }
    }

    private static HttpRequestMessage BuildRequest(string apiKey, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", ApiVersion);
        return req;
    }

    private ProviderSettings GetConfig() =>
        _settings.ProviderConfigs.TryGetValue("Anthropic", out var cfg) ? cfg : new ProviderSettings();
}
