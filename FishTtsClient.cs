using System.Net.WebSockets;
using MessagePack;
using MessagePack.Resolvers;
using NAudio.Wave;

namespace VoiceBridge;

public sealed class FishTtsClient : IDisposable
{
    private readonly SettingsStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _generationLock = new();
    private ClientWebSocket? _socket;
    private CancellationTokenSource _generation = new();
    private readonly IAudioPlayback _playback;
    private readonly Uri _endpoint;
    private readonly TimeSpan _firstAudioTimeout;
    private long _playbackEpoch;
    private string _fault = "";
    public bool CanReplace => string.IsNullOrEmpty(Volatile.Read(ref _fault));
    public string FaultReason => Volatile.Read(ref _fault);
    public event Action<string>? Failed;
    public object PlaybackStatus => _playback.Snapshot();
    public void Retry()
    {
        lock (_generationLock)
        {
            Interrupt(); _playbackEpoch = _playback.ResetDevice(); _fault = ""; Status = "idle";
        }
    }
    private Task? _receiver;
    private readonly MessagePackSerializerOptions _options = ContractlessStandardResolver.Options;
    public string Status { get; private set; } = "idle";
    private long _audioBytes;
    public long AudioBytes => Interlocked.Read(ref _audioBytes);
    private long _firstTextTimestamp;
    public double? FirstAudioMs { get; private set; }
    public long Disconnects;
    public long Connections;
    public long ServerErrors;

    public FishTtsClient(SettingsStore store, IAudioPlayback? playback = null, Uri? endpoint = null, TimeSpan? firstAudioTimeout = null)
    {
        _store = store;
        _playback = playback ?? new AudioPlayback();
        _endpoint = endpoint ?? new Uri("wss://api.fish.audio/v1/tts/live");
        if (!_endpoint.IsLoopback && _endpoint != new Uri("wss://api.fish.audio/v1/tts/live")) throw new ArgumentException("Unsupported TTS endpoint");
        _firstAudioTimeout = firstAudioTimeout ?? TimeSpan.FromSeconds(12);
        _playback.Failed += reason => _ = Task.Run(() => Fail(reason));
    }
    private void Fail(string reason)
    {
        lock (_generationLock)
        {
            Interrupt(); Status = reason;
            // Publish readiness only after cleanup and the final fault status.
            Volatile.Write(ref _fault, reason);
        }
        Failed?.Invoke(reason);
    }
    private void FailFor(CancellationToken token, string reason)
    {
        lock (_generationLock) { if (!token.IsCancellationRequested) Fail(reason); }
    }

