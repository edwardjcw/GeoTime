using System.Text.Json;
using GeoTime.Api.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace GeoTime.Tests;

public class SessionPerformanceLoggerTests
{
    [Fact]
    public void SessionPerformanceLogger_WritesJsonLinesToConfiguredDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "geotime-perf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GeoTime:PerfLogDirectory"] = tempDir,
                })
                .Build();

            var env = new TestWebHostEnvironment { ContentRootPath = tempDir };

            using (var logger = new SessionPerformanceLogger(env, config))
            {
                logger.Write("test_event", "backend", new { value = 42 });
                Assert.True(File.Exists(logger.LogPath));
            }

            var lines = File.ReadAllLines(Directory.GetFiles(tempDir, "geotime-session-*.jsonl").Single());
            Assert.True(lines.Length >= 3); // session_start, test_event, session_end

            using var startDoc = JsonDocument.Parse(lines[0]);
            Assert.Equal("session_start", startDoc.RootElement.GetProperty("event").GetString());

            using var eventDoc = JsonDocument.Parse(lines[^2]);
            Assert.Equal("test_event", eventDoc.RootElement.GetProperty("event").GetString());
            Assert.Equal(42, eventDoc.RootElement.GetProperty("data").GetProperty("value").GetInt32());
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "GeoTime.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string EnvironmentName { get; set; } = "Test";
    }
}
