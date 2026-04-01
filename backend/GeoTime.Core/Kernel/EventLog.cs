using GeoTime.Core.Models;

namespace GeoTime.Core.Kernel;

/// <summary>
/// Records significant geological events with timestamps.
/// </summary>
public sealed class EventLog(int maxEntries = 10_000)
{
    private readonly List<GeoLogEntry> _entries = [];

    public void Record(GeoLogEntry entry)
    {
        _entries.Add(entry);
        if (_entries.Count > maxEntries)
            _entries.RemoveRange(0, _entries.Count - maxEntries);
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
