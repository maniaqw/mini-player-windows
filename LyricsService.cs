using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GlassPlayer;

public sealed record LyricLine(double Seconds, string Text);
public sealed record LyricsResult(IReadOnlyList<LyricLine> Lines, string Status);

public sealed class LyricsService : IDisposable
{
    private readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTimeOffset nextRequest;
    private readonly string cacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassPlayer", "lyrics");

    public LyricsService() => client.DefaultRequestHeaders.UserAgent.ParseAdd("GlassPlayer/2.0 (local Windows desktop application)");

    public async Task<LyricsResult> Find(string title, string artist, string album, double duration, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist)) return new([], "Нет сведений для поиска текста");
        var signature = $"{title}\n{artist}\n{album}\n{Math.Round(duration)}";
        var path = Path.Combine(cacheFolder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature))) + ".json");
        try
        {
            var cached = JsonSerializer.Deserialize<Cache>(await File.ReadAllTextAsync(path, token));
            if (cached is not null && DateTimeOffset.UtcNow - cached.Saved < TimeSpan.FromDays(cached.Lrc is null ? 1 : 30))
                return Result(cached.Lrc);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }

        await gate.WaitAsync(token);
        try
        {
            string query = $"track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
            if (!string.IsNullOrWhiteSpace(album)) query += "&album_name=" + Uri.EscapeDataString(album);
            if (duration is >= 1 and <= 3600) query += "&duration=" + Math.Round(duration).ToString(CultureInfo.InvariantCulture);
            var exact = await Get("/api/get?" + query, token);
            string? lrc = exact is { ValueKind: JsonValueKind.Object } exactRecord ? Synced(exactRecord) : null;
            if (lrc is null)
            {
                var cleanTitle = Regex.Replace(title, @"\s*[\(\[]\s*(feat\.?|ft\.?).*?[\)\]]", "", RegexOptions.IgnoreCase).Trim();
                var search = await Get($"/api/search?track_name={Uri.EscapeDataString(cleanTitle)}&artist_name={Uri.EscapeDataString(artist)}", token);
                if (search is { ValueKind: JsonValueKind.Array } records)
                {
                    var candidates = records.EnumerateArray().Where(r =>
                        Normalize(r.GetProperty("trackName").GetString() ?? "") == Normalize(title)
                        && Normalize(r.GetProperty("artistName").GetString() ?? "").Contains(Normalize(artist), StringComparison.Ordinal)
                        && (duration <= 0 || Math.Abs(r.GetProperty("duration").GetDouble() - duration) <= 3)
                        && Synced(r) is not null);
                    var best = candidates.OrderBy(r => Math.Abs(r.GetProperty("duration").GetDouble() - duration)).FirstOrDefault();
                    if (best.ValueKind == JsonValueKind.Object) lrc = Synced(best);
                }
            }
            try
            {
                Directory.CreateDirectory(cacheFolder);
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new Cache(DateTimeOffset.UtcNow, lrc)), token);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return Result(lrc);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return new([], "Сервис текста не ответил"); }
        catch (HttpRequestException) { return new([], "Сервис текста недоступен"); }
        catch (JsonException) { return new([], "Не удалось прочитать текст"); }
        finally { gate.Release(); }
    }

    private async Task<JsonElement?> Get(string path, CancellationToken token)
    {
        var delay = nextRequest - DateTimeOffset.UtcNow;
        if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
        using var response = await client.GetAsync("https://lrclib.net" + path, token);
        nextRequest = DateTimeOffset.UtcNow.AddMilliseconds(400);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(60);
            nextRequest = DateTimeOffset.UtcNow + retry + TimeSpan.FromMilliseconds(Random.Shared.Next(200, 1500));
            throw new HttpRequestException("Lyrics rate limit");
        }
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(token);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string? Synced(JsonElement record) => record.TryGetProperty("syncedLyrics", out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;
    private static LyricsResult Result(string? lrc)
    {
        var lines = lrc is null ? [] : Parse(lrc);
        return new(lines, lines.Count > 0 ? "Синхронизированный текст · LRCLIB" : "Синхронизированный текст не найден");
    }

    public static IReadOnlyList<LyricLine> Parse(string lrc)
    {
        var result = new List<LyricLine>();
        double offset = 0;
        var offsetMatches = Regex.Matches(lrc, @"\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase);
        if (offsetMatches.Count > 0 && double.TryParse(offsetMatches[^1].Groups[1].Value, CultureInfo.InvariantCulture, out var milliseconds)) offset = milliseconds / 1000;
        foreach (var line in lrc.Split('\n'))
        {
            var matches = Regex.Matches(line, @"\[(\d{1,3}):(\d{2}(?:[\.:]\d{1,3})?)\]");
            if (matches.Count == 0) continue;
            var text = line[(matches[^1].Index + matches[^1].Length)..].Trim();
            foreach (Match match in matches)
            {
                var secondsText = match.Groups[2].Value.Replace(':', '.');
                if (double.TryParse(secondsText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds) && seconds < 60)
                    result.Add(new(Math.Max(0, int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 60 + seconds - offset), text));
            }
        }
        return result.OrderBy(x => x.Seconds).ToArray();
    }

    private static string Normalize(string text)
    {
        text = Regex.Replace(text, @"\s*[\(\[]\s*(feat\.?|ft\.?).*?[\)\]]", "", RegexOptions.IgnoreCase);
        return string.Concat(text.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))).ToLowerInvariant();
    }
    public void Dispose() => client.Dispose();
    private sealed record Cache(DateTimeOffset Saved, string? Lrc);
}
