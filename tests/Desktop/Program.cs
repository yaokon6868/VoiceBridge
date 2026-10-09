using VoiceBridge;
using System.Text.Json;
using NAudio.CoreAudioApi;
using System.Diagnostics;
using System.Windows.Automation;

if (args.Contains("--tree-shape"))
{
    var pid = int.Parse(args[Array.IndexOf(args, "--pid") + 1]);
    using var process = Process.GetProcessById(pid);
    var tree = DesktopTranscriptReader.CaptureTree(process.MainWindowHandle);
    var paths = new List<DesktopUiNode[]>();
    void Walk(DesktopUiNode node, DesktopUiNode[] parents)
    {
        var path = parents.Append(node).ToArray();
        if (node.Name is "你说：" or "You said:") paths.Add(path);
        foreach (var child in node.Children) Walk(child, path);
    }
    Walk(tree, []);
    Console.WriteLine(JsonSerializer.Serialize(paths.TakeLast(1).Select(path => path.TakeLast(5).Select(n => new {
        n.Kind, nameLength = n.Name.Length, children = n.Children.Select(c => new { c.Kind, length = c.Name.Length,
            shortLabel = c.Kind == "ControlType.Text" && c.Name.Length <= 10 ? c.Name : "",
            marker = c.Name is "你说：" or "ChatGPT 说：", childKinds = c.Children.Select(g => g.Kind).ToArray() }).ToArray()
    }).ToArray()).ToArray()));
    return;
}

if (args.Contains("--inspect-transcript"))
{
    var selectedPid = args.Contains("--pid") ? int.Parse(args[Array.IndexOf(args, "--pid") + 1]) : throw new ArgumentException("Supply the intended main-window --pid.");
    foreach (var process in Process.GetProcessesByName("ChatGPT"))
    using (process)
    {
        if (process.Id != selectedPid) continue;
        if (process.MainWindowHandle == IntPtr.Zero) continue;
        var root = AutomationElement.FromHandle(process.MainWindowHandle);
        var cache = new CacheRequest { TreeScope = TreeScope.Element, AutomationElementMode = AutomationElementMode.None };
        cache.Add(AutomationElement.NameProperty); cache.Add(AutomationElement.ControlTypeProperty);
        cache.Add(AutomationElement.AutomationIdProperty);
        AutomationElementCollection nodes;
        using (cache.Activate()) nodes = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        var rows = new List<object>();
        var lastRole = -1;
        for (var i = 0; i < nodes.Count; i++)
            if (nodes[i].Cached.Name is "ChatGPT 说：" or "ChatGPT said:" or "ChatGPT says:") lastRole = i;
        for (var i = Math.Max(0, lastRole - 5); i < nodes.Count; i++)
        {
            var node = nodes[i].Cached;
            var name = node.Name ?? "";
            if (name.Length == 0) continue;
            rows.Add(new { index = i, type = node.ControlType.ProgrammaticName, id = node.AutomationId,
                length = name.Length, name = name[..Math.Min(name.Length, 190)] });
            if (rows.Count >= 65) break;
        }
        Console.WriteLine(JsonSerializer.Serialize(new { pid = process.Id, nodeCount = nodes.Count, lastRole, rows }));
    }
    return;
}

if (args.Contains("--reader-probe") || args.Contains("--trace-voice"))
{
    var selectedPid = args.Contains("--pid") ? int.Parse(args[Array.IndexOf(args, "--pid") + 1]) : throw new ArgumentException("Supply the intended main-window --pid.");
    var deadline = DateTime.UtcNow.AddSeconds(args.Contains("--trace-voice") ? 25 : 0);
    DesktopTranscriptFrame? previous = null;
    do
    {
        using var process = Process.GetProcessById(selectedPid);
        DesktopTranscriptFrame frame;
        try { frame = DesktopTranscriptReader.Capture(process.MainWindowHandle); }
        catch (Exception ex) { Console.WriteLine(JsonSerializer.Serialize(new { cacheFailure = ex.GetType().Name })); await Task.Delay(350); continue; }
        if (previous is null || frame != previous)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { at = DateTimeOffset.Now,
                frame.VoiceVisible, frame.IdleVisible, frame.Speaker, frame.RoleCount, frame.NodeCount,
                textLength = frame.Text.Length, frame.Complete,
                prefixPreserved = previous is null || frame.Text.StartsWith(previous.Text, StringComparison.Ordinal),
                userChanged = previous is not null && previous.UserKey != frame.UserKey,
                route = DesktopEchoRoute.Inspect() }));
        }
        previous = frame;
        if (DateTime.UtcNow >= deadline) break;
        await Task.Delay(350);
    } while (true);
    return;
}

