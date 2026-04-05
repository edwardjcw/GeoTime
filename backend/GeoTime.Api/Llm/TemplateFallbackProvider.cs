using System.Runtime.CompilerServices;
using System.Text;

namespace GeoTime.Api.Llm;

/// <summary>
/// Always-available fallback LLM provider that generates deterministic prose
/// from the geological context using a template engine (Phase D4).
/// No external dependencies, no API key required.
/// </summary>
public sealed class TemplateFallbackProvider : ILlmProvider
{
    public string Name => "Template";
    public bool IsAvailable => true;

    public Task<LlmProviderStatus> GetStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(new LlmProviderStatus(true, "Always available", false));

    public Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        // Phase D4 will implement a rich template engine. For now we return a
        // structured placeholder that the D5 API endpoint can safely parse.
        var sb = new StringBuilder();
        sb.AppendLine("This location has been analysed by the GeoTime planetary simulation engine.");
        sb.AppendLine();
        sb.AppendLine("The geological context has been assembled from tectonic, hydrological, " +
                      "orographic, and climate data computed during the simulation. " +
                      "Configure an LLM provider (Gemini, OpenAI, Anthropic, or Ollama) in the " +
                      "⚙ LLM settings panel to receive a richer narrative description.");
        return Task.FromResult(sb.ToString());
    }

    public async IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userPrompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var text = await GenerateAsync(systemPrompt, userPrompt, ct);
        yield return text;
    }
}
