namespace VoiceBridge;

public sealed record BridgeEvent(
    string Type,
    string Source,
    string SessionId,
    string? Text = null,
    string Mode = "snapshot",
    long? ClientTimestamp = null);

public sealed class AppSettings
{
    public const string FreeFishModel = "s2.1-pro-free";
    public double Left { get; set; } = 180;
    public double Top { get; set; } = 720;
    public double Width { get; set; } = 1180;
    public double Height { get; set; } = 190;
    public double FontSize { get; set; } = 34;
    public double TextOpacity { get; set; } = 1.0;
    public double BackgroundOpacity { get; set; } = 0.16;
    public bool MouseThrough { get; set; } = true;
    public int PhraseDelayMs { get; set; } = 130;
    public int PhraseMinChars { get; set; } = 8;
    public string FishVoiceId { get; set; } = "";
    public string FishModel { get; set; } = FreeFishModel;
    public string ProtectedFishApiKey { get; set; } = "";
    public bool EnableTts { get; set; } = true;
    public bool WatchCodexDesktop { get; set; } = true;
    // Off until real speaker/microphone double-talk acceptance passes.
    public bool ExperimentalEchoReference { get; set; } = false;
}
