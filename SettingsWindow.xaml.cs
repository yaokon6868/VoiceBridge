using System.Windows;

namespace VoiceBridge;

public partial class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly string? _programDirectory;
    public event Action? PreviewRequested;
    private void Preview_Click(object sender, RoutedEventArgs e) => PreviewRequested?.Invoke();
    public SettingsWindow(SettingsStore store)
    {
        InitializeComponent();
        _store = store;
        Title = $"VoiceBridge {RuntimeProfile.Version} · 设置";
        VersionText.Text = $"当前版本：{RuntimeProfile.Version}";
        StartupProfileText.Text = RuntimeProfile.IsCandidate ? "启动配置：候选测试" : "启动配置：日常";
        DesktopStartupText.Text = RuntimeProfile.DesktopCapture
            ? "自动桌面接入：启动时已请求"
            : "自动桌面接入：启动时未请求";
        var programPath = Environment.ProcessPath;
        ProgramFilePath.Text = programPath ?? "无法获取当前程序路径";
        _programDirectory = programPath is null ? null : Path.GetDirectoryName(programPath);
        OpenProgramFolder.IsEnabled = !string.IsNullOrWhiteSpace(_programDirectory);
        SettingsDirectory.Text = RuntimeProfile.SettingsRoot;
        var s = store.Value;
        ApiKey.Password = store.GetApiKey();
        VoiceId.Text = s.FishVoiceId;
        Model.Text = s.FishModel;
        EnableTts.IsChecked = s.EnableTts;
        FontSizeSlider.Value = s.FontSize;
        TextOpacity.Value = s.TextOpacity;
        BackgroundOpacity.Value = s.BackgroundOpacity;
        MouseThrough.IsChecked = s.MouseThrough;
        WatchCodex.IsChecked = s.WatchCodexDesktop;
        EchoCancellation.IsChecked = s.EchoCancellationEnabled;
        PhysicalMic.Text = s.PhysicalMicrophone;
        SpeakerName.Text = s.SpeakerDevice;
        _=LoadAudioDevicesAsync();
    }

    private void OpenProgramFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_programDirectory)) return;
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
            {
                UseShellExecute = true
            };
            startInfo.ArgumentList.Add(_programDirectory);
            System.Diagnostics.Process.Start(startInfo);
        }
        catch
        {
            System.Windows.MessageBox.Show("无法打开程序文件夹，请复制上方程序路径后在文件资源管理器中查找。", "VoiceBridge", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async Task LoadAudioDevicesAsync()
    {
        var path=Path.Combine(AppContext.BaseDirectory,"aec","VoiceBridge.Aec.exe");
        if(!File.Exists(path))return;
        try
        {
            using var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path,"--devices")
            {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=System.Text.Encoding.UTF8});
            if(process is null)return;
            var output=await process.StandardOutput.ReadToEndAsync();await process.WaitForExitAsync();
            if(process.ExitCode!=0)return;
            var devices=System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(output);
            foreach(var device in devices.EnumerateArray())
            {
                var name=device.GetProperty("name").GetString()!;
                if(name.Contains("cable",StringComparison.OrdinalIgnoreCase)||name.Contains("virtual",StringComparison.OrdinalIgnoreCase))continue;
                if(device.GetProperty("inputs").GetInt32()>0)PhysicalMic.Items.Add(name);
                if(device.GetProperty("outputs").GetInt32()>0)SpeakerName.Items.Add(name);
            }
        }catch { /* Manual entry remains available when inventory is unavailable. */ }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = _store.Value;
        _store.SetApiKey(ApiKey.Password);
        s.FishVoiceId = VoiceId.Text.Trim();
        s.FishModel = AppSettings.FreeFishModel;
        s.EnableTts = EnableTts.IsChecked == true;
        s.FontSize = FontSizeSlider.Value;
        s.TextOpacity = TextOpacity.Value;
        s.BackgroundOpacity = BackgroundOpacity.Value;
        s.MouseThrough = MouseThrough.IsChecked == true;
        s.WatchCodexDesktop = WatchCodex.IsChecked == true;
        s.EchoCancellationEnabled = EchoCancellation.IsChecked == true;
        s.PhysicalMicrophone = PhysicalMic.Text.Trim();
        s.SpeakerDevice = SpeakerName.Text.Trim();
        try
        {
            _store.Save();
            var persisted = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsStore.SettingsPath));
            if (persisted is null || persisted.ProtectedFishApiKey != s.ProtectedFishApiKey || persisted.FishVoiceId != s.FishVoiceId)
                throw new IOException("保存后核对配置失败。");
        }
        catch
        {
            System.Windows.MessageBox.Show("设置未能保存，请保留窗口中的内容后重试。", "VoiceBridge", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        DialogResult = true;
    }
}
