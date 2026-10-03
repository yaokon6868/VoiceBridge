namespace VoiceBridge;
public interface IAudioPlayback : IDisposable
{
    bool CanStart(bool duplex) => true;
    event Action<string>? Failed;
    long BeginTurn();
    long ResetDevice();
    void EnsureStarted(bool echoReference, long epoch);
    bool Enqueue(byte[] pcm, long epoch);
    ValueTask<bool> EnqueueAsync(byte[] pcm, long epoch, CancellationToken token) => ValueTask.FromResult(Enqueue(pcm, epoch));
    object Snapshot();
    void Stop();
}
