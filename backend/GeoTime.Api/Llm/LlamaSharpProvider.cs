using System.Runtime.CompilerServices;

namespace GeoTime.Api.Llm;

/// <summary>
/// LLM provider placeholder for future in-process GGUF inference via LLamaSharp.
/// Config keys: <c>Llm:LlamaSharp:ModelPath</c>, <c>Llm:LlamaSharp:ModelUrl</c>,
///              <c>Llm:LlamaSharp:ContextSize</c>, <c>Llm:LlamaSharp:GpuLayers</c>.
///
/// Inference is not implemented in this build, so this provider is never
/// selectable even when a GGUF file has been downloaded and validated.
/// </summary>
public sealed class LlamaSharpProvider : ILlmProvider
{
    private const string InferenceUnavailableMessage = "LlamaSharp inference is unavailable in this build.";

    private readonly LlmSettingsService _settings;

    public string Name => "LlamaSharp";

    public bool IsAvailable => false;

    public LlamaSharpProvider(LlmSettingsService settings)
    {
        _settings = settings;
    }

    public Task<LlmProviderStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var path = GetModelPath();
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult(new LlmProviderStatus(
                false,
                $"{InferenceUnavailableMessage} Model path not configured.",
                true));

        if (!File.Exists(path))
            return Task.FromResult(new LlmProviderStatus(
                false,
                $"{InferenceUnavailableMessage} Model not downloaded.",
                true));

        // Verify GGUF magic bytes
        try
        {
            using var fs = File.OpenRead(path);
            var magic = new byte[4];
            if (fs.Read(magic, 0, 4) < 4 || magic[0] != 'G' || magic[1] != 'G' || magic[2] != 'U' || magic[3] != 'F')
                return Task.FromResult(new LlmProviderStatus(
                    false,
                    $"{InferenceUnavailableMessage} Invalid GGUF file.",
                    true));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new LlmProviderStatus(
                false,
                $"{InferenceUnavailableMessage} Cannot read model: {ex.Message}",
                true));
        }

        return Task.FromResult(new LlmProviderStatus(
            false,
            $"{InferenceUnavailableMessage} GGUF model is present but generation cannot run.",
            false));
    }

    public Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        throw new InvalidOperationException(
            $"{InferenceUnavailableMessage} Use another provider until in-process inference is implemented.");
    }

    public async IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userPrompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var response = await GenerateAsync(systemPrompt, userPrompt, ct);
        yield return response;
    }

    private string? GetModelPath()
    {
        return _settings.ProviderConfigs.TryGetValue("LlamaSharp", out var cfg) ? cfg.ModelPath : null;
    }
}