    public async Task SpeakAsync(string text)
    {
        if (!CanReplace || !_store.Value.EnableTts || string.IsNullOrWhiteSpace(text)) return;
        if (_store.Value.FishModel != AppSettings.FreeFishModel)
        {
            Status = "blocked-non-free-model";
            return;
        }
        if (string.IsNullOrWhiteSpace(_store.GetApiKey()) || string.IsNullOrWhiteSpace(_store.Value.FishVoiceId))
        {
            Status = "needs-settings";
            return;
        }
        CancellationToken token;
        long epoch;
        lock (_generationLock) { if (!CanReplace) return; token = _generation.Token; epoch = _playbackEpoch; }
        Interlocked.CompareExchange(ref _firstTextTimestamp, System.Diagnostics.Stopwatch.GetTimestamp(), 0);
        {
            var before = AudioBytes;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_firstAudioTimeout, token);
                    lock (_generationLock)
                    {
                        if (!token.IsCancellationRequested && AudioBytes == before)
                            Fail(FirstAudioMs is null ? "first-audio-timeout" : "audio-progress-timeout");
                    }
                }
                catch (OperationCanceledException) { }
            });
        }
        var acquired = false;
        try
        {
            await _gate.WaitAsync(token);
            acquired = true;
            token.ThrowIfCancellationRequested();
            var socket = await EnsureConnectedAsync(token, epoch);
            await SendAsync(socket, new Dictionary<string, object?> { ["event"] = "text", ["text"] = text }, token);
            await SendAsync(socket, new Dictionary<string, object?> { ["event"] = "flush" }, token);
        }
        catch (OperationCanceledException)
        {
            FailFor(token, "connection-timeout");
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            FailFor(token, Status.StartsWith("handshake-failed:") ? Status : "connection-failed:" + ex.GetType().Name);
        }
        finally { if (acquired) _gate.Release(); }
    }

    private async Task<ClientWebSocket> EnsureConnectedAsync(CancellationToken token, long epoch)
    {
        ClientWebSocket socket;
        lock (_generationLock)
        {
            token.ThrowIfCancellationRequested();
            if (_socket?.State == WebSocketState.Open) return _socket;
            ResetSocket(); socket = new ClientWebSocket(); _socket = socket;
        }
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.SetRequestHeader("Authorization", $"Bearer {_store.GetApiKey()}");
        // Model selection is a handshake header, not a query parameter. Never
        // omit it: the service defaults to a paid model when it is missing.
        socket.Options.SetRequestHeader("model", AppSettings.FreeFishModel);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try { await socket.ConnectAsync(_endpoint, timeout.Token); }
        catch
        {
            if (!token.IsCancellationRequested) Status = "handshake-failed:" + (int)socket.HttpStatusCode;
            throw;
        }
        token.ThrowIfCancellationRequested();
        _playback.EnsureStarted(_store.Value.ExperimentalEchoReference, epoch);
        await SendAsync(socket, new Dictionary<string, object?>
        {
            ["event"] = "start",
            ["request"] = new Dictionary<string, object?>
            {
                ["text"] = "",
                ["reference_id"] = _store.Value.FishVoiceId,
                ["format"] = "pcm",
                ["sample_rate"] = 44100,
                ["latency"] = "balanced",
                ["chunk_length"] = 100
            }
        }, token);
        Interlocked.Increment(ref Connections);
        Status = "connected";
        _receiver = ReceiveLoopAsync(socket, token, epoch);
        return socket;
    }

    private async Task SendAsync(ClientWebSocket socket, Dictionary<string, object?> payload, CancellationToken token)
    {
        var bytes = MessagePackSerializer.Serialize(payload, _options);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await socket.SendAsync(bytes, WebSocketMessageType.Binary, true, timeout.Token);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token, long epoch)
    {
        var chunk = new byte[128 * 1024];
        try
        {
            while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                using var data = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(chunk, token);
                    if (result.MessageType == WebSocketMessageType.Close) { if (!token.IsCancellationRequested) Status = "server-closed"; return; }
                    data.Write(chunk, 0, result.Count);
                } while (!result.EndOfMessage);
                var map = MessagePackSerializer.Deserialize<Dictionary<string, object>>(data.ToArray(), _options);
                if (token.IsCancellationRequested) return;
                if (map.TryGetValue("event", out var kind) && (kind?.ToString() == "finish" || kind?.ToString() == "error"))
                {
                    var failed = kind?.ToString() == "error" || (map.TryGetValue("reason", out var reason) && reason?.ToString() == "error");
                    if (failed) Interlocked.Increment(ref ServerErrors);
                    if (failed) FailFor(token, "server-error");
                    else Status = "session-finished";
                    return;
                }
                if (TryGetAudio(map, out var audio))
                {
                    lock (_generationLock)
                    {
                        if(token.IsCancellationRequested)return;
                        if(!_playback.Enqueue(audio,epoch))continue;
                        if (FirstAudioMs is null)
                            FirstAudioMs = System.Diagnostics.Stopwatch.GetElapsedTime(Interlocked.Read(ref _firstTextTimestamp)).TotalMilliseconds;
                        Interlocked.Add(ref _audioBytes, audio.Length);
                        Status = "receiving-audio";
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { FailFor(token, "receiver:"+ex.GetType().Name); }
        finally
        {
            // A dead receiver must not leave an apparently open, unusable socket.
            // Old turns may only detach their own connection.
            if (ReferenceEquals(Interlocked.CompareExchange(ref _socket, null, socket), socket))
            {
                Interlocked.Increment(ref Disconnects);
                try { socket.Abort(); socket.Dispose(); } catch { }
            }
        }
    }

    private static bool TryGetAudio(Dictionary<string, object> map, out byte[] audio)
    {
        audio = Array.Empty<byte>();
        foreach (var key in new[] { "audio", "data" })
        {
            if (!map.TryGetValue(key, out var value)) continue;
            if (value is byte[] bytes) { audio = bytes; return true; }
            if (value is ReadOnlyMemory<byte> memory) { audio = memory.ToArray(); return true; }
        }
        return false;
    }

    public void Interrupt()
    {
        lock (_generationLock)
        {
            _generation.Cancel();
            _generation.Dispose();
            _generation = new CancellationTokenSource();
            ResetSocket();
            _playbackEpoch = _playback.BeginTurn();
            Status = "interrupted";
            Interlocked.Exchange(ref _firstTextTimestamp, 0);
            FirstAudioMs = null;
        }
    }

    private void ResetSocket()
    {
        var socket = Interlocked.Exchange(ref _socket, null);
        try { socket?.Abort(); socket?.Dispose(); } catch { }
    }

    public void Dispose()
    {
        Interrupt();
        _playback.Dispose();
        // In-flight cancelled senders may still release this managed semaphore.
        _generation.Dispose();
    }
}
