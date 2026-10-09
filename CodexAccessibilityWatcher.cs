using System.Diagnostics;
using System.Management;

namespace VoiceBridge;

public sealed class CodexAccessibilityWatcher : IDisposable
{
    private sealed class State(DesktopTranscriptSession transcript)
    {
        public DesktopTranscriptSession Transcript = transcript;
        public DateTime LastGoodUtc = DateTime.UtcNow;
    }
    private readonly Action<BridgeEvent> _emit;
    private readonly Dictionary<int, State> _states = new();
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    public long Scans { get; private set; }
    public int ActiveSources { get; private set; }
    public double LastScanMs { get; private set; }
    public string LastError { get; private set; } = "";
    public bool IsRunning => _loop is not null;
    public long TextSnapshots { get; private set; }
    public long TextRevisions { get; private set; }
    public long TransientFrames { get; private set; }

    public CodexAccessibilityWatcher(Action<BridgeEvent> emit) => _emit = emit;
    public void Start() => _loop ??= Task.Run(LoopAsync);

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var timer = Stopwatch.StartNew();
            try { LastError = ""; Scan(); }
            catch (Exception ex) { LastError = ex.GetType().Name; }
            LastScanMs = timer.Elapsed.TotalMilliseconds;
            Scans++;
            try { await Task.Delay(ActiveSources > 0 ? 100 : 750, _stop.Token); }
            catch (OperationCanceledException) { }
        }
    }

    private void Scan()
    {
        var alive = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName("ChatGPT"))
        using (process)
        {
            if (process.MainWindowHandle == IntPtr.Zero) continue;
            alive.Add(process.Id);
            try
            {
            if (!_states.TryGetValue(process.Id, out var state))
                _states[process.Id] = state = new(new(SourceName(process.Id), _emit));
            state.Transcript.Observe(DesktopTranscriptReader.Capture(process.MainWindowHandle), DateTime.UtcNow);
            state.LastGoodUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                LastError = $"pid={process.Id}:{ex.GetType().Name}";
                if (_states.TryGetValue(process.Id, out var stale) && DateTime.UtcNow - stale.LastGoodUtc > TimeSpan.FromSeconds(3))
                    stale.Transcript.Stop();
            }
        }
        foreach (var pid in _states.Keys.Where(p => !alive.Contains(p)).ToArray())
        {
            var state = _states[pid];
            state.Transcript.Stop();
            _states.Remove(pid);
        }
        ActiveSources = _states.Values.Count(s => s.Transcript.Active);
        TextSnapshots = _states.Values.Sum(s => s.Transcript.TextSnapshots);
        TextRevisions = _states.Values.Sum(s => s.Transcript.Revisions);
        TransientFrames = _states.Values.Sum(s => s.Transcript.TransientFrames);
    }

    private static string SourceName(int pid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId={pid}");
            foreach (ManagementObject process in searcher.Get())
            using (process)
            {
                var command = process["CommandLine"]?.ToString() ?? "";
                return command.Contains("Codex-Isolated", StringComparison.OrdinalIgnoreCase) ? "codex-isolated" : "codex-normal";
            }
        }
        catch { }
        return "codex-desktop";
    }

    public void Dispose() => _stop.Cancel();
}
