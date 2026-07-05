using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GeoTime.Api.Diagnostics;

/// <summary>
/// Writes one JSONL file per backend process lifetime for optimization analysis.
/// Each line is a self-contained event with UTC timestamp, source, and payload.
/// </summary>
public sealed class SessionPerformanceLogger : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object _lock = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    public string LogPath { get; }

    public SessionPerformanceLogger(IWebHostEnvironment env, IConfiguration config)
    {
        var dir = config["GeoTime:PerfLogDirectory"]
            ?? Environment.GetEnvironmentVariable("GEOTIME_PERF_LOG_DIR")
            ?? Path.Combine(env.ContentRootPath, "logs");
        Directory.CreateDirectory(dir);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        LogPath = Path.Combine(dir, $"geotime-session-{stamp}-pid{Environment.ProcessId}.jsonl");

        _writer = new StreamWriter(
            new FileStream(LogPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read),
            leaveOpen: false)
        {
            AutoFlush = true,
        };

        Write("session_start", "backend", new
        {
            processId = Environment.ProcessId,
            machineName = Environment.MachineName,
            osDescription = RuntimeInformation.OSDescription,
            osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            dotnetVersion = Environment.Version.ToString(),
            processorCount = Environment.ProcessorCount,
            contentRoot = env.ContentRootPath,
            environment = env.EnvironmentName,
            logPath = LogPath,
        });
    }

    public void Write(string eventName, string source, object data)
    {
        if (_disposed) return;

        var entry = new Dictionary<string, object?>
        {
            ["ts"] = DateTime.UtcNow.ToString("o"),
            ["event"] = eventName,
            ["source"] = source,
            ["data"] = data,
        };

        var line = JsonSerializer.Serialize(entry, JsonOpts);
        lock (_lock)
        {
            _writer.WriteLine(line);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Write("session_end", "backend", new { processId = Environment.ProcessId });
        _writer.Dispose();
    }
}
