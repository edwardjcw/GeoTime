namespace GeoTime.Api.Llm;

/// <summary>
/// Common interface for all LLM provider backends.
/// Implementations must be thread-safe; the factory may call them concurrently.
/// </summary>
public interface ILlmProvider
{
    /// <summary>Short machine-readable name, e.g. "Gemini", "Ollama".</summary>
    string Name { get; }

    /// <summary>
    /// Fast synchronous availability check (no network I/O).
    /// Cloud providers: true if an API key is configured.
    /// Local providers: true if the model file / service is present.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Active probe — may perform a network request or file check.
    /// Returns a <see cref="LlmProviderStatus"/> with human-readable details.
    /// </summary>
    Task<LlmProviderStatus> GetStatusAsync(CancellationToken ct = default);

    /// <summary>Non-streaming text generation. Returns the full response string.</summary>
    Task<string> GenerateAsync(string systemPrompt, string userPrompt,
                               CancellationToken ct = default);

    /// <summary>Streaming text generation. Yields tokens as they arrive.</summary>
    IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userPrompt,
                                         CancellationToken ct = default);
}

/// <summary>Result of an active provider health probe.</summary>
/// <param name="IsReady">True when the provider can accept generation requests.</param>
/// <param name="StatusMessage">Human-readable status, e.g. "Connected", "API key missing".</param>
/// <param name="NeedsSetup">True for local providers that still require installation or model download.</param>
public record LlmProviderStatus(
    bool   IsReady,
    string StatusMessage,
    bool   NeedsSetup
);
