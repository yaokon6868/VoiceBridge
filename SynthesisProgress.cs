namespace VoiceBridge;

// One counter object per generation; cancelled tasks retain their old object.
// Text and credentials are never stored here. Accepted PCM is not audible PCM.
internal sealed class SynthesisProgress(long serial, long epoch)
{
    public long Epoch = epoch;
    public long RequestedPhrases, RequestedChars, PendingPhrases, PendingChars;
    public long SentPhrases, SentChars, Flushes;
    public long ReceivedAudioChunks, ReceivedAudioBytes, AcceptedAudioChunks, AcceptedAudioBytes, RejectedAudioChunks;
    public long LastSendUtcMs, LastAudioUtcMs;
    public int ServerFinished, ReceiverClosed, Cancelled;
    public object Snapshot() => new {
        turnSerial = serial, epoch = Interlocked.Read(ref Epoch),
        requestedPhrases = Interlocked.Read(ref RequestedPhrases), requestedChars = Interlocked.Read(ref RequestedChars),
        pendingPhrases = Interlocked.Read(ref PendingPhrases), pendingChars = Interlocked.Read(ref PendingChars),
        sentPhrases = Interlocked.Read(ref SentPhrases), sentChars = Interlocked.Read(ref SentChars), flushes = Interlocked.Read(ref Flushes),
        receivedAudioChunks = Interlocked.Read(ref ReceivedAudioChunks), receivedAudioBytes = Interlocked.Read(ref ReceivedAudioBytes),
        acceptedAudioChunks = Interlocked.Read(ref AcceptedAudioChunks), acceptedAudioBytes = Interlocked.Read(ref AcceptedAudioBytes),
        rejectedAudioChunks = Interlocked.Read(ref RejectedAudioChunks),
        lastSendUtcMs = Interlocked.Read(ref LastSendUtcMs), lastAudioUtcMs = Interlocked.Read(ref LastAudioUtcMs),
        serverFinished = Volatile.Read(ref ServerFinished) != 0, receiverClosed = Volatile.Read(ref ReceiverClosed) != 0,
        cancelled = Volatile.Read(ref Cancelled) != 0,
        audioMeaning = "accepted-into-playback-queue"
    };
}
