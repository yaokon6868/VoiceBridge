namespace VoiceBridge;

// Source activation is not speech. Only actual text may claim the playback turn.
public sealed class SessionRouter
{
    private readonly Action<BridgeEvent> _forward;
    private readonly Dictionary<string,string> _latest = new();
    private readonly HashSet<(string Source, string Session)> _retired = new();
    private readonly Queue<(string Source, string Session)> _retiredOrder = new();
    private readonly object _sync = new();
    private string? _activeSource, _activeSession;
    public SessionRouter(Action<BridgeEvent> forward) => _forward = forward;
    public void Handle(BridgeEvent ev)
    {
        lock (_sync)
        {
            if(ev.Type=="start")
            {
                if (_retired.Contains((ev.Source, ev.SessionId))) return;
                if(_latest.Count >= 32 && !_latest.ContainsKey(ev.Source))return;
                _latest[ev.Source]=ev.SessionId; return;
            }
            if(!_latest.TryGetValue(ev.Source,out var session) || session!=ev.SessionId)return;
            if(ev.Type=="text" && !string.IsNullOrWhiteSpace(ev.Text))
            {
                if(_activeSource!=ev.Source || _activeSession!=ev.SessionId)
                {
                    if (_activeSource is not null && _activeSession is not null)
                    {
                        var previous = (_activeSource, _activeSession);
                        if (_retired.Add(previous)) _retiredOrder.Enqueue(previous);
                        if (_retiredOrder.Count > 64) _retired.Remove(_retiredOrder.Dequeue());
                        if (_latest.TryGetValue(_activeSource, out var latest) && latest == _activeSession)
                            _latest.Remove(_activeSource);
                    }
                    _activeSource=ev.Source;_activeSession=ev.SessionId;
                    _forward(ev with {Type="start",Text=null});
                }
                _forward(ev);return;
            }
            if(ev.Type is "stop" or "interrupt")
            {
                _latest.Remove(ev.Source);
                if(_activeSource==ev.Source && _activeSession==ev.SessionId)
                {
                    _forward(ev);_activeSource=null;_activeSession=null;
                }
                return;
            }
            if(ev.Type=="complete" && _activeSource==ev.Source && _activeSession==ev.SessionId)_forward(ev);
        }
    }
}
