namespace VoiceBridge;

// Voice lifetime and reply lifetime are separate. Missing controls suspend
// observation; role-count changes do not reset a reply's text cursor.
public sealed class DesktopTranscriptSession(string source, Action<BridgeEvent> emit)
{
    public static readonly TimeSpan MissingVoiceGrace = TimeSpan.FromSeconds(2);
    public bool Active { get; private set; }
    private string _session = "", _text = "", _userKey = "", _completedText = "";
    private DesktopSpeaker _speaker;
    private int _roles;
    private DateTime? _missingVoiceAt, _ambiguousUserAt;
    public long TextSnapshots { get; private set; }
    public long Revisions { get; private set; }
    public long TransientFrames { get; private set; }

    public void Observe(DesktopTranscriptFrame frame, DateTime now)
    {
        if (!frame.VoiceVisible)
        {
            if (!Active) return;
            _missingVoiceAt ??= now; TransientFrames++;
            if (now - _missingVoiceAt >= MissingVoiceGrace) Stop();
            // The normal chat tree can appear during a voice-pane remount.
            // Never interpret it as a new voice reply or overwrite our cursor.
            return;
        }
        _missingVoiceAt = null;
        if (!Active)
        {
            Active = true; _speaker = frame.Speaker; _userKey = frame.UserKey;
            _text = frame.Text; _roles = frame.RoleCount; _completedText = "";
            _session = Guid.NewGuid().ToString("N");
            emit(new("start", source, _session));
            return; // Opening voice must not replay pre-existing chat history.
        }
        if (frame.Speaker == DesktopSpeaker.None) return;
        if (frame.Speaker == DesktopSpeaker.User)
        {
            if (_speaker != DesktopSpeaker.User)
            {
                // A temporarily missing assistant can reveal the previous user.
                // A genuinely new role/user acts immediately; ambiguous repeated
                // user text must remain visible briefly before interrupting.
                var ambiguous = frame.UserKey == _userKey && frame.RoleCount <= _roles;
                if (ambiguous)
                {
                    _ambiguousUserAt ??= now;
                    if (now - _ambiguousUserAt < TimeSpan.FromMilliseconds(250)) return;
                }
                BeginReply(); _speaker = DesktopSpeaker.User;
            }
            _ambiguousUserAt = null; _userKey = frame.UserKey; _roles = frame.RoleCount;
            return;
        }
        _ambiguousUserAt = null;
        var newUser = frame.UserKey.Length > 0 && frame.UserKey != _userKey && frame.RoleCount > _roles;
        if (_speaker != DesktopSpeaker.Assistant || newUser)
        {
            if (_speaker == DesktopSpeaker.Assistant && newUser) BeginReply();
            _speaker = DesktopSpeaker.Assistant; _text = ""; _completedText = "";
        }
        _userKey = frame.UserKey; _roles = frame.RoleCount;
        if (frame.Text.Length == 0) return; // Incomplete provider frame, not deletion.
        if (frame.Text != _text)
        {
            if (!frame.Text.StartsWith(_text, StringComparison.Ordinal)) Revisions++;
            _text = frame.Text; TextSnapshots++;
            // Revisions and paragraph additions go to the revision-aware text
            // pipeline as a complete snapshot. Never silently discard a suffix.
            emit(new("text", source, _session, _text, "snapshot"));
        }
        if (frame.Complete && _completedText != _text)
        { emit(new("complete", source, _session)); _completedText = _text; }
    }
    private void BeginReply()
    {
        emit(new("interrupt", source, _session));
        _session = Guid.NewGuid().ToString("N"); _text = ""; _completedText = "";
        emit(new("start", source, _session));
    }
    public void Stop()
    {
        if (Active) emit(new("stop", source, _session));
        Active = false; _missingVoiceAt = null; _ambiguousUserAt = null;
    }
}
