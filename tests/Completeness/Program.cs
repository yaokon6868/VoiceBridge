using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using VoiceBridge;

var failures = new List<string>();
var checks = 0;
void Check(bool pass, string boundary, string? evidence = null)
{
    checks++;
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")}: {boundary}");
    if (!pass)
    {
        failures.Add(boundary);
        if (evidence is not null) Console.WriteLine("  " + evidence);
    }
}
async Task Until(Func<bool> condition, string boundary)
{
    var deadline = DateTime.UtcNow.AddSeconds(6);
    while (!condition())
    {
        if (DateTime.UtcNow >= deadline) throw new TimeoutException(boundary);
        await Task.Delay(10);
    }
}

var runs = new ConcurrentDictionary<string, ServerRun>();
var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
await using var app = builder.Build();
app.UseWebSockets();
app.Map("/tts/{run}", async context =>
{
    var run = runs[context.Request.RouteValues["run"]!.ToString()!];
    run.Models.Enqueue(context.Request.Headers["model"].ToString());
    run.TestAuthorization.Enqueue(context.Request.Headers.Authorization.ToString() == "Bearer offline-completeness-key");
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var buffer = new byte[65536];
    var pendingText = new Queue<string>();
    try
    {
        while (socket.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult received;
            do
            {
                received = await socket.ReceiveAsync(buffer, context.RequestAborted);
                if (received.MessageType == WebSocketMessageType.Close) return;
                message.Write(buffer, 0, received.Count);
            } while (!received.EndOfMessage);
            var payload = MessagePackSerializer.Deserialize<Dictionary<string, object>>(message.ToArray(), ContractlessStandardResolver.Options);
            var kind = payload["event"].ToString();
            if (kind == "text")
            {
                var text = payload["text"].ToString()!;
                run.Texts.Enqueue(text);
                pendingText.Enqueue(text);
            }
            if (kind != "flush") continue;
            if (pendingText.Count != 1) throw new InvalidOperationException("Each flush must follow exactly one text message.");
            pendingText.Dequeue();
            // Numbered PCM frames distinguish omission, duplication and reordering.
            for (var frame = 0; frame < 3; frame++)
            {
                var pcm = new byte[882];
                BitConverter.GetBytes(Interlocked.Increment(ref run.Sequence)).CopyTo(pcm, 0);
                var audio = new Dictionary<string, object> { ["event"] = "audio", ["audio"] = pcm };
                await socket.SendAsync(MessagePackSerializer.Serialize(audio, ContractlessStandardResolver.Options), WebSocketMessageType.Binary, true, context.RequestAborted);
            }
            if (run.Texts.Count < run.ExpectedPhrases) continue;
            if (run.CloseNormally)
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "offline response complete", context.RequestAborted);
            else
                await socket.SendAsync(MessagePackSerializer.Serialize(new Dictionary<string, object> { ["event"] = "finish" }, ContractlessStandardResolver.Options), WebSocketMessageType.Binary, true, context.RequestAborted);
            run.EndSent.TrySetResult(true);
            return;
        }
    }
    catch (WebSocketException) { }
    catch (OperationCanceledException) { }
    catch (Exception error) { run.Errors.Enqueue(error.Message); }
});
await app.StartAsync();
var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();

