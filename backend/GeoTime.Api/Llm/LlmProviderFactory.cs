namespace GeoTime.Api.Llm;

/// <summary>
/// Resolves the active <see cref="ILlmProvider"/> at runtime by consulting
/// <see cref="LlmSettingsService"/>. Falls back down the preference list to
/// the first available provider and ultimately to <see cref="TemplateFallbackProvider"/>.
/// </summary>
public class LlmProviderFactory
{
    private readonly LlmSettingsService _settings;
    private readonly Dictionary<string, ILlmProvider> _providers;

    public LlmProviderFactory(
        LlmSettingsService settings,
        IEnumerable<ILlmProvider> providers)
    {
        _settings  = settings;
        _providers = providers.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the currently configured active provider.
    /// If that provider is not available, steps through the remaining providers
    /// in preference order and returns the first available one.
    /// Always falls back to <see cref="TemplateFallbackProvider"/> if nothing else works.
    /// </summary>
    public ILlmProvider GetActiveProvider()
    {
        if (_providers.TryGetValue(_settings.ActiveProvider, out var active) && active.IsAvailable)
            return active;

        // Walk preference order
        var preferenceOrder = new[] { "Gemini", "OpenAi", "Anthropic", "Ollama", "LlamaSharp", "Template" };
        foreach (var name in preferenceOrder)
        {
            if (_providers.TryGetValue(name, out var p) && p.IsAvailable)
                return p;
        }

        // Ultimate fallback
        return _providers.TryGetValue("Template", out var fallback) ? fallback : new TemplateFallbackProvider();
    }

    /// <summary>Returns all registered providers (for the settings UI).</summary>
    public IReadOnlyCollection<ILlmProvider> GetAllProviders() => _providers.Values;
}
