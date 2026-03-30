using GeoTime.Core.Models;

namespace GeoTime.Core.Kernel;

/// <summary>
/// Records significant geological events with timestamps.
/// </summary>
public sealed class EventLog
{
    private readonly List<GeoLogEntry> _entries = new();
    private readonly int _maxEntries;

    public EventLog(int maxEntries = 10_000) => _maxEntries = maxEntries;

    public void Record(GeoLogEntry entry)
    {
        _entries.Add(entry);
        if (_entries.Count > _maxEntries)
            _entries.RemoveRange(0, _entries.Count - _maxEntries);
    }

    public IReadOnlyList<GeoLogEntry> GetAll() => _entries;

    public List<GeoLogEntry> GetRange(double startMa, double endMa)
        => _entries.Where(e => e.TimeMa >= startMa && e.TimeMa <= endMa).ToList();

    public List<GeoLogEntry> GetByType(string type)
        => _entries.Where(e => e.Type == type).ToList();

    public List<GeoLogEntry> GetRecent(int count)
        => _entries.Skip(Math.Max(0, _entries.Count - count)).ToList();

    public int Length => _entries.Count;

    public void Clear() => _entries.Clear();
}
