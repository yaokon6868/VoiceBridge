using System.Windows;

namespace VoiceBridge;

public partial class App : System.Windows.Application
{
    private AppHost? _host;
    private Mutex? _instanceMutex;
    private bool _ownsMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 3 && e.Args[0] == "--request-exit")
        {
            try { Shutdown(GracefulAppShutdown.Request(int.Parse(e.Args[1]), e.Args[2])); }
            catch (Exception) { Shutdown(1); }
            return;
        }
        _instanceMutex = new Mutex(true, @"Local\VoiceBridge.Next.Singleton", out _ownsMutex);
        if (!_ownsMutex)
        {
            if (!RuntimeProfile.Background && !await ExistingInstanceActivation.TryShowSettingsAsync(new Uri($"http://127.0.0.1:{RuntimeProfile.Port}/")))
                System.Windows.MessageBox.Show("VoiceBridge 已在运行，但未能打开设置。请从托盘打开；切换版本请使用桌面启动入口。", "VoiceBridge");
            Shutdown();
            return;
        }
        _host = new AppHost();
        _host.Start();
        if (!RuntimeProfile.Background) _host.RequestSettings();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        if (_ownsMutex) _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
