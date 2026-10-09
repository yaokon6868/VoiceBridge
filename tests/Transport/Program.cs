using VoiceBridge;
using System.Net.WebSockets;
using MessagePack;
using MessagePack.Resolvers;
using System.Collections.Concurrent;

var builder=WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:17893");
await using var app=builder.Build(); app.UseWebSockets();
var headers=new ConcurrentQueue<string>();
var modes=new ConcurrentQueue<string>();
app.Map("/tts", async context=>{
    headers.Enqueue(context.Request.Headers["model"].ToString());
    using var socket=await context.WebSockets.AcceptWebSocketAsync();
    modes.TryDequeue(out var mode);
    var buffer=new byte[65536];
    var flushes=0;
    try {
        while(socket.State==WebSocketState.Open){
            var result=await socket.ReceiveAsync(buffer,context.RequestAborted);
            if(result.MessageType==WebSocketMessageType.Close)break;
            var map=MessagePackSerializer.Deserialize<Dictionary<string,object>>(buffer.AsMemory(0,result.Count),ContractlessStandardResolver.Options);
            if(map["event"].ToString()!="flush")continue;
            flushes++;
            if(mode=="silent")continue;
            if(mode=="audio-once" && flushes>1)continue;
            object response=mode=="error"?new Dictionary<string,object>{{"event","error"}}:new Dictionary<string,object>{{"event","audio"},{"audio",new byte[882]}};
            await socket.SendAsync(MessagePackSerializer.Serialize(response,ContractlessStandardResolver.Options),WebSocketMessageType.Binary,true,context.RequestAborted);
        }
    } catch(WebSocketException){} catch(OperationCanceledException){}
});
await app.StartAsync();
var store=new SettingsStore(inMemory:true);store.SetApiKey("test-only");store.Value.FishVoiceId="test";
var playback=new FakePlayback();
using var fish=new FishTtsClient(store,playback,new Uri("ws://127.0.0.1:17893/tts"),TimeSpan.FromMilliseconds(350));
void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS: "+name);}
async Task Until(Func<bool> condition){var end=DateTime.UtcNow.AddSeconds(3);while(!condition()){if(DateTime.UtcNow>end)throw new Exception("timeout");await Task.Delay(10);}}
modes.Enqueue("silent");await fish.SpeakAsync("Silent server test.");await Until(()=>!fish.CanReplace);
Check(fish.Status=="first-audio-timeout","silent synthesis opens fallback circuit");
Check(playback.Bytes==0,"silent synthesis cannot claim playback");
fish.Retry();modes.Enqueue("audio");await fish.SpeakAsync("Recovered sentence.");await Until(()=>playback.Bytes>0);
Check(fish.CanReplace,"explicit retry restores synthesis");
Check(playback.Resets>0,"retry rebuilds a failed playback device");
var epoch=playback.Epoch;fish.Interrupt();
Check(!playback.Enqueue(new byte[10],epoch),"late previous-turn PCM is rejected");
Check(playback.Bytes==0,"interrupt clears pending playback");
modes.Enqueue("error");await fish.SpeakAsync("Server error test.");await Until(()=>!fish.CanReplace);
Check(fish.Status=="server-error","server error enables official-voice fallback");
Check(headers.All(x=>x==AppSettings.FreeFishModel),"every handshake locks free model");
fish.Retry();modes.Enqueue("audio-once");await fish.SpeakAsync("First phrase works.");await Until(()=>playback.Bytes>0);
await fish.SpeakAsync("Second phrase stalls.");await Until(()=>!fish.CanReplace);
Check(fish.Status=="audio-progress-timeout","mid-turn silent server cannot leave official voice muted indefinitely");
fish.Retry();modes.Enqueue("silent");await fish.SpeakAsync("Cancelled timeout.");fish.Interrupt();
await Task.Delay(450);Check(fish.CanReplace,"cancelled watchdog cannot fail a newer generation");
fish.Retry();playback.Hold=new(TaskCreationOptions.RunContinuationsAsynchronously);modes.Enqueue("audio");
await fish.SpeakAsync("Bounded playback backpressure.");await Until(()=>playback.Waiting);
await Task.Delay(450);Check(fish.CanReplace,"playback backpressure is not a silent Fish server");
fish.Interrupt();playback.Hold.TrySetResult(true);playback.Hold=null;
await Task.Delay(100);Check(playback.Bytes==0,"interrupt cancels a pending asynchronous playback enqueue");
var progressMethod=typeof(FishTtsClient).GetMethod("ProgressSnapshot");
Check(progressMethod is not null,"Fish exposes per-generation send and audio progress without transcript contents");
if(progressMethod is not null){
    System.Text.Json.JsonElement Progress(){var data=System.Text.Json.JsonSerializer.Serialize(progressMethod.Invoke(fish,null));return System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(data);}
    fish.Retry();modes.Enqueue("audio");
    const string phraseOne="Completeness diagnostic phrase one.";
    const string phraseTwo="Completeness diagnostic phrase two.";
    await fish.SpeakAsync(phraseOne);await fish.SpeakAsync(phraseTwo);
    await Until(()=>Progress().GetProperty("current").GetProperty("acceptedAudioChunks").GetInt64()==2);
    var summary=Progress();var current=summary.GetProperty("current");
    Check(current.GetProperty("sentPhrases").GetInt64()==2 && current.GetProperty("sentChars").GetInt64()==phraseOne.Length+phraseTwo.Length
        && current.GetProperty("pendingPhrases").GetInt64()==0 && current.GetProperty("flushes").GetInt64()==2,"Fish progress proves both phrases were sent and flushed");
    Check(current.GetProperty("acceptedAudioBytes").GetInt64()==playback.Bytes,"audio progress counts accepted PCM rather than assuming playback completed");
    Check(!summary.ToString().Contains(phraseOne) && !summary.ToString().Contains("test-only"),"Fish progress never contains text or API keys");
    fish.Interrupt();summary=Progress();
    Check(summary.GetProperty("current").GetProperty("sentPhrases").GetInt64()==0
        && summary.GetProperty("previous").GetProperty("sentPhrases").GetInt64()==2,"old generation retains its own counts without contaminating the next turn");
    fish.Retry();modes.Enqueue("audio");playback.AfterAccepted=()=>fish.Interrupt();
    await fish.SpeakAsync("Cancellation immediately after a successful enqueue.");
    await Until(()=>Progress().GetProperty("previous").GetProperty("receivedAudioChunks").GetInt64()==1);
    await Task.Delay(50);summary=Progress();playback.AfterAccepted=null;
    Check(summary.GetProperty("previous").GetProperty("acceptedAudioChunks").GetInt64()==1
        && summary.GetProperty("current").GetProperty("acceptedAudioChunks").GetInt64()==0,
        "enqueue accepted immediately before cancellation is counted in the old generation");
}
await app.StopAsync();
var routed=new List<BridgeEvent>();var router=new SessionRouter(routed.Add);
router.Handle(new("start","web","one"));
Check(routed.Count==0,"idle source cannot claim playback");
router.Handle(new("text","web","one","Hello."));
router.Handle(new("start","codex-normal","idle"));router.Handle(new("stop","codex-normal","idle"));
Check(routed.Count==2 && routed[0].Type=="start" && routed[1].Type=="text","other idle client cannot interrupt speech");
router.Handle(new("start","web","two"));router.Handle(new("text","web","one","Stale."));
Check(routed.Count==2,"stale session text cannot re-enter playback");
router.Handle(new("text","web","two","New reply."));router.Handle(new("interrupt","web","two"));
Check(routed[^1].Type=="interrupt","active user interruption is forwarded immediately");
var handoffs=new List<BridgeEvent>();var handoffRouter=new SessionRouter(handoffs.Add);
handoffRouter.Handle(new("start","web","reply-a"));handoffRouter.Handle(new("text","web","reply-a","First source."));
handoffRouter.Handle(new("start","codex-isolated","reply-b"));handoffRouter.Handle(new("text","codex-isolated","reply-b","New active source."));
handoffRouter.Handle(new("text","web","reply-a","Late old source."));
handoffRouter.Handle(new("start","web","reply-a"));handoffRouter.Handle(new("text","web","reply-a","Repeated old source."));
handoffRouter.Handle(new("text","codex-isolated","reply-b","New active source continues."));
Check(handoffs.Count(x=>x.Type=="start")==2 && handoffs.Skip(4).All(x=>x.Source=="codex-isolated"),"late text from a replaced reply cannot repeatedly reclaim playback");
handoffRouter.Handle(new("start","web","reply-c"));handoffRouter.Handle(new("text","web","reply-c","A genuinely new reply."));
Check(handoffs.Count(x=>x.Type=="start")==3 && handoffs[^1].SessionId=="reply-c","a new reply from the previous source can legitimately take over again");
using var phrasesPipeline=new TextPipeline(new AppSettings());
var phraseOutput=new List<string>();phrasesPipeline.PhraseReady+=text=>{lock(phraseOutput)phraseOutput.Add(text);};
phrasesPipeline.Handle(new("start","fragments","one"));
phrasesPipeline.Handle(new("text","fragments","one","几个字"));
await Task.Delay(1050);
Check(phraseOutput.Count==0,"short unpunctuated pause is not synthesized as a tiny fragment");
phrasesPipeline.Handle(new("text","fragments","one","几个字合并成为一句话，"));
Check(phraseOutput.Count==1 && phraseOutput[0].EndsWith('，'),"comma phrase is submitted immediately after merging fragments");
phrasesPipeline.Handle(new("text","fragments","one","几个字合并成为一句话，好。"));
phrasesPipeline.Handle(new("complete","fragments","one"));await Task.Delay(300);
Check(phraseOutput.Count==2 && phraseOutput[1]=="好。","actual completion preserves the short final tail");

sealed class FakePlayback : IAudioPlayback {
    private readonly object sync=new();public long Epoch;public int Bytes;public int Resets;
    public event Action<string>? Failed {add{} remove{}}
    public TaskCompletionSource<bool>? Hold;public bool Waiting;
    public Action? AfterAccepted;
    public long BeginTurn(){lock(sync){Bytes=0;return ++Epoch;}}
    public long ResetDevice(){lock(sync){Resets++;Bytes=0;return ++Epoch;}}
    public void EnsureStarted(bool echo,long epoch){}
    public bool Enqueue(byte[] pcm,long epoch){lock(sync){if(epoch!=Epoch)return false;Bytes+=pcm.Length;return true;}}
    public async ValueTask<bool> EnqueueAsync(byte[] pcm,long epoch,CancellationToken token){var hold=Hold;if(hold is not null){Waiting=true;await hold.Task.WaitAsync(token);}token.ThrowIfCancellationRequested();var accepted=Enqueue(pcm,epoch);if(accepted)AfterAccepted?.Invoke();return accepted;}
    public object Snapshot()=>new{Bytes,Epoch};
    public void Stop()=>BeginTurn();public void Dispose(){}
}