if (args.Contains("--mute-probe"))
{
    object[] Snapshot()
    {
        var pids = new HashSet<uint>();
        foreach (var process in Process.GetProcessesByName("ChatGPT")) using (process) pids.Add((uint)process.Id);
        var result = new List<object>();
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        using (device)
        {
            var sessions = device.AudioSessionManager.Sessions;
            for (var i = 0; i < sessions.Count; i++)
            {
                using var session = sessions[i];
                if (pids.Contains(session.GetProcessID))
                    result.Add(new { device = device.FriendlyName, pid = session.GetProcessID, muted = session.SimpleAudioVolume.Mute });
            }
        }
        return result.ToArray();
    }
    var before = Snapshot();
    object[] during;
    object status;
    using (var muter = new ChatGptAudioMuter()) { muter.MuteDesktopApps(); during = Snapshot(); status = muter.Status(); }
    Console.WriteLine(JsonSerializer.Serialize(new { before, during, after = Snapshot(), status })); return;
}

if (args.Length == 3 && args[0] == "--request-exit")
{
    var result = GracefulAppShutdown.Request(int.Parse(args[1]), args[2]);
    Console.WriteLine(JsonSerializer.Serialize(new { ShutdownResult = result, Forced = false }));
    Environment.ExitCode = result == 0 ? 0 : 1; return;
}

if (args.Contains("--inventory"))
{
    Console.WriteLine(JsonSerializer.Serialize(DesktopEchoRoute.Inspect())); return;
}
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS: " + name); }
var clean = new DesktopCaptureEndpoint(1, DesktopRouteSnapshot.CleanMicrophone);
var physical = new DesktopCaptureEndpoint(2, "Physical mic");
Check(!new DesktopRouteSnapshot(true, []).Connected, "idle capture is not proof of AEC routing");
Check(!new DesktopRouteSnapshot(false, [clean]).Connected, "partial endpoint enumeration cannot approve playback");
Check(!new DesktopRouteSnapshot(true, [physical]).Connected, "physical input blocks desktop echo replacement");
Check(new DesktopRouteSnapshot(true, [clean, clean with { ProcessId = 2 }]).Connected, "both Codex clients can use clean input");
Check(!new DesktopRouteSnapshot(true, [clean, physical]).Connected, "mixed client inputs do not count as verified");
var gate = new DesktopSpeechGate();
Check(gate.Begin("chatgpt-web-1", true, false) && gate.Allow(true, false), "desktop readiness cannot block web captions or playback");
Check(gate.Begin("codex-isolated", false, false) && gate.Allow(false, false), "AEC-disabled existing behavior is preserved");
Check(!gate.Begin("codex-isolated", true, false) && !gate.Allow(true, true), "unsafe desktop turn is not resumed mid-sentence");
Check(gate.Begin("codex-normal", true, true) && gate.Allow(true, true), "next clean desktop turn can start");
Check(!gate.Allow(true, false) && !gate.Allow(true, true), "route loss stops replacement and rejects late fragments");
Check(gate.Begin("chatgpt-web-2", true, false), "web turn recovers independently of desktop route loss");
using var pipeline = new TextPipeline(new AppSettings());
var captions = "";
var phrases = new List<string>();
var flowGate = new DesktopSpeechGate();
var routeReady = false;
var router = new SessionRouter(ev => {
    if (ev.Type == "start") flowGate.Begin(ev.Source, true, routeReady);
    pipeline.Handle(ev);
});
pipeline.CaptionChanged += text => captions = text;
pipeline.PhraseReady += text => { if (flowGate.Allow(true, routeReady)) phrases.Add(text); };
const string reply = "字幕仍应及时显示，这句话不能交给 Fish。";
router.Handle(new("start", "codex-isolated", "one"));
router.Handle(new("text", "codex-isolated", "one", reply));
await Task.Delay(300);
Check(captions == reply && phrases.Count == 0, "unsafe route preserves real pipeline captions while withholding synthesis");
routeReady = true;
router.Handle(new("text", "codex-isolated", "one", reply + "接通后不复读旧回复。"));
await Task.Delay(300);
Check(phrases.Count == 0, "route recovery cannot replay old response fragments");
router.Handle(new("start", "chatgpt-web-1", "two"));
router.Handle(new("text", "chatgpt-web-1", "two", "网页新的正常回复，仍然使用逗号断句。"));
await Task.Delay(300);
Check(phrases.Count > 0 && captions.StartsWith("网页"), "web synthesis remains available after blocked desktop reply");

