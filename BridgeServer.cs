using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VoiceBridge;

public sealed class BridgeServer : IDisposable
{
    private readonly Action<BridgeEvent> _receive;
    private readonly Func<object>? _diagnostics;
    private readonly Func<bool> _ttsReady;
    private readonly Func<bool> _aecReady;
    public Func<bool> EchoEnabled { get; set; } = () => false;
    private WebApplication? _app;
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly int _port;
    private readonly ConnectionGuard _guard = new();
    public BridgeServer(Action<BridgeEvent> receive, Func<object>? diagnostics = null, Func<bool>? ttsReady = null, Func<bool>? aecReady = null, int port = RuntimeProfile.Port)
    {
        _receive = receive;
        _diagnostics = diagnostics;
        _ttsReady = ttsReady ?? (() => false);
        _aecReady = aecReady ?? (() => false);
        _port = port;
    }

    public async Task StartAsync(CancellationToken token)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(o => { o.ListenLocalhost(_port); o.Limits.MaxRequestBodySize = 65536; });
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNameCaseInsensitive = true);
        _app = builder.Build();
        _app.Use(async (context,next) =>
        {
            context.Response.Headers.CacheControl="no-store";
            if(context.Request.Path=="/session")
            {
                if(context.Request.Method!="POST" || !_guard.TrustedBootstrap(context.Request)) { context.Response.StatusCode=403; return; }
                await context.Response.WriteAsJsonAsync(new { token=_guard.Token }); return;
            }
            if(!_guard.Authorized(context.Request)) { context.Response.StatusCode=401; return; }
            await next(context);
        });
        _app.UseWebSockets();
        _app.MapGet("/health", () => Results.Json(new { ok = true, service = "VoiceBridge Next", version = RuntimeProfile.Version, instanceId = _instanceId, ttsReady = _ttsReady(), aecReady = _aecReady(), aecEnabled=EchoEnabled() }));
        _app.MapGet("/diagnostics", () => Results.Json(_diagnostics?.Invoke() ?? new { }));
        _app.MapPost("/event", (BridgeEvent ev) => { if(!ValidEvent(ev))return Results.BadRequest(); _receive(ev); return Results.Ok(); });
        _app.Map("/ws", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var buffer = new byte[64 * 1024];
            while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, token);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    message.Write(buffer, 0, result.Count);
                    if(message.Length > 65536) { await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig,"Event too large", token); return; }
                } while (!result.EndOfMessage);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,"Closed",token);
                    break;
                }
                try
                {
                    var ev = JsonSerializer.Deserialize<BridgeEvent>(message.ToArray(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (ev is not null && ValidEvent(ev)) _receive(ev);
                }
                catch { }
            }
        });
        await _app.StartAsync(token);
    }

    private static bool ValidEvent(BridgeEvent ev) => ev.Type is "start" or "text" or "complete" or "stop" or "interrupt" or "web-status"
        && !string.IsNullOrWhiteSpace(ev.Source) && ev.Source.Length<=128
        && !string.IsNullOrWhiteSpace(ev.SessionId) && ev.SessionId.Length<=128
        && (ev.Text?.Length??0)<=32768 && ev.Mode is "snapshot" or "delta";

    public void Dispose()
    {
        if (_app is null) return;
        try { _app.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); } catch { }
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
