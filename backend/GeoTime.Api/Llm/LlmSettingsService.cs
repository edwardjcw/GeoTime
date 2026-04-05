using System.Text.Json;

namespace GeoTime.Api.Llm;

/// <summary>
/// Holds runtime-mutable LLM configuration shared across all providers.
/// Seeds from <c>appsettings.json</c> on startup; persists user changes to
/// <c>llm-settings.json</c> in the app-data directory so restarts aren't needed.
/// </summary>
public class LlmSettingsService
{
    private static readonly string SettingsPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GeoTime",
            "llm-settings.json");

    private readonly object _lock = new();

    /// <summary>Name of the currently active provider, e.g. "Gemini".</summary>
    public string ActiveProvider { get; private set; } = "Template";

    /// <summary>Per-provider configuration keyed by provider name.</summary>
    public Dictionary<string, ProviderSettings> ProviderConfigs { get; } = new(StringComparer.OrdinalIgnoreCase);

    public LlmSettingsService(IConfiguration configuration)
    {
        // Seed defaults from appsettings.json Llm section
        var llmSection = configuration.GetSection("Llm");

        var preferenceOrder = llmSection.GetSection("PreferenceOrder").Get<string[]>()
            ?? ["Gemini", "OpenAi", "Anthropic", "Ollama", "LlamaSharp", "Template"];
        ActiveProvider = preferenceOrder.FirstOrDefault() ?? "Template";

        foreach (var name in new[] { "Gemini", "OpenAi", "Anthropic", "Ollama", "LlamaSharp", "Template" })
        {
            var section = llmSection.GetSection(name);
            ProviderConfigs[name] = new ProviderSettings(
                ApiKey:      section["ApiKey"],
                Model:       section["Model"],
                BaseUrl:     section["BaseUrl"],
                ModelPath:   section["ModelPath"],
                ModelUrl:    section["ModelUrl"],
                ContextSize: section.GetValue<int?>("ContextSize"),
                GpuLayers:   section.GetValue<int?>("GpuLayers")
            );
        }

        // Overlay with persisted user settings if they exist
        LoadFromFile();
    }

    /// <summary>Change the active provider at runtime. Thread-safe.</summary>
    public void SetActiveProvider(string name)
    {
        lock (_lock) { ActiveProvider = name; }
    }

    /// <summary>Update configuration for a named provider. Thread-safe.</summary>
    public void UpdateProviderConfig(string name, ProviderSettings settings)
    {
        lock (_lock) { ProviderConfigs[name] = settings; }
    }

    /// <summary>Persist current settings to <c>llm-settings.json</c>.</summary>
    public void Save()
    {
        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath)!;
                Directory.CreateDirectory(dir);
                var payload = new PersistedSettings(ActiveProvider, ProviderConfigs);
                File.WriteAllText(SettingsPath,
                    JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // Non-critical — settings just won't survive a restart
            }
        }
    }

    private void LoadFromFile()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var json = File.ReadAllText(SettingsPath);
            var data = JsonSerializer.Deserialize<PersistedSettings>(json);
            if (data == null) return;
            if (!string.IsNullOrWhiteSpace(data.ActiveProvider))
                ActiveProvider = data.ActiveProvider;
            foreach (var (name, cfg) in data.ProviderConfigs)
                ProviderConfigs[name] = cfg;
        }
        catch
        {
            // Ignore corrupt or missing settings file
        }
    }

    private sealed record PersistedSettings(
        string ActiveProvider,
        Dictionary<string, ProviderSettings> ProviderConfigs);
}

/// <summary>Configuration for a single LLM provider.</summary>
public record ProviderSettings(
    string? ApiKey,
    string? Model,
    string? BaseUrl,
    string? ModelPath,
    string? ModelUrl,
    int?    ContextSize,
    int?    GpuLayers
)
{
    public ProviderSettings() : this(null, null, null, null, null, null, null) { }
}