gate.End();
Check(!gate.IsDesktop && gate.Allow(true, false), "ended desktop turn cannot leave stale route rejection in the next idle period");
Check(DesktopMutePolicy.ShouldMute(true, true, true, true, false), "connected voice suppresses official audio before assistant text exists");
Check(!DesktopMutePolicy.ShouldMute(true, false, true, true, false)
    && !DesktopMutePolicy.ShouldMute(true, true, true, false, false)
    && !DesktopMutePolicy.ShouldMute(true, true, true, true, true), "TTS failure, unprocessed input or rejected turn restores official voice");
Check(!DesktopMutePolicy.ShouldMute(false, true, true, true, false), "voice exit releases official sound");
var originallyMuted = new FakeAudioSession("speaker-a/session-1", 10, true);
var originallyAudible = new FakeAudioSession("speaker-b/session-2", 10, false);
var fish = new FakeAudioSession("speaker-a/fish", 20, false);
var sessionsForTest = new List<IDesktopAudioSession> { originallyMuted, originallyAudible, fish };
using var audioMuter = new ChatGptAudioMuter(() => sessionsForTest.ToArray(), () => [10]);
audioMuter.MuteDesktopApps();
Check(originallyMuted.Muted && originallyAudible.Muted && !fish.Muted, "only Codex output is muted across both speaker endpoints; Fish remains audible");
var lateSession = new FakeAudioSession("speaker-b/new-session", 10, false);
sessionsForTest.Add(lateSession);
audioMuter.MuteDesktopApps();
Check(lateSession.Muted, "render sessions created after voice start are also suppressed");
var forwarded = new List<BridgeEvent>();
var muteRouter = new SessionRouter(forwarded.Add);
muteRouter.Handle(new("start", "codex-isolated", "first"));
Check(forwarded.Count == 0 && originallyAudible.Muted, "official suppression does not wait for text router to claim a turn");
muteRouter.Handle(new("text", "codex-isolated", "first", "你好。"));
muteRouter.Handle(new("interrupt", "codex-isolated", "first"));
audioMuter.MuteDesktopApps();
Check(originallyAudible.Muted && !fish.Muted, "normal turn interruption does not unmute official voice or mute Fish");
audioMuter.Restore();
Check(originallyMuted.Muted && !originallyAudible.Muted && !lateSession.Muted && !audioMuter.HasLease,
    "same PID sessions recover their separate original mute states");
audioMuter.MuteDesktopApps();
originallyAudible.FailWrite = true;
audioMuter.Restore();
Check(audioMuter.HasLease && originallyAudible.Muted, "failed restore retains the lease for retry");
originallyAudible.FailWrite = false;
audioMuter.Restore();
Check(!originallyAudible.Muted && !audioMuter.HasLease, "later successful restore releases the remaining session");

