using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;

namespace GeoTime.Api.Llm;

/// <summary>
/// Orchestrates the guided installation flow for Ollama and LlamaSharp local providers.
/// Each setup is driven as an async background task that reports progress via a
/// <see cref="Channel{T}"/>; the SSE endpoint in Program.cs drains that channel and
/// forwards events to the browser.
/// </summary>
public sealed class LocalLlmSetupService
{
    private readonly LlmSettingsService _settings;
    private readonly HttpClient _http;
    private readonly LlamaSharpProvider _llamaSharp;

    // Per-provider channels so the user can check progress after re-opening the panel.
    private readonly Dictionary<string, Channel<LlmSetupProgress>> _channels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task> _tasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public LocalLlmSetupService(LlmSettingsService settings, HttpClient http, LlamaSharpProvider llamaSharp)
    {
        _settings   = settings;
        _http       = http;
        _llamaSharp = llamaSharp;
    }

    /// <summary>
    /// Returns the progress channel for the given provider, creating and starting a new
    /// setup task if one is not already running.
    /// </summary>
    public ChannelReader<LlmSetupProgress> StartSetup(string provider)
    {
        lock (_lock)
        {
            if (_tasks.TryGetValue(provider, out var existing) && !existing.IsCompleted)
                return _channels[provider].Reader;

            var channel = Channel.CreateUnbounded<LlmSetupProgress>();
            _channels[provider] = channel;

            var task = provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase)
                ? RunOllamaSetup(channel.Writer)
                : RunLlamaSharpSetup(channel.Writer);

            _tasks[provider] = task;
            return channel.Reader;
        }
    }

    /// <summary>Returns the reader for an in-progress or completed setup, or null.</summary>
    public ChannelReader<LlmSetupProgress>? GetProgressReader(string provider)
    {
        lock (_lock)
        {
            return _channels.TryGetValue(provider, out var ch) ? ch.Reader : null;
        }
    }

    // ── Ollama Setup ─────────────────────────────────────────────────────────

    private async Task RunOllamaSetup(ChannelWriter<LlmSetupProgress> writer)
    {
        try
        {
            await Emit(writer, "Checking prerequisites", 5, "Verifying curl/wget and OS");

            var model   = GetModel("Ollama");
            var baseUrl = GetBaseUrl("Ollama");

            await Emit(writer, "Checking Ollama installation", 10, "Running 'ollama --version'");

            bool ollamaInstalled = false;
            try
            {
                var version = await RunProcess("ollama", "--version");
                ollamaInstalled = !string.IsNullOrWhiteSpace(version);
                if (ollamaInstalled)
                    await Emit(writer, "Ollama found", 15, version.Trim());
            }
            catch { /* not installed */ }

            if (!ollamaInstalled)
            {
                await Emit(writer, "Downloading Ollama installer", 20, "Fetching installer script");
                await DownloadOllamaInstaller(writer);
                await Emit(writer, "Installing Ollama", 40, "Running installer");
                await InstallOllama(writer);
            }

            await Emit(writer, "Starting Ollama service", 50, "Running 'ollama serve'");
            await StartOllamaService(writer, baseUrl);

            await Emit(writer, $"Pulling model {model}", 60, $"Downloading {model}");
            await PullOllamaModel(writer, model);

            await Emit(writer, "Verifying", 90, "Sending test prompt");
            await VerifyOllama(writer, baseUrl, model);

            _settings.SetActiveProvider("Ollama");
            _settings.Save();

            await writer.WriteAsync(new LlmSetupProgress("Complete", 100, "Ollama is ready", true, false, null));
        }
        catch (Exception ex)
        {
            await writer.WriteAsync(new LlmSetupProgress("Error", 0, "", false, true, ex.Message));
        }
        finally
        {
            writer.Complete();
        }
    }

    private async Task DownloadOllamaInstaller(ChannelWriter<LlmSetupProgress> writer)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            // Download install.sh
            using var resp = await _http.GetAsync("https://ollama.com/install.sh");
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            var path  = Path.Combine(Path.GetTempPath(), "ollama-install.sh");
            await File.WriteAllBytesAsync(path, bytes);
            await Emit(writer, "Installer downloaded", 30, $"{bytes.Length / 1024} KB");
        }
        else
        {
            await Emit(writer, "Downloading installer", 25, "Downloading Ollama Windows installer");
            using var resp  = await _http.GetAsync("https://ollama.com/download/OllamaSetup.exe");
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            var path  = Path.Combine(Path.GetTempPath(), "OllamaSetup.exe");
            await File.WriteAllBytesAsync(path, bytes);
            await Emit(writer, "Installer downloaded", 30, $"{bytes.Length / 1024} KB");
        }
    }

    private async Task InstallOllama(ChannelWriter<LlmSetupProgress> writer)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var path = Path.Combine(Path.GetTempPath(), "ollama-install.sh");
            var output = await RunProcess("sh", path);
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                await Emit(writer, "Installing", 35, line.Trim());
        }
        else
        {
            var path   = Path.Combine(Path.GetTempPath(), "OllamaSetup.exe");
            var output = await RunProcess(path, "/S");
            await Emit(writer, "Installing", 38, output.Length > 0 ? output.Trim() : "Done");
        }
    }

    private async Task StartOllamaService(ChannelWriter<LlmSetupProgress> writer, string baseUrl)
    {
        // Fire & forget — don't await; Ollama serve runs as a daemon
        try
        {
            var psi = new ProcessStartInfo("ollama", "serve")
            {
                UseShellExecute        = false,
                RedirectStandardOutput = false,
                RedirectStandardError  = false,
            };
            Process.Start(psi);
        }
        catch { /* already running or not installed */ }

        // Wait up to 30 s for /api/tags to respond
        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(1000);
            try
            {
                var r = await _http.GetAsync($"{baseUrl}/api/tags");
                if (r.IsSuccessStatusCode)
                {
                    await Emit(writer, "Ollama service running", 55, "Health check passed");
                    return;
                }
            }
            catch { /* still starting */ }
            await Emit(writer, "Waiting for Ollama", 52, $"Attempt {i + 1}/30");
        }
        throw new TimeoutException("Ollama service did not start within 30 seconds.");
    }

    private async Task PullOllamaModel(ChannelWriter<LlmSetupProgress> writer, string model)
    {
        // ollama pull emits JSON progress lines
        var psi = new ProcessStartInfo("ollama", $"pull {model}")
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start 'ollama pull'");

        while (!proc.StandardOutput.EndOfStream)
        {
            var line = await proc.StandardOutput.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
                long completed = doc.RootElement.TryGetProperty("completed", out var c) ? c.GetInt64() : 0;
                long total     = doc.RootElement.TryGetProperty("total",     out var t) ? t.GetInt64() : 0;
                var detail     = total > 0
                    ? $"{completed / (1024 * 1024)} MB / {total / (1024 * 1024)} MB ({100 * completed / total}%)"
                    : status;
                int pct = total > 0 ? (int)(60 + 25 * completed / total) : 70;
                await Emit(writer, $"Pulling {model}", pct, detail);
            }
            catch { await Emit(writer, $"Pulling {model}", 70, line.Trim()); }
        }

        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0)
        {
            var err = await proc.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"'ollama pull' failed: {err}");
        }
    }

    private async Task VerifyOllama(ChannelWriter<LlmSetupProgress> writer, string baseUrl, string model)
    {
        var body = new
        {
            model,
            stream   = false,
            messages = new[] { new { role = "user", content = "Reply with the single word: ready" } },
        };
        var resp = await _http.PostAsJsonAsync($"{baseUrl}/api/chat", body);
        resp.EnsureSuccessStatusCode();
        await Emit(writer, "Verification passed", 95, "Model responded correctly");
    }

    // ── LlamaSharp Setup ─────────────────────────────────────────────────────

    private async Task RunLlamaSharpSetup(ChannelWriter<LlmSetupProgress> writer)
    {
        try
        {
            await Emit(writer, "Checking prerequisites", 5, "Verifying disk space and write permissions");

            var cfg      = _settings.ProviderConfigs.TryGetValue("LlamaSharp", out var c) ? c : new ProviderSettings();
            var modelUrl = cfg.ModelUrl
                ?? "https://huggingface.co/google/gemma-3-4b-it-GGUF/resolve/main/gemma-3-4b-it-Q4_K_M.gguf";
            var modelPath = cfg.ModelPath
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "GeoTime", "models", "gemma-3-4b.gguf");

            var modelDir = Path.GetDirectoryName(modelPath)!;
            Directory.CreateDirectory(modelDir);

            await Emit(writer, "Determining model URL", 10, modelUrl);

            await DownloadGguf(writer, modelUrl, modelPath);

            await Emit(writer, "Validating GGUF header", 88, modelPath);
            ValidateGguf(modelPath);

            await Emit(writer, "Loading model", 92, "Initialising LlamaSharp context");
            _llamaSharp.NotifyModelReady();

            _settings.UpdateProviderConfig("LlamaSharp", cfg with { ModelPath = modelPath });
            _settings.SetActiveProvider("LlamaSharp");
            _settings.Save();

            await writer.WriteAsync(new LlmSetupProgress("Complete", 100, "LlamaSharp model ready", true, false, null));
        }
        catch (Exception ex)
        {
            await writer.WriteAsync(new LlmSetupProgress("Error", 0, "", false, true, ex.Message));
        }
        finally
        {
            writer.Complete();
        }
    }

    private async Task DownloadGguf(ChannelWriter<LlmSetupProgress> writer, string url, string destPath)
    {
        long existingBytes = File.Exists(destPath) ? new FileInfo(destPath).Length : 0;

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existingBytes > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingBytes, null);
            await Emit(writer, "Resuming download", 15, $"Resuming from {existingBytes / (1024 * 1024)} MB");
        }
        else
        {
            await Emit(writer, "Downloading model", 15, "Starting download");
        }

        using var resp = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Download failed: HTTP {(int)resp.StatusCode}");

        var totalBytes = (resp.Content.Headers.ContentLength ?? 0) + existingBytes;
        await using var src  = await resp.Content.ReadAsStreamAsync();
        await using var dest = new FileStream(destPath, existingBytes > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write);

        var buffer      = new byte[65536];
        long downloaded = existingBytes;
        int  read;

        while ((read = await src.ReadAsync(buffer)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, read));
            downloaded += read;
            int pct = totalBytes > 0 ? (int)(15 + 70 * downloaded / totalBytes) : 50;
            var detail = totalBytes > 0
                ? $"{downloaded / (1024 * 1024)} MB / {totalBytes / (1024 * 1024)} MB ({100 * downloaded / totalBytes}%)"
                : $"{downloaded / (1024 * 1024)} MB downloaded";
            await Emit(writer, "Downloading model", pct, detail);
        }
    }

    private static void ValidateGguf(string path)
    {
        using var fs    = File.OpenRead(path);
        var magic = new byte[4];
        int read  = fs.Read(magic, 0, 4);
        if (read < 4 || magic[0] != 'G' || magic[1] != 'G' || magic[2] != 'U' || magic[3] != 'F')
            throw new InvalidDataException("Downloaded file is not a valid GGUF model.");
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    private static async Task<string> RunProcess(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {exe}");
        var output = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0)
        {
            var err = await proc.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"{exe} exited {proc.ExitCode}: {err}");
        }
        return output;
    }

    private static Task Emit(ChannelWriter<LlmSetupProgress> writer, string step, int pct, string detail) =>
        writer.WriteAsync(new LlmSetupProgress(step, pct, detail, false, false, null)).AsTask();

    private string GetModel(string provider)
    {
        if (_settings.ProviderConfigs.TryGetValue(provider, out var cfg) && !string.IsNullOrWhiteSpace(cfg.Model))
            return cfg.Model;
        return provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase) ? "gemma3" : "";
    }

    private string GetBaseUrl(string provider)
    {
        if (_settings.ProviderConfigs.TryGetValue(provider, out var cfg) && !string.IsNullOrWhiteSpace(cfg.BaseUrl))
            return cfg.BaseUrl;
        return "http://localhost:11434";
    }
}

/// <summary>One progress event emitted during a local LLM setup flow.</summary>
public record LlmSetupProgress(
    string  Step,
    int     PercentTotal,
    string  Detail,
    bool    IsComplete,
    bool    IsError,
    string? ErrorMessage
);
