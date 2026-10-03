namespace VoiceBridge;
public interface IAudioPlayback : IDisposable
{
    event Action<string>? Failed;
    long BeginTurn();
    long ResetDevice();
    void EnsureStarted(bool echoReference, long epoch);
    bool Enqueue(byte[] pcm, long epoch);
    object Snapshot();
    void Stop();
}
