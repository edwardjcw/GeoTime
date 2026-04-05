using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace GeoTime.Api.Llm;

/// <summary>
/// LLM provider backed by Google's Generative AI REST API (Gemini).
/// Config keys: <c>Llm:Gemini:ApiKey</c>, <c>Llm:Gemini:Model</c>.
/// Handles 429 rate-limit responses with exponential back-off (3 retries).
/// </summary>
public sealed class GeminiProvider : ILlmProvider
{
    private const string DefaultModel = "gemini-2.0-flash";
    private const string BaseUrl = "https://generativelanguage.googleapis.com";

    private readonly LlmSettingsService _settings;
    private readonly HttpClient _http;

    public string Name => "Gemini";

    public bool IsAvailable
    {
        get
        {
            var cfg = GetConfig();
            return !string.IsNullOrWhiteSpace(cfg.ApiKey);
        }
    }

    public GeminiProvider(LlmSettingsService settings, HttpClient http)
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
            var model = cfg.Model ?? DefaultModel;
            var url   = $"{BaseUrl}/v1beta/models/{model}?key={cfg.ApiKey}";
            var resp  = await _http.GetAsync(url, ct);
            if (resp.IsSuccessStatusCode)
                return new LlmProviderStatus(true, "Connected", false);
            return new LlmProviderStatus(false, $"HTTP {(int)resp.StatusCode}", false);
        }
        catch (Exception ex)
        {
            return new LlmProviderStatus(false, ex.Message, false);
        }
    }

    public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var cfg   = GetConfig();
        var model = cfg.Model ?? DefaultModel;
        var url   = $"{BaseUrl}/v1beta/models/{model}:generateContent?key={cfg.ApiKey}";

        var body = new
        {
            system_instruction = new { parts = new[] { new { text = systemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = userPrompt } } } },
        };

        for (int attempt = 0; attempt <= 3; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);

            var resp = await _http.PostAsJsonAsync(url, body, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < 3)
                continue;
            resp.EnsureSuccessStatusCode();

            using var doc   = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var text = doc.RootElement
                          .GetProperty("candidates")[0]
                          .GetProperty("content")
                          .GetProperty("parts")[0]
                          .GetProperty("text")
                          .GetString() ?? "";
            return text;
        }

        throw new InvalidOperationException("Gemini: exceeded retry limit");
    }

    public async IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userPrompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var cfg   = GetConfig();
        var model = cfg.Model ?? DefaultModel;
        var url   = $"{BaseUrl}/v1beta/models/{model}:streamGenerateContent?alt=sse&key={cfg.ApiKey}";

        var body = new
        {
            system_instruction = new { parts = new[] { new { text = systemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = userPrompt } } } },
        };

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };

        using var resp   = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null && !ct.IsCancellationRequested)
        {
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;
            var json = line["data: ".Length..];
            if (json == "[DONE]") break;
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement
                          .GetProperty("candidates")[0]
                          .GetProperty("content")
                          .GetProperty("parts")[0]
                          .GetProperty("text")
                          .GetString() ?? "";
            if (!string.IsNullOrEmpty(text))
                yield return text;
        }
    }

    private ProviderSettings GetConfig() =>
        _settings.ProviderConfigs.TryGetValue("Gemini", out var cfg) ? cfg : new ProviderSettings();
}
