namespace GeoTime.Core.Kernel;

/// <summary>
/// Simple pub/sub event bus for geological events.
/// </summary>
public sealed class EventBus
{
    private readonly Dictionary<string, List<Action<object>>> _listeners = new();

    public void On(string type, Action<object> callback)
    {
        if (!_listeners.TryGetValue(type, out var list))
        {
            list = [];
            _listeners[type] = list;
        }
        list.Add(callback);
    }

    public void Off(string type, Action<object> callback)
    {
        if (_listeners.TryGetValue(type, out var list))
            list.Remove(callback);
    }

    public void Emit(string type, object payload)
    {
        if (!_listeners.TryGetValue(type, out var list)) return;
        foreach (var cb in list.ToList())
            cb(payload);
    }

    public void Clear() => _listeners.Clear();
}
