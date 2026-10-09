using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace VoiceBridge;

public sealed record DesktopCaptureEndpoint(uint ProcessId, string DeviceName);
public sealed record DesktopRouteSnapshot(bool Complete, DesktopCaptureEndpoint[] Inputs, string Error = "")
{
    public const string CleanMicrophone = "CABLE Output (VB-Audio Virtual Cable)";
    public bool Connected => Complete && Inputs.Length > 0 && Inputs.All(input =>
        string.Equals(input.DeviceName, CleanMicrophone, StringComparison.OrdinalIgnoreCase));
    public string State => !Complete ? "inspection-failed" : Inputs.Length == 0 ? "waiting-for-microphone"
        : Connected ? "clean-microphone-connected" : "physical-microphone-connected";
}

// Reads endpoint/session metadata only. Never opens a capture or loopback stream,
// changes defaults, or assumes the default device is what a client actually uses.
public sealed class DesktopEchoRoute
{
    private readonly object _sync = new();
    private DateTime _checked;
    private DesktopRouteSnapshot _snapshot = new(false, [], "not-checked");
    public DesktopRouteSnapshot Read(bool refresh = false)
    {
        lock (_sync)
        {
            if (!refresh && DateTime.UtcNow - _checked < TimeSpan.FromMilliseconds(500)) return _snapshot;
            _snapshot = Inspect(); _checked = DateTime.UtcNow; return _snapshot;
        }
    }
    public static DesktopRouteSnapshot Inspect()
    {
        var inputs = new List<DesktopCaptureEndpoint>();
        try
        {
            var clients = new HashSet<uint>();
            foreach (var process in Process.GetProcessesByName("ChatGPT"))
                using (process) clients.Add((uint)process.Id);
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            using (device)
            {
                var sessions = device.AudioSessionManager.Sessions;
                for (var i = 0; i < sessions.Count; i++)
                {
                    using var session = sessions[i];
                    if (session.State == AudioSessionState.AudioSessionStateActive && clients.Contains(session.GetProcessID))
                        inputs.Add(new(session.GetProcessID, device.FriendlyName));
                }
            }
            return new(true, inputs.ToArray());
        }
        catch (Exception error) { return new(false, inputs.ToArray(), error.GetType().Name); }
    }
}

// A disconnected microphone route invalidates the whole desktop turn. Never
// resume/replay fragments of that turn when the device subsequently reconnects.
public sealed class DesktopSpeechGate
{
    private readonly object _sync = new();
    private bool _desktop, _blocked;
    public bool IsDesktop { get { lock (_sync) return _desktop; } }
    public void End() { lock (_sync) { _desktop = false; _blocked = false; } }
    public bool Begin(string source, bool echoEnabled, bool routeReady)
    {
        lock (_sync) { _desktop = source.StartsWith("codex", StringComparison.OrdinalIgnoreCase);
            _blocked = _desktop && echoEnabled && !routeReady; return !_blocked; }
    }
    public bool Allow(bool echoEnabled, bool routeReady)
    {
        lock (_sync)
        {
            if (_desktop && echoEnabled && !routeReady) _blocked = true;
            return !_desktop || !_blocked;
        }
    }
}
