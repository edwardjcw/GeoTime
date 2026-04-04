using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;

namespace GeoTime.Tests;

public class SignalRIntegrationTests(WebApplicationFactory<GeoTime.Api.Program> factory)
    : IClassFixture<WebApplicationFactory<GeoTime.Api.Program>>
{
    [Fact]
    public async Task Hub_Connect_ReceivesConnectedEvent()
    {
        var server = factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(server.BaseAddress, "/hubs/simulation"),
                o => o.HttpMessageHandlerFactory = _ => server.CreateHandler())
            .Build();

        var tcs = new TaskCompletionSource<bool>();
        connection.On<object>("Connected", _ => tcs.TrySetResult(true));

        await connection.StartAsync();
        var received = await Task.WhenAny(tcs.Task, Task.Delay(5000)) == tcs.Task;
        await connection.DisposeAsync();

        Assert.True(received, "Should receive Connected event after connecting");
    }

    [Fact]
    public async Task Hub_GeneratePlanet_BroadcastsEvent()
    {
        // First generate via REST so the singleton has state
        var httpClient = factory.CreateClient();
        await httpClient.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });

        var server = factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(server.BaseAddress, "/hubs/simulation"),
                o => o.HttpMessageHandlerFactory = _ => server.CreateHandler())
            .Build();

        var tcs = new TaskCompletionSource<bool>();
        connection.On<object>("PlanetGenerated", _ => tcs.TrySetResult(true));

        await connection.StartAsync();
        await connection.InvokeAsync("GeneratePlanet", 99u);

        var received = await Task.WhenAny(tcs.Task, Task.Delay(5000)) == tcs.Task;
        await connection.DisposeAsync();

        Assert.True(received, "Should receive PlanetGenerated event");
    }

    [Fact]
    public async Task Hub_AdvanceSimulation_ReceivesTickAndComplete()
    {
        var httpClient = factory.CreateClient();
        await httpClient.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });

        var server = factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(server.BaseAddress, "/hubs/simulation"),
                o => o.HttpMessageHandlerFactory = _ => server.CreateHandler())
            .Build();

        var tickReceived = new TaskCompletionSource<bool>();
        var completeReceived = new TaskCompletionSource<bool>();

        connection.On<object>("SimulationTick", _ => tickReceived.TrySetResult(true));
        connection.On<object>("SimulationAdvanceComplete", _ => completeReceived.TrySetResult(true));

        await connection.StartAsync();
        await connection.InvokeAsync("AdvanceSimulation", 5.0, 2);

        var gotTick = await Task.WhenAny(tickReceived.Task, Task.Delay(10000)) == tickReceived.Task;
        var gotComplete = await Task.WhenAny(completeReceived.Task, Task.Delay(10000)) == completeReceived.Task;
        await connection.DisposeAsync();

        Assert.True(gotTick, "Should receive SimulationTick event");
        Assert.True(gotComplete, "Should receive SimulationAdvanceComplete event");
    }

    [Fact]
    public async Task Hub_AdvanceSimulation_ReceivesStateBundleData()
    {
        var httpClient = factory.CreateClient();
        await httpClient.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });

        var server = factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(server.BaseAddress, "/hubs/simulation"),
                o => o.HttpMessageHandlerFactory = _ => server.CreateHandler())
            .Build();

        var tcs = new TaskCompletionSource<byte[]>();
        // The hub sends StateBundleData as byte[] via SignalR
        connection.On<byte[]>("StateBundleData", data => tcs.TrySetResult(data));

        await connection.StartAsync();
        await connection.InvokeAsync("AdvanceSimulation", 1.0, 1);

        var gotBundle = await Task.WhenAny(tcs.Task, Task.Delay(15000)) == tcs.Task;
        await connection.DisposeAsync();

        Assert.True(gotBundle, "Should receive StateBundleData after advance");
        var data = await tcs.Task;
        // Bundle = 3 float arrays × cellCount floats × 4 bytes
        const int cc = GeoTime.Core.Models.GridConstants.CELL_COUNT;
        Assert.Equal(cc * sizeof(float) * 3, data.Length);
    }

    [Fact]
    public async Task Hub_RequestStateBundleBinary_ReceivesBundle()
    {
        var httpClient = factory.CreateClient();
        await httpClient.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });

        var server = factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(server.BaseAddress, "/hubs/simulation"),
                o => o.HttpMessageHandlerFactory = _ => server.CreateHandler())
            .Build();

        var tcs = new TaskCompletionSource<byte[]>();
        connection.On<byte[]>("StateBundleData", data => tcs.TrySetResult(data));

        await connection.StartAsync();
        await connection.InvokeAsync("RequestStateBundleBinary");

        var gotBundle = await Task.WhenAny(tcs.Task, Task.Delay(10000)) == tcs.Task;
        await connection.DisposeAsync();

        Assert.True(gotBundle, "Should receive StateBundleData from RequestStateBundleBinary");
        const int cc = GeoTime.Core.Models.GridConstants.CELL_COUNT;
        var bundleData = await tcs.Task;
        Assert.Equal(cc * sizeof(float) * 3, bundleData.Length);
    }

    [Fact]
    public async Task Hub_Connect_ReceivesComputeInfo()
    {
        var server = factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(server.BaseAddress, "/hubs/simulation"),
                o => o.HttpMessageHandlerFactory = _ => server.CreateHandler())
            .Build();

        var tcs = new TaskCompletionSource<System.Text.Json.JsonElement>();
        connection.On<System.Text.Json.JsonElement>("Connected", e => tcs.TrySetResult(e));

        await connection.StartAsync();
        var received = await Task.WhenAny(tcs.Task, Task.Delay(5000)) == tcs.Task;
        await connection.DisposeAsync();

        Assert.True(received, "Should receive Connected event");
        var el = await tcs.Task;
        Assert.True(el.TryGetProperty("computeMode", out var mode), "Connected event should include computeMode");
        Assert.True(el.TryGetProperty("computeDevice", out var device), "Connected event should include computeDevice");
        Assert.False(string.IsNullOrEmpty(mode.GetString()));
        Assert.False(string.IsNullOrEmpty(device.GetString()));
    }

    [Fact]
    public async Task Hub_RequestHeightMap_ReceivesData()
    {
        var httpClient = factory.CreateClient();
        await httpClient.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });

        var server = factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(server.BaseAddress, "/hubs/simulation"),
                o => o.HttpMessageHandlerFactory = _ => server.CreateHandler())
            .Build();

        var tcs = new TaskCompletionSource<bool>();
        connection.On<float[]>("HeightMapData", data =>
        {
            if (data is { Length: > 0 })
                tcs.TrySetResult(true);
        });

        await connection.StartAsync();
        await connection.InvokeAsync("RequestHeightMap");

        var received = await Task.WhenAny(tcs.Task, Task.Delay(5000)) == tcs.Task;
        await connection.DisposeAsync();

        Assert.True(received, "Should receive HeightMapData with non-empty array");
    }
}
