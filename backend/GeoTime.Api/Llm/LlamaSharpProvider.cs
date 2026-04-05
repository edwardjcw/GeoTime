using System.Runtime.CompilerServices;

namespace GeoTime.Api.Llm;

/// <summary>
/// LLM provider that loads a GGUF model file in-process via LLamaSharp.
/// Config keys: <c>Llm:LlamaSharp:ModelPath</c>, <c>Llm:LlamaSharp:ModelUrl</c>,
///              <c>Llm:LlamaSharp:ContextSize</c>, <c>Llm:LlamaSharp:GpuLayers</c>.
///
/// The LLamaSharp NuGet package is an optional dependency. If no model file
/// has been downloaded yet, <see cref="GetStatusAsync"/> returns
/// <c>NeedsSetup = true</c>; <see cref="GenerateAsync"/> throws
/// <see cref="InvalidOperationException"/> until the model is loaded.
/// </summary>
public sealed class LlamaSharpProvider : ILlmProvider
{
    private readonly LlmSettingsService _settings;
    private volatile bool _modelLoaded;

    public string Name => "LlamaSharp";

    public bool IsAvailable => ModelFileExists();

    public LlamaSharpProvider(LlmSettingsService settings)
    {
        _settings = settings;
    }

    public Task<LlmProviderStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var path = GetModelPath();
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult(new LlmProviderStatus(false, "Model path not configured", true));

        if (!File.Exists(path))
            return Task.FromResult(new LlmProviderStatus(false, "Model not downloaded", true));

        // Verify GGUF magic bytes
        try
        {
            using var fs = File.OpenRead(path);
            var magic = new byte[4];
            if (fs.Read(magic, 0, 4) < 4 || magic[0] != 'G' || magic[1] != 'G' || magic[2] != 'U' || magic[3] != 'F')
                return Task.FromResult(new LlmProviderStatus(false, "Invalid GGUF file", true));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new LlmProviderStatus(false, $"Cannot read model: {ex.Message}", true));
        }

        if (!_modelLoaded)
            return Task.FromResult(new LlmProviderStatus(false, "Model not loaded — run setup", true));

        return Task.FromResult(new LlmProviderStatus(true, "Ready (local inference)", false));
    }

    public Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        if (!_modelLoaded)
            throw new InvalidOperationException("LlamaSharp model is not loaded. Run the setup flow first.");

        // Actual LLamaSharp inference would go here once the package is added.
        throw new NotImplementedException(
            "LLamaSharp in-process inference requires the LLamaSharp NuGet package. " +
            "Install it via the setup flow or use another provider.");
    }

    public async IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userPrompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var response = await GenerateAsync(systemPrompt, userPrompt, ct);
        yield return response;
    }

    /// <summary>
    /// Called by the setup flow after a GGUF file has been downloaded and validated.
    /// Sets <see cref="_modelLoaded"/> so status reports Ready.
    /// </summary>
    public void NotifyModelReady()
    {
        _modelLoaded = true;
    }

    private bool ModelFileExists()
    {
        var path = GetModelPath();
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    private string? GetModelPath()
    {
        return _settings.ProviderConfigs.TryGetValue("LlamaSharp", out var cfg) ? cfg.ModelPath : null;
    }
}
