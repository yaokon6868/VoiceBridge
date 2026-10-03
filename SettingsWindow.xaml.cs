using System.Windows;

namespace VoiceBridge;

public partial class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    public event Action? PreviewRequested;
    private void Preview_Click(object sender, RoutedEventArgs e) => PreviewRequested?.Invoke();
    public SettingsWindow(SettingsStore store)
    {
        InitializeComponent();
        _store = store;
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