async Task Run(string name, string[] expectedPhrases, bool normalClose, Func<Flow, ServerRun, Task> feed)
{
    var run = new ServerRun(expectedPhrases.Length, normalClose);
    runs[name] = run;
    var endpoint = new UriBuilder(address) { Scheme = "ws", Path = "tts/" + name }.Uri;
    using var flow = new Flow(endpoint);
    try
    {
        await feed(flow, run);
        await Until(() => run.EndSent.Task.IsCompleted && flow.Playback.Accepted.Length >= expectedPhrases.Length * 3 && flow.Fish.Disconnects > 0,
            name + ": WS completion / accepted PCM boundary");
        await Task.Delay(300); // Repeated completion must not append another send.
        await Task.WhenAll(flow.Sends.ToArray()).WaitAsync(TimeSpan.FromSeconds(6));
        var submitted = flow.Submitted.ToArray();
        var sent = run.Texts.ToArray();
        var wantedPcm = Enumerable.Range(1, expectedPhrases.Length * 3).ToArray();
        Check(submitted.SequenceEqual(expectedPhrases), name + ": pipeline emits each intended phrase exactly once in order",
            "expected=" + JsonSerializer.Serialize(expectedPhrases) + "; actual=" + JsonSerializer.Serialize(submitted));
        Check(sent.SequenceEqual(expectedPhrases), name + ": WS receives all intended text in order without omission or replay",
            "expected=" + JsonSerializer.Serialize(expectedPhrases) + "; actual=" + JsonSerializer.Serialize(sent));
        Check(sent.SequenceEqual(submitted), name + ": synthesis preserves pipeline submission order");
        Check(flow.Interruptions == 1 && flow.Playback.BeginTurns == 1, name + ": idle source and completion never cancel the active turn");
        Check(run.Errors.IsEmpty && run.Models.Count > 0 && run.Models.All(model => model == AppSettings.FreeFishModel)
            && run.TestAuthorization.All(authorized => authorized), name + ": loopback transport uses test credentials and the fixed free model");
        Check(flow.Fish.CanReplace && string.IsNullOrEmpty(flow.Fish.FaultReason), name + ": normal response termination does not trip fallback");
        Check(flow.Playback.Accepted.SequenceEqual(wantedPcm) && flow.Playback.Rejected == 0,
            name + ": accepted PCM sequence is complete, ordered and unique");
        Check(flow.Playback.Buffered == wantedPcm.Length - flow.Playback.Consumed.Length,
            name + ": finish or close preserves all queued unconsumed PCM");
        flow.Playback.Drain();
        Check(flow.Playback.Consumed.SequenceEqual(wantedPcm) && flow.Playback.Buffered == 0,
            name + ": queued PCM can drain completely after finish or close");
        for (var i = 0; i < sent.Length; i++) Console.WriteLine($"TRACE {name} WS[{i + 1}]={JsonSerializer.Serialize(sent[i])}");
        Console.WriteLine($"TRACE {name} PCM accepted=[{string.Join(',', flow.Playback.Accepted)}], consumed=[{string.Join(',', flow.Playback.Consumed)}]");
    }
    catch (Exception error)
    {
        Check(false, name + ": " + error.Message,
            "WS=" + JsonSerializer.Serialize(run.Texts.ToArray()) + "; PCM=" + string.Join(',', flow.Playback.Accepted));
    }
}

const string first = "第一段检查点一这段正文必须从开头完整送出并且不能因为重复快照而再次朗读需要持续保留这一段的全部文字。\n";
const string second = "第二段检查点二这段正文会先出现短片段随后继续增长提前到来的完成提示不能把后半段截掉也不能把短片段拆成几字一顿。\n";
const string third = "第三段检查点三这段正文需要保持原有顺序传给同一个合成会话另一端虽然开启然后停止但没有正文因此不能取消当前朗读。\n";
const string tail = "完毕。";
await Run("long-response", [first, second, third, tail], false, async (flow, run) =>
{
    flow.Send("start");
    flow.Send("text", first[..3]);
    await Task.Delay(1050);
    Check(flow.Submitted.IsEmpty && run.Texts.IsEmpty, "long-response: a short pause does not emit a tiny phrase");
    flow.Send("text", first);
    await Until(() => run.Texts.Count >= 1, "long-response: first paragraph reaches WS");
    flow.Send("text", first);
    flow.Router.Handle(new("start", "codex-idle", "idle"));
    flow.Router.Handle(new("stop", "codex-idle", "idle"));
    flow.Send("text", first + second[..5]);
    flow.Send("complete");
    flow.Send("complete");
    await Task.Delay(80);
    flow.Send("text", first + second);
    flow.Send("text", first + second);
    await Task.Delay(250);
    flow.Send("text", first + second + third[..8]);
    await Task.Delay(1050);
    Check(flow.Submitted.Count == 2, "long-response: another short mid-response pause retains the pending fragment");
    flow.Send("text", first + second + third);
    flow.Send("text", first + second + third + tail);
    flow.Send("text", first + second + third + tail);
    flow.Send("complete");
    flow.Send("complete");
});

const string closeText = "正常关闭连接以后已经接收的音频仍然需要完整保留直到记录播放器把队列排空。";
await Run("normal-close", [closeText], true, (flow, run) =>
{
    flow.Send("start"); flow.Send("text", closeText); flow.Send("complete");
    return Task.CompletedTask;
});

const string originalPrefix = "已经朗读的第一段正文保持完整并且后续修订不能再把这一段重复送去朗读。";
const string revisedTail = "未读尾部必须从第一个字一直完整保留到最后一个字。";
await Run("insert-before-read-prefix", [originalPrefix, revisedTail], false, async (flow, run) =>
{
    flow.Send("start"); flow.Send("text", originalPrefix + "旧的尾巴");
    await Until(() => run.Texts.Count == 1 && flow.Playback.Accepted.Length == 3, "insert-prefix: first phrase reaches WS and playback");
    flow.Playback.Drain();
    flow.Send("text", "新增说明" + originalPrefix + revisedTail);
    flow.Send("complete"); flow.Send("complete");
});

