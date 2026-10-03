namespace VoiceBridge;

// Bounded metadata only: no keys, transcript content or audio is logged.
public sealed class EventJournal
{
    private readonly Queue<object> _events = new();
    private readonly object _sync = new();
    public void Record(BridgeEvent ev)
    {
        lock (_sync)
        {
            if (_events.Count == 100) _events.Dequeue();
            _events.Enqueue(new { at = DateTimeOffset.UtcNow, ev.Type, ev.Source, ev.SessionId,
                ev.Mode, textLength = ev.Text?.Length ?? 0 });
        }
    }
    public object[] Snapshot() { lock (_sync) return _events.ToArray(); }
}
