namespace VoiceBridge;

public static class RuntimeProfile
{
    public const string Version = "0.3.1-candidate";
    public const int Port = 17892;
    // Build identity must select the configuration even when Explorer starts
    // the EXE without flags. Explicit flags remain compatible with launchers.
    public static bool IsCandidate => Version.Contains("-candidate", StringComparison.OrdinalIgnoreCase)
        || Environment.GetCommandLineArgs().Contains("--candidate");
    public static bool DesktopCapture => IsCandidate || Environment.GetCommandLineArgs().Contains("--desktop");
    public static bool Background => Environment.GetCommandLineArgs().Contains("--background");
    public static bool EchoCancellationForStartup(bool savedChoice, bool hasSavedSettings, bool requested)
        => hasSavedSettings ? savedChoice : savedChoice || requested;
    public static string SettingsRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), IsCandidate?".voicebridge-next-candidate":".voicebridge-next");
}
