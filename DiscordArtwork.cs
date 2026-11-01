using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GlassPlayer;

public sealed class DiscordArtwork
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly Dictionary<string, (DateTimeOffset Created, Task<string?> Result)> cache = new();
    public Task<string?> Resolve(PresenceTrack track)
    {
        if (string.IsNullOrWhiteSpace(track.Album))
        {
            var parts = track.Artist.Split(new[] { " — ", " – " }, 2, StringSplitOptions.None);
            if (parts.Length == 2) track = track with { Album = parts[1].Trim() };
        }
        string key = track.Title + "\n" + track.Artist + "\n" + track.Album;
        if (!cache.TryGetValue(key, out var entry) || entry.Result.IsCompletedSuccessfully && entry.Result.Result is null && DateTimeOffset.UtcNow - entry.Created > TimeSpan.FromMinutes(1))
        {
            if (cache.Count > 256) cache.Clear();
            cache[key] = entry = (DateTimeOffset.UtcNow, Lookup(track));
        }
        return entry.Result;
    }
    private static async Task<string?> Lookup(PresenceTrack track)
    {
        try
        {
            string artist = track.Artist.Split(new[] { " — ", " – " }, StringSplitOptions.None)[0];
            string url = "https://itunes.apple.com/search?entity=song&limit=25&term=" + Uri.EscapeDataString(track.Title + " " + artist);
            string? found = null;
            try
            {
                using var response = await Http.GetAsync(url).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                found = Select(json.RootElement, track);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { }
            if (found is null)
            {
                try { found = await LookupWeb(track, artist).ConfigureAwait(false); }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or RegexMatchTimeoutException) { }
            }
            if (found is not null || string.IsNullOrWhiteSpace(track.Album)) return found;
            await Task.Delay(3100).ConfigureAwait(false);
            using var albumResponse = await Http.GetAsync("https://itunes.apple.com/search?entity=song&limit=25&term=" + Uri.EscapeDataString(track.Album + " " + artist)).ConfigureAwait(false);
            albumResponse.EnsureSuccessStatusCode();
            using var albumJson = JsonDocument.Parse(await albumResponse.Content.ReadAsStringAsync().ConfigureAwait(false));
            return Select(albumJson.RootElement, track, true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or RegexMatchTimeoutException) { return null; }
    }
    private static async Task<string?> LookupWeb(PresenceTrack track, string artist)
    {
        string html = await Http.GetStringAsync("https://music.apple.com/us/search?term=" + Uri.EscapeDataString(artist + " " + track.Title)).ConfigureAwait(false);
        var script = Regex.Match(html, "<script[^>]*id=\"serialized-server-data\"[^>]*>(.*?)</script>", RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        if (!script.Success) return null;
        using var data = JsonDocument.Parse(script.Groups[1].Value);
        return SelectWeb(data.RootElement, track);
    }
    public static string? SelectWeb(JsonElement root, PresenceTrack track)
    {
        static string Normalize(string value) => string.Concat(value.ToLowerInvariant().Where(char.IsLetterOrDigit));
        string artist = track.Artist.Split(new[] { " — ", " – " }, StringSplitOptions.None)[0];
        if (string.IsNullOrWhiteSpace(artist)) return null;
        foreach (var item in Objects(root))
        {
            if (!item.TryGetProperty("title", out var title) || title.ValueKind != JsonValueKind.String || Normalize(title.GetString()!) != Normalize(track.Title)) continue;
            if (!item.TryGetProperty("contentDescriptor", out var descriptor) || !descriptor.TryGetProperty("kind", out var kind) || kind.GetString() != "song") continue;
            if (!item.TryGetProperty("subtitleLinks", out var links) || links.ValueKind != JsonValueKind.Array) continue;
            string credits = string.Join(" ", links.EnumerateArray().Select(link => link.TryGetProperty("title", out var name) ? name.GetString() : ""));
            if (Normalize(credits) != Normalize(artist)) continue;
            if (item.TryGetProperty("duration", out var duration) && duration.ValueKind == JsonValueKind.Number && track.Duration > 0 && Math.Abs(duration.GetDouble() - track.Duration) > 5) continue;
            if (!item.TryGetProperty("artwork", out var art) || !art.TryGetProperty("dictionary", out var dictionary) || !dictionary.TryGetProperty("url", out var url)) continue;
            string image = (url.GetString() ?? "").Replace("{w}", "600").Replace("{h}", "600").Replace("{f}", "jpg");
            if (Uri.TryCreate(image, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host.EndsWith(".mzstatic.com", StringComparison.OrdinalIgnoreCase)) return image;
        }
        return null;
    }
    private static IEnumerable<JsonElement> Objects(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
            foreach (var property in element.EnumerateObject())
                foreach (var child in Objects(property.Value)) yield return child;
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
                foreach (var child in Objects(item)) yield return child;
    }
    public static string? Select(JsonElement root, PresenceTrack track, bool albumOnly = false)
    {
        static string Normalize(string value) => string.Concat(value.ToLowerInvariant().Where(char.IsLetterOrDigit));
        static string Read(JsonElement item, string name) => item.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
        string artist = track.Artist.Split(new[] { " — ", " – " }, StringSplitOptions.None)[0];
        if (string.IsNullOrWhiteSpace(artist)) return null;
        if (!root.TryGetProperty("results", out var results)) return null;
        var matches = results.EnumerateArray().Where(item => (albumOnly ? !string.IsNullOrWhiteSpace(track.Album) && Normalize(Read(item, "collectionName")) == Normalize(track.Album) : Normalize(Read(item, "trackName")) == Normalize(track.Title))
            && Normalize(Read(item, "artistName")) == Normalize(artist)
            && (albumOnly || !item.TryGetProperty("trackTimeMillis", out var duration) || track.Duration <= 0 || Math.Abs(duration.GetDouble() / 1000 - track.Duration) < 5))
            .OrderByDescending(item => Normalize(Read(item, "collectionName")) == Normalize(track.Album));
        foreach (var item in matches)
        {
            string image = Read(item, "artworkUrl100");
            if (Uri.TryCreate(image, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host.EndsWith(".mzstatic.com", StringComparison.OrdinalIgnoreCase))
                return image.Replace("100x100bb", "600x600bb", StringComparison.Ordinal);
        }
        return null;
    }
    public static string? PlatformIcon(string source) => source switch
    {
        "Apple Music" => "https://music.apple.com/assets/favicon/favicon-180.png",
        "Spotify" => "https://open.spotifycdn.com/cdn/images/favicon32.b64ecc03.png",
        "SoundCloud" => "https://a-v2.sndcdn.com/assets/images/sc-icons/ios-a62dfc8fe7.png",
        _ => null
    };
}
