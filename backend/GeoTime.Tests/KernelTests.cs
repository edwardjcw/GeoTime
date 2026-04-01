using GeoTime.Core.Kernel;

namespace GeoTime.Tests;

public class KernelTests
{
    [Fact]
    public void EventBus_EmitAndReceive()
    {
        var bus = new EventBus();
        object? received = null;
        bus.On("TEST", payload => received = payload);
        bus.Emit("TEST", "hello");
        Assert.Equal("hello", received);
    }

    [Fact]
    public void EventBus_Off_RemovesListener()
    {
        var bus = new EventBus();
        var count = 0;
        Action<object> cb = _ => count++;
        bus.On("TEST", cb);
        bus.Emit("TEST", "a");
        bus.Off("TEST", cb);
        bus.Emit("TEST", "b");
        Assert.Equal(1, count);
    }

    [Fact]
    public void EventBus_Clear_RemovesAllListeners()
    {
        var bus = new EventBus();
        var count = 0;
        bus.On("TEST", _ => count++);
        bus.Clear();
        bus.Emit("TEST", "a");
        Assert.Equal(0, count);
    }

    [Fact]
    public void EventLog_RecordAndRetrieve()
    {
        var log = new EventLog(100);
        log.Record(new GeoTime.Core.Models.GeoLogEntry
        {
            TimeMa = -4000, Type = "VOLCANIC_ERUPTION", Description = "test"
        });
        Assert.Equal(1, log.Length);
        Assert.Equal("VOLCANIC_ERUPTION", log.GetAll()[0].Type);
    }

    [Fact]
    public void EventLog_GetRange_FiltersCorrectly()
    {
        var log = new EventLog();
        log.Record(new GeoTime.Core.Models.GeoLogEntry { TimeMa = -4000, Type = "A", Description = "" });
        log.Record(new GeoTime.Core.Models.GeoLogEntry { TimeMa = -3000, Type = "B", Description = "" });
        log.Record(new GeoTime.Core.Models.GeoLogEntry { TimeMa = -2000, Type = "C", Description = "" });

        var range = log.GetRange(-3500, -2500);
        Assert.Single(range);
        Assert.Equal("B", range[0].Type);
    }

    [Fact]
    public void EventLog_TrimOldest()
    {
        var log = new EventLog(5);
        for (var i = 0; i < 10; i++)
            log.Record(new GeoTime.Core.Models.GeoLogEntry { TimeMa = i, Type = "T", Description = $"{i}" });
        Assert.Equal(5, log.Length);
    }

    [Fact]
    public void SimClock_AdvancesTime()
    {
        var bus = new EventBus();
        var clock = new SimClock(bus);
        clock.Advance(1.0);
        Assert.Equal(SimClock.INITIAL_TIME + 1.0, clock.T, 5);
    }

    [Fact]
    public void SimClock_PausedDoesNotAdvance()
    {
        var bus = new EventBus();
        var clock = new SimClock(bus);
        clock.Pause();
        clock.Advance(1.0);
        Assert.Equal(SimClock.INITIAL_TIME, clock.T, 5);
    }

    [Fact]
    public void SimClock_SetRateClamped()
    {
        var bus = new EventBus();
        var clock = new SimClock(bus);
        clock.SetRate(200);
        Assert.Equal(100, clock.Rate);
        clock.SetRate(0.0001);
        Assert.Equal(0.001, clock.Rate, 4);
    }

    [Fact]
    public void SimClock_SeekTo()
    {
        var bus = new EventBus();
        var clock = new SimClock(bus);
        clock.SeekTo(-2000);
        Assert.Equal(-2000, clock.T);
    }

    [Fact]
    public void SnapshotManager_TakesAndFindsSnapshots()
    {
        var mgr = new SnapshotManager(10, 100);
        var data = new byte[100];
        data[0] = 42;
        mgr.TakeSnapshot(-4500, data);
        Assert.Equal(1, mgr.Count);

        var snap = mgr.FindNearestBefore(-4000);
        Assert.NotNull(snap);
        Assert.Equal(-4500, snap.TimeMa);
        Assert.Equal(42, snap.BufferData[0]);
    }

    [Fact]
    public void SnapshotManager_MaybeTakeSnapshot_RespectsInterval()
    {
        var mgr = new SnapshotManager(10, 100);
        var data = new byte[10];
        Assert.True(mgr.MaybeTakeSnapshot(-4500, data));
        Assert.False(mgr.MaybeTakeSnapshot(-4495, data));
        Assert.True(mgr.MaybeTakeSnapshot(-4490, data));
    }
}
