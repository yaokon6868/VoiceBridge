using VoiceBridge;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

var events=new ConcurrentQueue<BridgeEvent>();
using var server=new BridgeServer(events.Enqueue,port:17895);
var shutdownCalls = 0;
var settingsCalls = 0;
server.ShutdownRequested = () => Interlocked.Increment(ref shutdownCalls);
server.SettingsRequested = () => Interlocked.Increment(ref settingsCalls);
await server.StartAsync(CancellationToken.None);
using var client=new HttpClient { BaseAddress=new Uri("http://127.0.0.1:17895") };
void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS: "+name);}
Check((await client.GetAsync("health")).StatusCode==HttpStatusCode.Unauthorized,"health requires a session token");
using(var web=new HttpRequestMessage(HttpMethod.Post,"session")){
    web.Headers.Add("Origin","https://evil.example");web.Headers.Add("X-VoiceBridge-Client","extension");
    Check((await client.SendAsync(web)).StatusCode==HttpStatusCode.Forbidden,"website cannot bootstrap a session");
}
using(var web=new HttpRequestMessage(HttpMethod.Post,"session")){
    web.Headers.Add("Sec-Fetch-Site","cross-site");web.Headers.Add("X-VoiceBridge-Client","extension");
    Check((await client.SendAsync(web)).StatusCode==HttpStatusCode.Forbidden,"cross-site request without Origin cannot bootstrap");
}
using var pairing=new HttpRequestMessage(HttpMethod.Post,"session");
pairing.Headers.Add("Origin","chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
pairing.Headers.Add("X-VoiceBridge-Client","extension");
var paired=await client.SendAsync(pairing);paired.EnsureSuccessStatusCode();
var token=(await paired.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
client.DefaultRequestHeaders.Authorization=new("Bearer",token);
Check((await client.GetAsync("health")).IsSuccessStatusCode,"installed extension can connect");
var connection=JsonSerializer.SerializeToElement(server.ConnectionDiagnostics);
Check(connection.GetProperty("paired").GetInt64()==1 && connection.GetProperty("pairingRejected").GetInt64()==2
    && connection.GetProperty("unauthorized").GetInt64()==1,"connection diagnostics distinguish absent, outdated and denied clients without logging tokens");
var startupHealth = (await client.GetFromJsonAsync<JsonElement>("health")).GetProperty("startup");
Check(!startupHealth.TryGetProperty("settingsRoot", out _) && !startupHealth.TryGetProperty("executablePath", out _),
    "health does not disclose configuration or executable paths");
Check(startupHealth.GetProperty("profile").GetString() == "candidate" && startupHealth.GetProperty("desktopRequested").GetBoolean(),
    "launcher can verify the actual startup profile and desktop request");
using(var web=new HttpRequestMessage(HttpMethod.Get,"diagnostics")){
    web.Headers.Add("Origin","https://evil.example");
    Check((await client.SendAsync(web)).StatusCode==HttpStatusCode.Unauthorized,"website cannot use even a leaked token");
}
Check((await client.PostAsJsonAsync("event",new BridgeEvent("start","test","one"))).IsSuccessStatusCode,"valid authenticated event is accepted");
Check((await client.PostAsJsonAsync("event",new BridgeEvent("unknown","test","one"))).StatusCode==HttpStatusCode.BadRequest,"unknown event is rejected");
Check((await client.PostAsJsonAsync("event",new BridgeEvent("text","","one","test"))).StatusCode==HttpStatusCode.BadRequest,"invalid source is rejected");
Check((await client.PostAsJsonAsync("event",new BridgeEvent("text","test","one",new string('x',33000)))).StatusCode==HttpStatusCode.BadRequest,"oversized text is rejected");
using(var socket=new ClientWebSocket()){
    socket.Options.SetRequestHeader("Authorization","Bearer "+token);
    await socket.ConnectAsync(new Uri("ws://127.0.0.1:17895/ws"),CancellationToken.None);
    await socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new BridgeEvent("text","test","one","Hello."))),WebSocketMessageType.Text,true,CancellationToken.None);
    var until=DateTime.UtcNow.AddSeconds(2);while(events.Count<2 && DateTime.UtcNow<until)await Task.Delay(10);
    Check(events.Count==2,"authenticated WebSocket feeds the same event pipeline");
    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure,"done",CancellationToken.None);
}
using(var socket=new ClientWebSocket()){
    socket.Options.SetRequestHeader("Origin","https://evil.example");
    try { await socket.ConnectAsync(new Uri("ws://127.0.0.1:17895/ws"),CancellationToken.None);throw new Exception("unexpected connection"); }
    catch(WebSocketException){Console.WriteLine("PASS: website WebSocket cannot submit speech");}
}
Check((await client.PostAsync("show-settings", null)).StatusCode == HttpStatusCode.Forbidden, "settings requires a native launcher header");
using (var webSettings = new HttpRequestMessage(HttpMethod.Post, "show-settings")) {
    webSettings.Headers.Add("Origin", "chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    webSettings.Headers.Add("X-VoiceBridge-Client", "launcher");
    Check((await client.SendAsync(webSettings)).StatusCode == HttpStatusCode.Forbidden, "browser cannot open application settings");
}
using (var nativeSettings = new HttpRequestMessage(HttpMethod.Post, "show-settings")) {
    nativeSettings.Headers.Add("X-VoiceBridge-Client", "launcher");
    Check((await client.SendAsync(nativeSettings)).StatusCode == HttpStatusCode.OK && settingsCalls == 1 && shutdownCalls == 0,
        "launcher opens settings without requesting shutdown");
}
using (var extensionQuit = new HttpRequestMessage(HttpMethod.Post, "shutdown")) {
    extensionQuit.Headers.Add("Origin", "chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    extensionQuit.Headers.Add("X-VoiceBridge-Client", "launcher");
    Check((await client.SendAsync(extensionQuit)).StatusCode == HttpStatusCode.Forbidden, "even authenticated browser extension cannot stop the application");
}
Check((await client.PostAsync("shutdown", null)).StatusCode == HttpStatusCode.Forbidden, "quit requires explicit native launcher header");
using (var nativeQuit = new HttpRequestMessage(HttpMethod.Post, "shutdown")) {
    nativeQuit.Headers.Add("X-VoiceBridge-Client", "launcher");
    Check((await client.SendAsync(nativeQuit)).StatusCode == HttpStatusCode.Accepted, "authenticated native launcher can request graceful exit");
}
await Task.Delay(400);
Check(Volatile.Read(ref shutdownCalls) == 1, "only authorized native quit invokes shutdown after the response");

// OnExit runs on a UI context which has stopped pumping. Hold an actual local
// WebSocket open to force asynchronous Kestrel shutdown, then dispose there.
using var exitCancellation = new CancellationTokenSource();
var exitServer = new BridgeServer(_ => { }, port: 17897);
await exitServer.StartAsync(exitCancellation.Token);
using var exitClient = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:17897") };
using var exitPair = new HttpRequestMessage(HttpMethod.Post, "session");
exitPair.Headers.Add("X-VoiceBridge-Client", "extension");
using var exitPairResponse = await exitClient.SendAsync(exitPair);
var exitToken = (await exitPairResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
using var heldSocket = new ClientWebSocket();
heldSocket.Options.SetRequestHeader("Authorization", "Bearer " + exitToken);
await heldSocket.ConnectAsync(new Uri("ws://127.0.0.1:17897/ws"), CancellationToken.None);
using var exited = new ManualResetEventSlim();
Exception? exitError = null;
var stalledUi = new NonPumpingContext();
var exitThread = new Thread(() => {
    SynchronizationContext.SetSynchronizationContext(stalledUi);
    try { exitCancellation.Cancel(); exitServer.Dispose(); }
    catch (Exception ex) { exitError = ex; }
    finally { exited.Set(); }
}) { IsBackground = true };
exitThread.Start();
Check(exited.Wait(TimeSpan.FromSeconds(7)) && exitError is null,
    "real bridge exits with an open WebSocket on a UI context that cannot pump continuations");
Check(stalledUi.Posted == 0, "bridge cleanup never queues a continuation onto the exiting UI thread");
heldSocket.Abort();
exitServer.Dispose();

sealed class NonPumpingContext : SynchronizationContext
{
    private int _posted;
    public int Posted => Volatile.Read(ref _posted);
    public override void Post(SendOrPostCallback callback, object? state) => Interlocked.Increment(ref _posted);
}
