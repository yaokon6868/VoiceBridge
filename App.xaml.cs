using System.Windows;

namespace VoiceBridge;

public partial class App : System.Windows.Application
{
    private AppHost? _host;
    private Mutex? _instanceMutex;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instanceMutex = new Mutex(true, @"Local\VoiceBridge.Next.Singleton", out _ownsMutex);
        if (!_ownsMutex)
        {
            System.Windows.MessageBox.Show("VoiceBridge 已在运行，请双击系统托盘图标打开设置。", "VoiceBridge");
            Shutdown();
            return;
        }
        _host = new AppHost();
        _host.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        if (_ownsMutex) _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