DesktopUiNode Ui(string kind, string name, params DesktopUiNode[] children) => new("ControlType." + kind, name, children);
const string heading = "如果你经常把手机立起来用，";
const string rest = "先看底座是否稳固，避免手机一碰就倒。其次看角度调节，选择能适应桌面高度的款式。最后考虑收纳和充电空间，折叠后方便携带更实用。";
var parsed = DesktopTranscriptReader.Parse(Ui("Pane", "",
    Ui("Button", "结束语音聊天"), Ui("Text", "你说："), Ui("Text", "手机支架怎么选？"),
    Ui("Text", "ChatGPT 说："), Ui("Text", heading, Ui("Text", heading)),
    Ui("Button", "引用链接", Ui("Text", "不应朗读的按钮文字")),
    Ui("Text", rest[..22]), Ui("Text", rest[22..]), Ui("Text", "回复已完成"),
    Ui("Text", "最新一条回复"), Ui("Text", "不应读的页脚"),
    Ui("Edit", "随心输入", Ui("Text", "不应读的编辑器内容"))));
Check(parsed.VoiceVisible && parsed.Text == heading + rest && parsed.Complete,
    "cached tree extracts all paragraphs once and excludes button, footer and editor descendants");
Check(parsed.UserKey.Length > 0 && parsed.Speaker == DesktopSpeaker.Assistant,
    "reply identity comes from the user message rather than whole-tree role count");
DesktopTranscriptFrame MessageWithTools(string elapsed) => DesktopTranscriptReader.Parse(Ui("Group", "",
    Ui("Text", "你说："), Ui("Group", "", Ui("Group", "", Ui("Text", "手机支架怎么选？"))),
    Ui("Text", "17:50"), Ui("Group", "", Ui("Text", elapsed)),
    Ui("Text", "ChatGPT 说："), Ui("Group", "", Ui("Text", heading)),
    Ui("Group", "", Ui("Text", rest)), Ui("Text", "18:01"), Ui("Text", "不应朗读的状态")));
var clockBoundary = MessageWithTools("正在思考 11 秒");
Check(clockBoundary.UserKey == MessageWithTools("正在思考 12 秒").UserKey && clockBoundary.Text == heading + rest,
    "verified message clock boundary excludes changing tool status from user identity and assistant speech");
Check(DesktopTranscriptReader.Parse(Ui("Group", "", Ui("Text", "ChatGPT 说："),
    Ui("Group", "", Ui("Text", "12:30")))).Text == "12:30",
    "a time inside a message paragraph remains ordinary spoken content");

var observed = new List<BridgeEvent>();
var delivered = new List<string>();
var observedCaption = "";
using var capturePipeline = new TextPipeline(new AppSettings());
capturePipeline.PhraseReady += delivered.Add;
capturePipeline.CaptionChanged += text => observedCaption = text;
var captureRouter = new SessionRouter(capturePipeline.Handle);
var captureSession = new DesktopTranscriptSession("codex-isolated", ev => { observed.Add(ev); captureRouter.Handle(ev); });
var testNow = DateTime.UtcNow;
DesktopTranscriptFrame Frame(DesktopSpeaker speaker, string text = "", bool voice = true, int roles = 2,
    bool complete = false, string user = "phone") => new(voice, !voice, speaker, user, text, complete, roles, 20);
captureSession.Observe(Frame(DesktopSpeaker.User, roles: 1), testNow);
captureSession.Observe(Frame(DesktopSpeaker.Assistant, heading), testNow.AddMilliseconds(100));
Check(string.Concat(delivered) == heading, "first comma phrase is submitted before the rest of the response");
captureSession.Observe(Frame(DesktopSpeaker.None, voice: false, roles: 0), testNow.AddSeconds(11));
captureSession.Observe(Frame(DesktopSpeaker.None, voice: false, roles: 0), testNow.AddSeconds(12));
Check(captureSession.Active && DesktopMutePolicy.ShouldMute(captureSession.Active, true, true, true, false),
    "recorded 1.3-second control remount cannot stop voice or release official mute");
