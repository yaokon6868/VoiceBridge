using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace VoiceBridge;

// Bootstrap is for installed browser extensions, never a normal web page.
// Same-user native programs and installed extensions are trusted; this is not
// isolation from malware running as the Windows user.
public sealed class ConnectionGuard
{
    private readonly string _token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public string Token => _token;
    public bool TrustedBootstrap(HttpRequest request) =>
        request.Headers["X-VoiceBridge-Client"]=="extension" && TrustedContext(request);
    public bool Authorized(HttpRequest request)
    {
        if(!TrustedContext(request))return false;
        var supplied=request.Headers.Authorization.ToString();
        var expected="Bearer "+_token;
        return supplied.Length==expected.Length && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(supplied),Encoding.UTF8.GetBytes(expected));
    }
    private static bool TrustedContext(HttpRequest request)
    {
        var origin=request.Headers.Origin.ToString();
        if(origin.Length>0 && (!Uri.TryCreate(origin,UriKind.Absolute,out var uri) ||
            uri.Scheme!="chrome-extension" || uri.Host.Length!=32 || uri.Host.Any(c=>c<'a'||c>'p')))return false;
        if(origin.Length>0)return true;
        var site=request.Headers["Sec-Fetch-Site"].ToString();
        return site.Length==0 || site=="none";
    }
}
