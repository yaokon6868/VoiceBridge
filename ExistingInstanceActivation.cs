using System.Net.Http;
using System.Text.Json;

namespace VoiceBridge;

public static class ExistingInstanceActivation
{
    public static async Task<bool> TryShowSettingsAsync(Uri bridge)
    {
        if (!bridge.IsLoopback || bridge.Scheme != Uri.UriSchemeHttp) return false;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var pair = new HttpRequestMessage(HttpMethod.Post, new Uri(bridge, "session"));
            pair.Headers.Add("X-VoiceBridge-Client", "extension");
            using var paired = await client.SendAsync(pair);
            if (!paired.IsSuccessStatusCode) return false;
            using var data = JsonDocument.Parse(await paired.Content.ReadAsStringAsync());
            if (!data.RootElement.TryGetProperty("token", out var value) || value.ValueKind != JsonValueKind.String) return false;
            var token = value.GetString();
            if (string.IsNullOrWhiteSpace(token)) return false;
            using var show = new HttpRequestMessage(HttpMethod.Post, new Uri(bridge, "show-settings"));
            show.Headers.Add("X-VoiceBridge-Client", "launcher");
            show.Headers.Authorization = new("Bearer", token);
            using var result = await client.SendAsync(show);
            return result.IsSuccessStatusCode;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException)
        {
            return false;
        }
    }
}
