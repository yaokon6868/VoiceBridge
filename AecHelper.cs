using System.Diagnostics;
using System.Text.Json;

namespace VoiceBridge;

// Owns only our microphone processor. No system default devices are changed.
public sealed class AecHelper : IDisposable
{
    private Process? _process;
    private readonly string _root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "voice-aec"));
    private readonly CancellationTokenSource _stop = new();
    private string _error = "";
    private readonly object _sync = new();
    public bool Ready
    {
        get
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "status.json")));
                var data = document.RootElement;
                return _process is { HasExited: false }
                    && data.GetProperty("parentPid").GetInt32() == Environment.ProcessId
                    && data.GetProperty("state").GetString() == "running"
                    && DateTimeOffset.UtcNow.ToUnixTimeSeconds() - data.GetProperty("at").GetDouble() < 5;
            }
            catch { return false; }
        }
    }
    public void Start() => _ = Task.Run(async () =>
    {
        while (!_stop.IsCancellationRequested)
        {
            lock (_sync)
            {
                if (!_stop.IsCancellationRequested && (_process is null || _process.HasExited))
                {
                    try
                    {
                        _process?.Dispose();
                        var info = new ProcessStartInfo(Path.Combine(_root, ".venv", "Scripts", "python.exe"))
                        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = _root };
                        info.ArgumentList.Add(Path.Combine(_root, "aec_bridge.py"));
                        info.ArgumentList.Add("--status"); info.ArgumentList.Add(Path.Combine(_root, "status.json"));
                        info.ArgumentList.Add("--parent-pid"); info.ArgumentList.Add(Environment.ProcessId.ToString());
                        _process = Process.Start(info);
                        _error = "";
                    }
                    catch (Exception ex) { _error = ex.GetType().Name; }
                }
            }
            try { await Task.Delay(5000, _stop.Token); } catch (OperationCanceledException) { break; }
        }
    });
    public object Status()
    {
        try
        {
            var file = Path.Combine(_root, "status.json");
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var data = document.RootElement;
            return new { fresh = _process is { HasExited: false } && data.GetProperty("parentPid").GetInt32() == Environment.ProcessId
                    && DateTimeOffset.UtcNow.ToUnixTimeSeconds() - data.GetProperty("at").GetDouble() < 5,
                data = data.Clone(), error = _error };
        }
        catch { return new { fresh = false, error = _error.Length > 0 ? _error : "starting" }; }
    }
    public void Dispose()
    {
        _stop.Cancel();
        lock (_sync)
        {
            try { if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true); } catch { }
            _process?.Dispose(); _process = null;
        }
    }
}
