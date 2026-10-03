using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VoiceBridge;

public sealed class SettingsStore
{
    public static string Root => RuntimeProfile.SettingsRoot;
    public static string SettingsPath => Path.Combine(Root, "settings.json");
    public AppSettings Value { get; private set; }
    private readonly bool _inMemory;

    public SettingsStore(bool inMemory = false)
    {
        _inMemory = inMemory;
        if(inMemory) { Value = new AppSettings(); return; }
        Directory.CreateDirectory(Root);
        try
        {
            Value = File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings()
                : new AppSettings();
        }
        catch { Value = new AppSettings(); }
        // Migrate existing installations before any TTS connection can be made.
        Value.FishModel = AppSettings.FreeFishModel;
        // Loading must not overwrite settings while another version is running.
    }

    public string GetApiKey()
    {
        // A saved key is authoritative, including updates from another settings window.
        // Do not let a stale inherited environment variable override the user's save.
        var protectedKey = Value.ProtectedFishApiKey;
        try
        {
            if (!_inMemory && File.Exists(SettingsPath))
                protectedKey = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath))?.ProtectedFishApiKey ?? protectedKey;
        }
        catch (IOException) { }
        catch (JsonException) { }
        if (string.IsNullOrWhiteSpace(protectedKey))
            return Environment.GetEnvironmentVariable("FISH_AUDIO_API_KEY")?.Trim() ?? "";
        try
        {
            var raw = Convert.FromBase64String(protectedKey);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(raw, null, DataProtectionScope.CurrentUser));
        }
        catch { return ""; }
    }

    public void SetApiKey(string key)
    {
        Value.ProtectedFishApiKey = string.IsNullOrWhiteSpace(key) ? "" : Convert.ToBase64String(
            ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), null, DataProtectionScope.CurrentUser));
    }

    public void Save()
    {
        if (_inMemory) return;
        Value.FishModel = AppSettings.FreeFishModel;
        Directory.CreateDirectory(Root);
        var tempPath = SettingsPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(Value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tempPath, SettingsPath, true);
    }
}
