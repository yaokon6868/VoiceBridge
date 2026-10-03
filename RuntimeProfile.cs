namespace VoiceBridge;

public static class RuntimeProfile
{
    public const string Version = "0.3.1-candidate";
    public const int Port = 17892;
    public static bool DesktopCapture => Environment.GetCommandLineArgs().Contains("--desktop");
    public static string SettingsRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetCommandLineArgs().Contains("--candidate")?".voicebridge-next-candidate":".voicebridge-next");
}
