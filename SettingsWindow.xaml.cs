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
