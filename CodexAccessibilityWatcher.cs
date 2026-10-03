using System.Diagnostics;
using System.Management;
using System.Windows.Automation;

namespace VoiceBridge;

public sealed class CodexAccessibilityWatcher : IDisposable
{
    private sealed class State
    {
        public string SessionId = "";
        public string Source = "";
        public bool Active;
        public bool Assistant;
        public string LastText = "";
        public int RoleIndex;
        public DateTime LastGoodUtc = DateTime.UtcNow;
    }

    // Verified in the installed Codex voice controls and localization assets.
    private static readonly HashSet<string> VoiceButtons = new(StringComparer.OrdinalIgnoreCase)
    {
        "结束语音聊天", "停止语音聊天", "结束语音", "End voice chat", "Stop voice chat"
    };
    private readonly Action<BridgeEvent> _emit;
    private readonly Dictionary<int, State> _states = new();
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    public long Scans { get; private set; }
    public int ActiveSources { get; private set; }
    public double LastScanMs { get; private set; }
    public string LastError { get; private set; } = "";

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
                _states[process.Id] = state = new State { Source = SourceName(process.Id) };
            var root = AutomationElement.FromHandle(process.MainWindowHandle);
            // Cache values in one provider call instead of cross-process reads per property.
            var cache = new CacheRequest { TreeScope = TreeScope.Element, AutomationElementMode = AutomationElementMode.None };
            cache.Add(AutomationElement.NameProperty);
            cache.Add(AutomationElement.ControlTypeProperty);
            AutomationElementCollection nodes;
            using (cache.Activate())
                nodes = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);

            bool active = false, assistant = false, collect = false, completed = false;
            int roleIndex = 0;
            var text = new System.Text.StringBuilder();
            for (var i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i].Cached;
                var name = n.Name ?? "";
                if (n.ControlType == ControlType.Button && VoiceButtons.Contains(name)) active = true;
                if (name is "ChatGPT 说：" or "ChatGPT said:" or "ChatGPT says:")
                {
                    assistant = true; collect = true; completed = false; roleIndex++; text.Clear(); continue;
                }
                if (name is "你说：" or "You said:" or "You say:")
                {
                    assistant = false; collect = false; completed = false; roleIndex++; text.Clear(); continue;
                }
                if (name is "最新回复" or "Latest response" or "随心输入") collect = false;
                if (collect && name is "回复已完成" or "Response completed") completed = true;
                if (collect && assistant && n.ControlType == ControlType.Text && name.Length > 0
                    && name is not "回复已完成" and not "Response completed")
                    text.Append(name);
            }
            var snapshot = text.ToString();
            if (active && !state.Active)
            {
                state.SessionId = Guid.NewGuid().ToString("N");
                state.LastText = snapshot;
                state.RoleIndex = roleIndex;
                state.Assistant = assistant;
                _emit(new BridgeEvent("start", state.Source, state.SessionId));
            }
            else if (!active && state.Active)
                _emit(new BridgeEvent("stop", state.Source, state.SessionId));
            else if (active)
            {
                if (roleIndex != state.RoleIndex || assistant != state.Assistant)
                {
                    _emit(new BridgeEvent("interrupt", state.Source, state.SessionId));
                    state.SessionId = Guid.NewGuid().ToString("N");
                    state.LastText = "";
                    _emit(new BridgeEvent("start", state.Source, state.SessionId));
                }
                if (assistant && snapshot.Length > 0 && snapshot != state.LastText)
                {
                    // Only new suffixes are eligible for playback. Baseline history is not.
                    if (snapshot.StartsWith(state.LastText, StringComparison.Ordinal))
                        _emit(new BridgeEvent("text", state.Source, state.SessionId, snapshot[state.LastText.Length..], "delta"));
                    state.LastText = snapshot;
                }
                if (assistant && completed) _emit(new BridgeEvent("complete", state.Source, state.SessionId));
                state.RoleIndex = roleIndex;
                state.Assistant = assistant;
            }
            state.Active = active;
            state.LastGoodUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                LastError = $"pid={process.Id}:{ex.GetType().Name}";
                if (_states.TryGetValue(process.Id, out var stale) && stale.Active && DateTime.UtcNow - stale.LastGoodUtc > TimeSpan.FromSeconds(3))
                {
                    _emit(new BridgeEvent("stop", stale.Source, stale.SessionId)); stale.Active = false;
                }
            }
        }
        foreach (var pid in _states.Keys.Where(p => !alive.Contains(p)).ToArray())
        {
            var state = _states[pid];
            if (state.Active) _emit(new BridgeEvent("stop", state.Source, state.SessionId));
            _states.Remove(pid);
        }
        ActiveSources = _states.Values.Count(s => s.Active);
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
