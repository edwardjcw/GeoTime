namespace GeoTime.Core.Kernel;

/// <summary>
/// Manages keyframe snapshots of the simulation state for time-scrubbing.
/// </summary>
public sealed class SnapshotManager
{
    public sealed class Snapshot
    {
        public double TimeMa { get; set; }
        public byte[] BufferData { get; set; } = Array.Empty<byte>();
    }

    private readonly List<Snapshot> _snapshots = new();
    public double Interval { get; }
    private readonly int _maxSnapshots;
    private double _lastSnapshotTime = double.NegativeInfinity;

    public SnapshotManager(double interval = 10, int maxSnapshots = 500)
    {
        Interval = interval;
        _maxSnapshots = maxSnapshots;
    }

    public bool MaybeTakeSnapshot(double timeMa, byte[] stateData)
    {
        if (timeMa - _lastSnapshotTime >= Interval)
        {
            TakeSnapshot(timeMa, stateData);
            return true;
        }
        return false;
    }

    public void TakeSnapshot(double timeMa, byte[] stateData)
    {
        var copy = new byte[stateData.Length];
        Buffer.BlockCopy(stateData, 0, copy, 0, stateData.Length);
        _snapshots.Add(new Snapshot { TimeMa = timeMa, BufferData = copy });
        _lastSnapshotTime = timeMa;
        _snapshots.Sort((a, b) => a.TimeMa.CompareTo(b.TimeMa));
        while (_snapshots.Count > _maxSnapshots)
            _snapshots.RemoveAt(0);
    }

    public Snapshot? FindNearestBefore(double timeMa)
    {
        Snapshot? best = null;
        foreach (var snap in _snapshots)
        {
            if (snap.TimeMa <= timeMa) best = snap;
            else break;
        }
        return best;
    }

    public int Count => _snapshots.Count;
    public List<double> GetSnapshotTimes() => _snapshots.Select(s => s.TimeMa).ToList();
    public void Clear() { _snapshots.Clear(); _lastSnapshotTime = double.NegativeInfinity; }
}
