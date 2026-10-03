namespace VoiceBridge;

// Source activation is not speech. Only actual text may claim the playback turn.
public sealed class SessionRouter
{
    private readonly Action<BridgeEvent> _forward;
    private readonly Dictionary<string,string> _latest = new();
    private readonly object _sync = new();
    private string? _activeSource, _activeSession;
    public SessionRouter(Action<BridgeEvent> forward) => _forward = forward;
    public void Handle(BridgeEvent ev)
    {
        lock (_sync)
        {
            if(ev.Type=="start")
            {
                if(_latest.Count >= 32 && !_latest.ContainsKey(ev.Source))return;
                _latest[ev.Source]=ev.SessionId; return;
            }
            if(!_latest.TryGetValue(ev.Source,out var session) || session!=ev.SessionId)return;
            if(ev.Type=="text" && !string.IsNullOrWhiteSpace(ev.Text))
            {
                if(_activeSource!=ev.Source || _activeSession!=ev.SessionId)
                {
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