var revisedHeading = heading[..^1] + ",";
var longReply = revisedHeading + rest;
captureSession.Observe(Frame(DesktopSpeaker.Assistant, longReply, roles: 40, complete: true), testNow.AddSeconds(12.33));
for (var i = 0; i < 12; i++) captureSession.Observe(Frame(DesktopSpeaker.Assistant, longReply, roles: 40, complete: true), testNow.AddSeconds(13 + i * .1));
await Task.Delay(300);
Check(string.Concat(delivered) == heading + rest && observedCaption == longReply,
    "punctuation revision plus remount delivers the entire long tail without rereading its heading");
Check(!observed.Any(ev => ev.Type is "stop" or "interrupt") && observed.Count(ev => ev.Type == "complete") == 1,
    "tree role-count changes and repeated completion cannot cancel or duplicate the reply");
captureSession.Observe(Frame(DesktopSpeaker.None, roles: 0), testNow.AddSeconds(15));
Check(captureSession.Active && observed.Count(ev => ev.Type == "text") == 2,
    "empty transcript frame preserves the current cursor and emits no destructive event");
captureSession.Observe(Frame(DesktopSpeaker.User, roles: 41, user: "new question"), testNow.AddSeconds(16));
Check(observed.Count(ev => ev.Type == "interrupt") == 1,
    "an actual new user turn interrupts immediately");
captureSession.Observe(Frame(DesktopSpeaker.Assistant, longReply, roles: 42, complete: true, user: "new question"), testNow.AddSeconds(17));
await Task.Delay(300);
Check(string.Concat(delivered) == heading + rest + longReply,
    "a legitimately identical reply to a new user question is delivered once");
captureSession.Observe(Frame(DesktopSpeaker.None, voice: false), testNow.AddSeconds(18));
captureSession.Observe(Frame(DesktopSpeaker.None, voice: false), testNow.AddSeconds(20.1));
Check(!captureSession.Active && observed.Last().Type == "stop"
    && !DesktopMutePolicy.ShouldMute(captureSession.Active, true, true, true, false),
    "confirmed voice closure stops the turn and restores official sound");

var baselineEvents = new List<BridgeEvent>();
var baseline = new DesktopTranscriptSession("codex-normal", baselineEvents.Add);
baseline.Observe(Frame(DesktopSpeaker.Assistant, longReply, complete: true), testNow);
baseline.Observe(Frame(DesktopSpeaker.Assistant, longReply, complete: true), testNow.AddSeconds(1));
Check(!baselineEvents.Any(ev => ev.Type == "text"), "opening voice never replays existing chat history");
for (var seed = 1; seed <= 8; seed++)
{
    var result = new List<string>();
    using var variedPipeline = new TextPipeline(new AppSettings());
    variedPipeline.PhraseReady += result.Add;
    var variedRouter = new SessionRouter(variedPipeline.Handle);
    var variedSession = new DesktopTranscriptSession("codex-isolated", variedRouter.Handle);
    variedSession.Observe(Frame(DesktopSpeaker.User, roles: 1), testNow);
    var random = new Random(seed);
    for (var end = 0; end < longReply.Length;)
    {
        end = Math.Min(longReply.Length, end + random.Next(1, 13));
        variedSession.Observe(Frame(DesktopSpeaker.Assistant, longReply[..end], roles: seed + 5), testNow.AddMilliseconds(end));
    }
    variedSession.Observe(Frame(DesktopSpeaker.Assistant, longReply, roles: seed + 5, complete: true), testNow.AddSeconds(1));
    await Task.Delay(250);
    Check(string.Concat(result) == longReply, $"varying incremental snapshot splits preserve every character once (seed {seed})");
}

sealed class FakeAudioSession(string key, uint processId, bool muted) : IDesktopAudioSession
{
    public string Key => key;
    public uint ProcessId => processId;
    public bool FailWrite;
    public bool Muted { get => muted; set { if (FailWrite) throw new IOException("test failure"); muted = value; } }
    public void Dispose() { }
}
