using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace VoiceBridge;

public interface IDesktopAudioSession : IDisposable
{
    string Key { get; }
    uint ProcessId { get; }
    bool Muted { get; set; }
}

// Only render sessions are touched, never a microphone or the Fish helper.
public sealed class ChatGptAudioMuter : IDisposable
{
    private readonly Dictionary<string, bool> _previous = new();
    private readonly object _sync = new();
    private readonly Func<IReadOnlyList<IDesktopAudioSession>> _enumerate;
    private readonly Func<HashSet<uint>> _clientPids;
    private string _error = "";
    private bool _enumerationComplete;
    public ChatGptAudioMuter(Func<IReadOnlyList<IDesktopAudioSession>>? enumerate = null,
        Func<HashSet<uint>>? clientPids = null)
    { _enumerate = enumerate ?? Enumerate; _clientPids = clientPids ?? ClientPids; }
    public bool HasLease { get { lock (_sync) return _previous.Count > 0; } }
    public object Status() { lock (_sync) return new { trackedSessions = _previous.Count, error = _error }; }
    private static HashSet<uint> ClientPids()
    {
        var pids = new HashSet<uint>();
        foreach (var process in Process.GetProcessesByName("ChatGPT"))
            using (process) pids.Add((uint)process.Id);
        return pids;
    }
    private sealed class NativeSession(string device, AudioSessionControl session) : IDesktopAudioSession
    {
        public string Key { get; } = device + "\0" + session.GetSessionInstanceIdentifier;
        public uint ProcessId => session.GetProcessID;
        public bool Muted { get => session.SimpleAudioVolume.Mute; set => session.SimpleAudioVolume.Mute = value; }
        public void Dispose() => session.Dispose();
    }
    private IReadOnlyList<IDesktopAudioSession> Enumerate()
    {
        var result = new List<IDesktopAudioSession>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            using (device)
            {
                try
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    for (var i = 0; i < sessions.Count; i++)
                    {
                        var session = sessions[i];
                        try { result.Add(new NativeSession(device.ID, session)); }
                        catch (Exception ex) { session.Dispose(); _enumerationComplete = false; _error = ex.GetType().Name; }
                    }
                }
                catch (Exception ex) { _enumerationComplete = false; _error = ex.GetType().Name; }
            }
            return result;
        }
        catch { foreach (var session in result) session.Dispose(); throw; }
    }
    public void MuteDesktopApps() => Apply(true);
    public void Restore() => Apply(false);
    private void Apply(bool mute)
    {
        lock (_sync)
        {
            IReadOnlyList<IDesktopAudioSession>? sessions = null;
            try
            {
                HashSet<uint> pids = mute ? _clientPids() : [];
                _enumerationComplete = true; _error = "";
                sessions = _enumerate();
                var present = new HashSet<string>();
                foreach (var session in sessions)
                {
                    try
                    {
                        var key = session.Key; present.Add(key);
                        if (mute && pids.Contains(session.ProcessId))
                        {
                            var muted = session.Muted;
                            if (!_previous.ContainsKey(key)) _previous[key] = muted;
                            if (!muted) session.Muted = true;
                        }
                        else if (_previous.TryGetValue(key, out var original))
                        {
                            session.Muted = original; _previous.Remove(key);
                        }
                    }
                    catch (Exception ex) { _error = ex.GetType().Name; }
                }
                // Session instances are distinct even when a PID is reused.
                if (_enumerationComplete)
                    foreach (var key in _previous.Keys.Where(key => !present.Contains(key)).ToArray())
                        _previous.Remove(key);
            }
            catch (Exception ex) { _error = ex.GetType().Name; }
            finally { if (sessions is not null) foreach (var session in sessions) session.Dispose(); }
        }
    }
    public void Dispose() => Restore();
}

public static class DesktopMutePolicy
{
    public static bool ShouldMute(bool voiceActive, bool ttsReady, bool echoEnabled,
        bool routeReady, bool turnBlocked) => voiceActive && ttsReady && !turnBlocked && (!echoEnabled || routeReady);
}
