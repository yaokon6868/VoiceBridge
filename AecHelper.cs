using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace VoiceBridge;

// Owns the duplex engine and its authenticated PCM-only IPC, not system audio.
public sealed class AecHelper : IDisposable
{
    private readonly SettingsStore _settings;
    private readonly CancellationTokenSource _stop = new();
    private readonly HttpClient _http = new() { BaseAddress=new Uri("http://127.0.0.1:17896"), Timeout=TimeSpan.FromSeconds(2) };
    private Process? _process;
    private string _secret="";
    private string _error="";
    private JsonElement _status;
    private long _lastBarge;
    private readonly object _sync=new();
    private Task? _worker;
    private int _started;
    private bool _hadReady;
    public event Action? BargeIn;
    public event Action<string>? Failed;
    public AecHelper(SettingsStore settings) => _settings=settings;
    public bool Ready
    {
        get { lock(_sync) {try {return _process is {HasExited:false} && _status.ValueKind==JsonValueKind.Object
            && _status.TryGetProperty("state",out var state) && state.GetString()=="running"
            && _status.TryGetProperty("at",out var at) && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()/1000d-at.GetDouble()<1
            && _status.TryGetProperty("cableAt",out var cableAt) && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()/1000d-cableAt.GetDouble()<1;}catch(InvalidOperationException){return false;} } }
    }
    public object Status() { lock(_sync) return new { ready=Ready,error=_error,data=_status.ValueKind==JsonValueKind.Object?(object)_status.Clone():null }; }
    private HttpRequestMessage Request(HttpMethod method,string path)
    {
        var request=new HttpRequestMessage(method,path);
        request.Headers.Add("X-VoiceBridge-Aec-Token",Volatile.Read(ref _secret));
        return request;
    }
    public async Task CancelAsync(long epoch)
    {
        if(!Ready)return;
        try {
            using var request=Request(HttpMethod.Post,"cancel");request.Headers.Add("X-Epoch",epoch.ToString());
            request.Content=new ByteArrayContent([]);
            using var response=await _http.SendAsync(request,_stop.Token);
        } catch(OperationCanceledException){} catch(HttpRequestException){}
    }
    public async ValueTask<bool> EnqueueAsync(byte[] pcm,long epoch,CancellationToken token)
    {
        for(var offset=0;offset<pcm.Length;)
        {
            token.ThrowIfCancellationRequested();
            if(!Ready)throw new IOException("Duplex engine is unavailable");
            var length=Math.Min(16384,pcm.Length-offset);
            using var request=Request(HttpMethod.Post,"audio");request.Headers.Add("X-Epoch",epoch.ToString());
            request.Content=new ByteArrayContent(pcm,offset,length);
            using var response=await _http.SendAsync(request,token);
            if(response.StatusCode==HttpStatusCode.Gone)return false;
            if((int)response.StatusCode==429){await Task.Delay(15,token);continue;}
            response.EnsureSuccessStatusCode();offset+=length;
        }
        return true;
    }
    public void Start()
    {
        if(Interlocked.Exchange(ref _started,1)!=0)return;
        _worker=Task.Run(async()=>
    {
        var configuration="";
        while(!_stop.IsCancellationRequested)
        {
            try
            {
                var wanted=_settings.Value.EchoCancellationEnabled;
                var next=$"{_settings.Value.PhysicalMicrophone}|{_settings.Value.SpeakerDevice}";
                if(!wanted || configuration!=next){_hadReady=false;StopProcess();configuration=next;}
                if(wanted && _process is not {HasExited:false})
                {
                    if(_hadReady){_hadReady=false;Failed?.Invoke("echo-engine-unavailable");}
                    StopProcess();
                    var executable=Path.Combine(AppContext.BaseDirectory,"aec","VoiceBridge.Aec.exe");
                    if(!File.Exists(executable))throw new FileNotFoundException("回声处理组件未安装",executable);
                    _secret=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));_lastBarge=0;
                    var info=new ProcessStartInfo(executable) { UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(executable)! };
                    foreach(var argument in new[]{"--secret",_secret,"--parent-pid",Environment.ProcessId.ToString(),"--mic",_settings.Value.PhysicalMicrophone,"--speaker",_settings.Value.SpeakerDevice})info.ArgumentList.Add(argument);
                    _process=Process.Start(info);
                }
                if(wanted)
                {
                    using var request=Request(HttpMethod.Get,"status");
                    using var response=await _http.SendAsync(request,_stop.Token);response.EnsureSuccessStatusCode();
                    var data=JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(_stop.Token));
                    lock(_sync){_status=data;_error="";}
                    var ready=Ready;if(_hadReady && !ready)Failed?.Invoke("echo-engine-unavailable");_hadReady=ready;
                    var barge=data.GetProperty("bargeIns").GetInt64();
                    if(barge>_lastBarge){_lastBarge=barge;BargeIn?.Invoke();}
                }
            }
            catch(OperationCanceledException) when(_stop.IsCancellationRequested){break;}
            catch(Exception ex)
            {
                var lost=_hadReady;_hadReady=false;
                lock(_sync){_error=ex is FileNotFoundException?"回声处理组件未安装":ex.GetType().Name;_status=default;}
                if(lost)Failed?.Invoke("echo-engine-unavailable");
            }
            try { await Task.Delay(_process is {HasExited:false}?150:2000,_stop.Token); }catch(OperationCanceledException){break;}
        }
    });
    }
    private void StopProcess()
    {
        lock(_sync){_status=default;}
        if(_process is {HasExited:false}){try {_process.Kill();_process.WaitForExit(2000);}catch(InvalidOperationException){}}
        _process?.Dispose();_process=null;
    }
    public void Dispose(){_stop.Cancel();try{_worker?.Wait(2500);}catch(AggregateException){}StopProcess();_http.Dispose();}
}
