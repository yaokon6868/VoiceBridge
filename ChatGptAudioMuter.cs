using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace VoiceBridge;

public sealed class ChatGptAudioMuter : IDisposable
{
    private readonly Dictionary<uint, bool> _previous = new();
    private readonly object _sync = new();

    public void MuteDesktopApps()
    {
        lock (_sync)
        {
            try
            {
                var pids = Process.GetProcessesByName("ChatGPT").Select(p => (uint)p.Id).ToHashSet();
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var sessions = device.AudioSessionManager.Sessions;
                for (var i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    var pid = session.GetProcessID;
                    if (!pids.Contains(pid)) continue;
                    if (!_previous.ContainsKey(pid)) _previous[pid] = session.SimpleAudioVolume.Mute;
                    session.SimpleAudioVolume.Mute = true;
                }
            }
            catch { }
        }
    }

    public void Restore()
    {
        lock (_sync)
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var sessions = device.AudioSessionManager.Sessions;
                for (var i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    if (_previous.TryGetValue(session.GetProcessID, out var muted))
                        session.SimpleAudioVolume.Mute = muted;
                }
            }
            catch { }
            _previous.Clear();
        }
    }

    public void Dispose() => Restore();
}
