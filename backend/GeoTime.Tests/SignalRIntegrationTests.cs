using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;

namespace GeoTime.Tests;

public class SignalRIntegrationTests : IClassFixture<WebApplicationFactory<GeoTime.Api.Program>>
{
    private readonly WebApplicationFactory<GeoTime.Api.Program> _factory;

    public SignalRIntegrationTests(WebApplicationFactory<GeoTime.Api.Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Hub_Connect_ReceivesConnectedEvent()
    {
        var server = _factory.Server;
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
        var httpClient = _factory.CreateClient();
        await httpClient.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });

        var server = _factory.Server;
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
        var httpClient = _factory.CreateClient();
        await httpClient.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });

        var server = _factory.Server;
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
    public async Task Hub_RequestHeightMap_ReceivesData()
    {
        var httpClient = _factory.CreateClient();
        await httpClient.PostAsJsonAsync("/api/planet/generate", new { seed = 42u });

        var server = _factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(server.BaseAddress, "/hubs/simulation"),
                o => o.HttpMessageHandlerFactory = _ => server.CreateHandler())
            .Build();

        var tcs = new TaskCompletionSource<bool>();
        connection.On<float[]>("HeightMapData", data =>
        {
            if (data != null && data.Length > 0)
                tcs.TrySetResult(true);
        });

        await connection.StartAsync();
        await connection.InvokeAsync("RequestHeightMap");

        var received = await Task.WhenAny(tcs.Task, Task.Delay(5000)) == tcs.Task;
        await connection.DisposeAsync();

        Assert.True(received, "Should receive HeightMapData with non-empty array");
    }
}