const string removable = "可以删除的前文标记";
await Run("delete-before-read-prefix", [removable + originalPrefix, revisedTail], false, async (flow, run) =>
{
    flow.Send("start"); flow.Send("text", removable + originalPrefix + "旧的尾巴");
    await Until(() => run.Texts.Count == 1 && flow.Playback.Accepted.Length == 3, "delete-prefix: first phrase reaches WS and playback");
    flow.Playback.Drain();
    flow.Send("text", originalPrefix + revisedTail);
    flow.Send("complete"); flow.Send("complete");
});

await app.StopAsync();
Console.WriteLine($"Completeness checks: {checks - failures.Count}/{checks} passed. Loopback WS and a recording playback queue only; no user settings, Fish API, audio device or actual listening was used.");
Environment.ExitCode = failures.Count == 0 ? 0 : 1;

sealed class ServerRun(int expectedPhrases, bool closeNormally)
{
    public int ExpectedPhrases { get; } = expectedPhrases;
    public bool CloseNormally { get; } = closeNormally;
    public ConcurrentQueue<string> Texts { get; } = new();
    public ConcurrentQueue<string> Models { get; } = new();
    public ConcurrentQueue<bool> TestAuthorization { get; } = new();
    public ConcurrentQueue<string> Errors { get; } = new();
    public TaskCompletionSource<bool> EndSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Sequence;
}

sealed class Flow : IDisposable
{
    public RecordingPlayback Playback { get; } = new();
    public FishTtsClient Fish { get; }
    public TextPipeline Pipeline { get; }
    public SessionRouter Router { get; }
    public ConcurrentQueue<string> Submitted { get; } = new();
    public ConcurrentQueue<Task> Sends { get; } = new();
    public int Interruptions;
    public Flow(Uri endpoint)
    {
        var settings = new SettingsStore(inMemory: true);
        settings.SetApiKey("offline-completeness-key");
        settings.Value.FishVoiceId = "offline-reference";
        settings.Value.FishModel = AppSettings.FreeFishModel;
        Fish = new FishTtsClient(settings, Playback, endpoint);
        Pipeline = new TextPipeline(settings.Value);
        Pipeline.Interrupted += () => { Interlocked.Increment(ref Interruptions); Fish.Interrupt(); };
        Pipeline.PhraseReady += phrase => { Submitted.Enqueue(phrase); Sends.Enqueue(Fish.SpeakAsync(phrase)); };
        Router = new SessionRouter(Pipeline.Handle);
    }
    public void Send(string type, string? text = null) => Router.Handle(new(type, "web", "response", text));
    public void Dispose() { Pipeline.Dispose(); Fish.Dispose(); }
}

sealed class RecordingPlayback : IAudioPlayback
{
    private readonly object _sync = new();
    private readonly Queue<int> _buffered = new();
    private readonly List<int> _accepted = new(), _consumed = new();
    private long _epoch;
    public int BeginTurns;
    public int Rejected;
    public event Action<string>? Failed { add { } remove { } }
    public int[] Accepted { get { lock (_sync) return _accepted.ToArray(); } }
    public int[] Consumed { get { lock (_sync) return _consumed.ToArray(); } }
    public int Buffered { get { lock (_sync) return _buffered.Count; } }
    public long BeginTurn() { lock (_sync) { BeginTurns++; _buffered.Clear(); return ++_epoch; } }
    public long ResetDevice() => BeginTurn();
    public void EnsureStarted(bool echoReference, long epoch) { }
    public bool Enqueue(byte[] pcm, long epoch)
    {
        lock (_sync)
        {
            if (epoch != _epoch) { Rejected++; return false; }
            var sequence = BitConverter.ToInt32(pcm, 0);
            _accepted.Add(sequence); _buffered.Enqueue(sequence);
            return true;
        }
    }
    public ValueTask<bool> EnqueueAsync(byte[] pcm, long epoch, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Enqueue(pcm, epoch));
    }
    public void Drain() { lock (_sync) while (_buffered.TryDequeue(out var frame)) _consumed.Add(frame); }
    public object Snapshot() { lock (_sync) return new { epoch = _epoch, bufferedFrames = _buffered.Count, acceptedFrames = _accepted.Count, consumedFrames = _consumed.Count }; }
    public void Stop() => BeginTurn();
    public void Dispose() { }
}
