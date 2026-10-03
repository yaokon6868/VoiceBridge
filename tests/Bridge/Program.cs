using VoiceBridge;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

var events=new ConcurrentQueue<BridgeEvent>();
using var server=new BridgeServer(events.Enqueue,port:17895);
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
Check(!(await (await client.GetAsync("health")).Content.ReadAsStringAsync()).Contains("settingsPath"),"health does not disclose a user path");
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
