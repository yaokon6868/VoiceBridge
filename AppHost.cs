using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace VoiceBridge;

public sealed class AppHost : IDisposable
{
    private readonly SettingsStore _settings = new();
    private readonly OverlayWindow _overlay;
    private readonly TextPipeline _pipeline;
    private readonly SessionRouter _router;
    private readonly FishTtsClient _fish;
    private readonly AecHelper? _aec;
    private readonly BridgeServer _server;
    private readonly CodexAccessibilityWatcher _codex;
    private readonly ChatGptAudioMuter _muter = new();
    private readonly CancellationTokenSource _stop = new();
    private Forms.NotifyIcon? _tray;
    private Forms.ContextMenuStrip? _trayMenu;
    private SettingsWindow? _settingsWindow;
    private long _sourceEvents;
    private long _sourceTextEvents;
    private long _phrases;
    private readonly EventJournal _journal = new();
    private bool TtsReady => _fish.CanReplace && _settings.Value.EnableTts && !string.IsNullOrWhiteSpace(_settings.GetApiKey()) && !string.IsNullOrWhiteSpace(_settings.Value.FishVoiceId);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> _webStatus = new();

    public AppHost()
    {
        // An explicit preview launcher enables AEC without altering saved stable settings.
        _settings.Value.ExperimentalEchoReference = Environment.GetCommandLineArgs().Contains("--echo-cancel");
        if (_settings.Value.ExperimentalEchoReference) _aec = new AecHelper();
        _overlay = new OverlayWindow(_settings);
        _pipeline = new TextPipeline(_settings.Value);
        _router = new SessionRouter(ev =>
        {
            if(ev.Source.StartsWith("codex", StringComparison.OrdinalIgnoreCase))
            {
                if(ev.Type=="start" && TtsReady)_muter.MuteDesktopApps();
                else if(ev.Type is "stop" or "interrupt")_muter.Restore();
            }
            else if(ev.Type=="start")_muter.Restore();
            _pipeline.Handle(ev);
        });
        _fish = new FishTtsClient(_settings);
        _fish.Failed += reason =>
        {
            _muter.Restore();
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                ShowBalloon("换声暂停", "已停止换声并允许网页恢复官方语音。原因：" + reason + "。可从托盘重试。", Forms.ToolTipIcon.Warning));
        };
        _codex = new CodexAccessibilityWatcher(Receive);
        _server = new BridgeServer(Receive, () => new {
            sourceEvents = Interlocked.Read(ref _sourceEvents),
            sourceTextEvents = Interlocked.Read(ref _sourceTextEvents),
            fishStatus = _fish.Status,
            fallbackReason = _fish.FaultReason,
            ttsReady = TtsReady,
            phrases = Interlocked.Read(ref _phrases),
            audioBytes = _fish.AudioBytes,
            firstAudioMs = _fish.FirstAudioMs,
            fishConnections = Interlocked.Read(ref _fish.Connections),
            fishDisconnects = Interlocked.Read(ref _fish.Disconnects),
            fishServerErrors = Interlocked.Read(ref _fish.ServerErrors),
            playback = _fish.PlaybackStatus,
            recentEvents = _journal.Snapshot(),
            echoCancellation = _aec?.Status(),
            codexScans = _codex.Scans,
            codexActive = _codex.ActiveSources,
            codexScanMs = _codex.LastScanMs,
            codexError = _codex.LastError,
            web = _webStatus
        }, () => TtsReady, () => _aec?.Ready == true);
        _pipeline.CaptionChanged += text => System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _overlay.SetCaption(text);
            _overlay.SetVisible(text.Length > 0);
        });
        _pipeline.PhraseReady += text => { Interlocked.Increment(ref _phrases); _ = _fish.SpeakAsync(text); };
        _pipeline.Interrupted += () =>
        {
            _fish.Interrupt();
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => { _overlay.SetCaption(""); _overlay.SetVisible(false); });
        };
    }

    public void Start()
    {
        _aec?.Start();
        CreateTray();
        _ = Task.Run(async () =>
        {
            try { await _server.StartAsync(_stop.Token); }
            catch (Exception ex) { ShowBalloon("启动失败", $"本地桥接端口 {RuntimeProfile.Port} 无法启动：{ex.Message}"); }
        });
        if (RuntimeProfile.DesktopCapture && _settings.Value.WatchCodexDesktop) _codex.Start();
        if (string.IsNullOrWhiteSpace(_settings.GetApiKey()) || string.IsNullOrWhiteSpace(_settings.Value.FishVoiceId))
        {
            ShowBalloon("VoiceBridge 已启动", "字幕功能已就绪。请打开设置填写 Fish Audio API Key 和 Voice ID。", Forms.ToolTipIcon.Info);
            System.Windows.Application.Current.Dispatcher.BeginInvoke(OpenSettings);
        }
    }

    private void Receive(BridgeEvent ev)
    {
        if (ev.Type == "web-status")
        {
            try {
                var data = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(ev.Text ?? "{}");
                if (_webStatus.Count < 20 || _webStatus.ContainsKey(ev.Source))
                    _webStatus[ev.Source] = new { at = DateTimeOffset.UtcNow, data };
            } catch { }
            return;
        }
        if (ev.Source != "diagnostic")
        {
            Interlocked.Increment(ref _sourceEvents);
            if (ev.Type == "text") Interlocked.Increment(ref _sourceTextEvents);
        }
        _journal.Record(ev);
        _router.Handle(ev);
    }

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        _trayMenu = menu;
        menu.ShowImageMargin = false;
        menu.Font = new Font("Microsoft YaHei UI", 10);
        menu.Padding = new Forms.Padding(6);
        menu.BackColor = Color.FromArgb(25, 32, 48);
        menu.ForeColor = Color.White;
        menu.Renderer = new TrayMenuRenderer();
        menu.Items.Add("重试 Fish 换声", null, (_, _) => _fish.Retry());
        menu.Items.Add("接入普通版和隔离版 Codex", null, (_, _) =>
        {
            _codex.Start();
            ShowBalloon("桌面接入已启动", "已监听两套 Codex。仅在语音回复有新文本时接管朗读。", Forms.ToolTipIcon.Info);
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("设置与外观", null, (_, _) => OpenSettings());
        menu.Items.Add("停止当前朗读", null, (_, _) => _fish.Interrupt());
        menu.Items.Add("调整字幕位置", null, (_, _) => { _settings.Value.MouseThrough=false; _settings.Save(); _overlay.ApplyAppearance(); _overlay.SetCaption("拖动调整位置 · 右下角缩放"); _overlay.SetVisible(true); });
        menu.Items.Add("显示字幕窗", null, (_, _) => { _overlay.SetCaption("VoiceBridge 字幕预览"); _overlay.SetVisible(true); });
        menu.Items.Add("隐藏字幕窗", null, (_, _) => _overlay.SetVisible(false));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出 VoiceBridge", null, (_, _) =>
        {
            // Let the native popup finish closing before beginning shutdown.
            menu.Close();
            if (_tray is not null) _tray.Visible=false;
            System.Windows.Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => System.Windows.Application.Current.Shutdown()));
        });
        _tray = new Forms.NotifyIcon
        {
            Text = "VoiceBridge 0.3.0 Beta",
            Icon = TrayArtwork.Create(),
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => OpenSettings();
    }

    private void OpenSettings()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_settingsWindow is not null) { _settingsWindow.Activate(); return; }
            var window = new SettingsWindow(_settings);
            _settingsWindow = window;
            window.Closed += (_, _) => _settingsWindow=null;
            window.PreviewRequested += () => { _overlay.ApplyAppearance(); _overlay.SetCaption("你好，这是字幕效果预览。\n拖动和缩放，让字幕适合你的屏幕。"); _overlay.SetVisible(true); };
            if (window.ShowDialog() == true)
            {
                _overlay.ApplyAppearance();
                if (!TtsReady) { _fish.Interrupt(); _muter.Restore(); }
                ShowBalloon("设置已保存", "新的字幕与 Fish Audio 设置已生效。", Forms.ToolTipIcon.Info);
            }
        });
    }

    private void ShowBalloon(string title, string text, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Error)
    {
        if (_tray is null) return;
        _tray.BalloonTipTitle = title;
        _tray.BalloonTipText = text;
        _tray.BalloonTipIcon = icon;
        _tray.ShowBalloonTip(5000);
    }

    public void Dispose()
    {
        if (_tray is not null) { _tray.Visible=false; var icon=_tray.Icon; _tray.Dispose(); _tray=null; icon?.Dispose(); }
        _trayMenu?.Close();
        _trayMenu?.Dispose();
        _settingsWindow?.Close();
        _overlay.Close();
        _stop.Cancel();
        _codex.Dispose();
        _server.Dispose();
        _pipeline.Dispose();
        _fish.Dispose();
        _aec?.Dispose();
        _muter.Dispose();
        _stop.Dispose();
    }
}
