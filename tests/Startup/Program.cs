using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using VoiceBridge;

if (args.Contains("--profile-probe"))
{
    Console.WriteLine(JsonSerializer.Serialize(new {
        candidateSettings = RuntimeProfile.SettingsRoot.EndsWith(".voicebridge-next-candidate"),
        desktop = RuntimeProfile.DesktopCapture
    }));
    return;
}

var failures = new List<string>();
var checks = 0;
void Check(bool pass, string name)
{
    checks++;
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")}: {name}");
    if (!pass) failures.Add(name);
}

async Task<JsonElement> Probe(params string[] flags)
{
    var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    info.ArgumentList.Add("--profile-probe");
    foreach (var flag in flags) info.ArgumentList.Add(flag);
    using var process = Process.Start(info)!;
    var output = await process.StandardOutput.ReadToEndAsync();
    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
    if (process.ExitCode != 0) throw new Exception(await process.StandardError.ReadToEndAsync());
    return JsonSerializer.Deserialize<JsonElement>(output);
}

var plain = await Probe();
var launcher = await Probe("--candidate", "--desktop", "--echo-cancel");
var background = await Probe("--background");
Check(plain.GetProperty("candidateSettings").GetBoolean(), "direct candidate EXE uses the candidate settings, preserving saved voice and AEC choices");
Check(plain.GetProperty("desktop").GetBoolean(), "direct candidate EXE requests desktop capture just like its launcher");
Check(plain.ToString() == launcher.ToString(), "direct EXE and explicit launcher select the same profile");
Check(plain.ToString() == background.ToString(), "background launch does not switch configuration or desktop mode");

var echoPolicy = typeof(RuntimeProfile).GetMethod("EchoCancellationForStartup");
Check(echoPolicy is not null, "startup has an explicit policy for saved AEC choices");
if (echoPolicy is not null)
{
    bool Echo(bool saved, bool exists, bool requested) => (bool)echoPolicy.Invoke(null, [saved, exists, requested])!;
    Check(!Echo(false, true, true), "launcher AEC flag does not override a saved disabled choice");
    Check(Echo(true, true, false), "direct EXE preserves a saved enabled AEC choice");
    Check(Echo(false, false, true), "first launch can initialize AEC from an explicit request");
    Check(!Echo(false, false, false), "first launch without an AEC request keeps the default");
}

var activationType = Assembly.GetExecutingAssembly().GetType("VoiceBridge.ExistingInstanceActivation");
Check(activationType is not null, "repeat launch can activate the existing instance without starting an audio engine");
if (activationType is not null)
{
    var method = activationType.GetMethod("TryShowSettingsAsync")!;
    Task<bool> Activate(Uri uri) => (Task<bool>)method.Invoke(null, [uri])!;
    Check(!await Activate(new Uri("https://example.com/")), "activation refuses non-loopback servers");

    async Task<(bool Activated, bool NativeHeaders)> Serve(bool accept)
    {
        var reserve = new TcpListener(IPAddress.Loopback, 0);
        reserve.Start();
        var port = ((IPEndPoint)reserve.LocalEndpoint).Port;
        reserve.Stop();
        var uri = new Uri($"http://127.0.0.1:{port}/");
        using var listener = new HttpListener();
        listener.Prefixes.Add(uri.ToString());
        listener.Start();
        var headersValid = true;
        var server = Task.Run(async () => {
            var session = await listener.GetContextAsync();
            headersValid &= session.Request.HttpMethod == "POST" && session.Request.Url!.AbsolutePath == "/session"
                && session.Request.Headers["X-VoiceBridge-Client"] == "extension";
            var body = Encoding.UTF8.GetBytes("{\"token\":\"test-activation-token\"}");
            session.Response.ContentType = "application/json";
            await session.Response.OutputStream.WriteAsync(body);
            session.Response.Close();
            var show = await listener.GetContextAsync();
            headersValid &= show.Request.HttpMethod == "POST" && show.Request.Url!.AbsolutePath == "/show-settings"
                && show.Request.Headers["X-VoiceBridge-Client"] == "launcher"
                && show.Request.Headers["Authorization"] == "Bearer test-activation-token"
                && show.Request.Headers["Origin"] is null;
            show.Response.StatusCode = accept ? 200 : 403;
            show.Response.Close();
        });
        var activated = await Activate(uri).WaitAsync(TimeSpan.FromSeconds(5));
        await server.WaitAsync(TimeSpan.FromSeconds(5));
        return (activated, headersValid);
    }

    var accepted = await Serve(true);
    Check(accepted.Activated && accepted.NativeHeaders, "repeat launch pairs locally and uses the authenticated native settings request");
    var rejected = await Serve(false);
    Check(!rejected.Activated, "rejected settings request is not reported as successful activation");
}

Console.WriteLine($"Startup checks: {checks - failures.Count}/{checks} passed. No user settings or Fish API were used.");
Environment.ExitCode = failures.Count == 0 ? 0 : 1;
