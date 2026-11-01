using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using GlassPlayer;

internal static class DiscordChecks
{
    public static async Task RunAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var song = new PresenceTrack("Test song", "Test artist", "Apple Music", 30, 180, true, now);
        const string soundCloudId = "SoundcloudLtd.SoundCloud-MusicAudio_2xc63xn306dnw!App";
        Check(MediaSource.DisplayName(soundCloudId) == "SoundCloud" && MediaSource.DisplayName("SoundCloud.exe") == "SoundCloud", "SoundCloud package and desktop IDs have a readable platform name");
        Check(MediaSource.DisplayName("UnknownCompany.MusicPackage_abcd123!App") == "Music", "unrecognized package IDs never appear as platform names");
        using (var soundCloud = JsonDocument.Parse(DiscordPresenceService.BuildActivityCommand(song with { Source = soundCloudId, ArtworkUrl = "https://is1-ssl.mzstatic.com/cover.jpg" }, now, "soundcloud")))
        {
            var activity = soundCloud.RootElement.GetProperty("args").GetProperty("activity");
            Check(activity.GetProperty("name").GetString() == "SoundCloud" && activity.GetProperty("state").GetString() == "SoundCloud · Test artist", "Discord header and state normalize raw SoundCloud IDs");
            var assets = activity.GetProperty("assets");
            Check(assets.GetProperty("small_text").GetString() == "SoundCloud" && assets.GetProperty("small_image").GetString()!.Contains("sndcdn.com"), "SoundCloud has its own small platform logo");
        }
        using (var images = JsonDocument.Parse(DiscordPresenceService.BuildActivityCommand(song with { ArtworkUrl = "https://is1-ssl.mzstatic.com/image/cover.jpg" }, now, "images")))
        {
            var activity = images.RootElement.GetProperty("args").GetProperty("activity");
            Check(activity.GetProperty("name").GetString() == "Apple Music", "listening header uses the current platform name");
            var assets = activity.GetProperty("assets");
            Check(assets.GetProperty("large_image").GetString()!.EndsWith("cover.jpg") && assets.GetProperty("small_text").GetString() == "Apple Music", "cover is the large image and platform is the small image");
        }
        using (var catalog = JsonDocument.Parse("""{"results":[{"trackName":"Test song","artistName":"Other artist","artworkUrl100":"https://is1-ssl.mzstatic.com/wrong.jpg"},{"trackName":"Test song","artistName":"Test artist","trackTimeMillis":180000,"collectionName":"Album","artworkUrl100":"https://is1-ssl.mzstatic.com/100x100bb.jpg"}]}"""))
        {
            Check(DiscordArtwork.Select(catalog.RootElement, song) == "https://is1-ssl.mzstatic.com/600x600bb.jpg", "artwork lookup matches title and artist and requests a large cover");
            Check(DiscordArtwork.Select(catalog.RootElement, song with { Title = "Missing", Album = "Album", Artist = "Test artist — Album" }, true) is not null, "album fallback handles Apple Music artist and album metadata");
            Check(DiscordArtwork.Select(catalog.RootElement, song with { Artist = "Unrelated" }) is null, "unrelated artist artwork is excluded");
        }
        using (var payload = JsonDocument.Parse(DiscordPresenceService.BuildActivityCommand(song, now, "test")))
        {
            var activity = payload.RootElement.GetProperty("args").GetProperty("activity");
            Check(activity.GetProperty("type").GetInt32() == 2 && activity.GetProperty("details").GetString() == "Test song" && activity.GetProperty("state").GetString() == "Apple Music · Test artist", "RPC includes listening type, song, music app and artist");
            Check(activity.GetProperty("timestamps").GetProperty("end").GetInt64() - now.ToUnixTimeSeconds() == 150, "RPC timestamps match playback progress");
        }
        using (var cleared = JsonDocument.Parse(DiscordPresenceService.BuildActivityCommand(song with { Playing = false }, now, "pause")))
            Check(cleared.RootElement.GetProperty("args").GetProperty("activity").ValueKind == JsonValueKind.Null, "paused tracks clear Discord presence");
        using (var unknownTime = JsonDocument.Parse(DiscordPresenceService.BuildActivityCommand(song with { Duration = 0, Position = 63_000_000_000 }, now, "unknown-time")))
            Check(!unknownTime.RootElement.GetProperty("args").GetProperty("activity").TryGetProperty("timestamps", out _), "unknown duration does not publish bogus Discord timestamps");
        using (var unknownPosition = JsonDocument.Parse(DiscordPresenceService.BuildActivityCommand(song with { HasTimelinePosition = false }, now, "unknown-position")))
            Check(!unknownPosition.RootElement.GetProperty("args").GetProperty("activity").TryGetProperty("timestamps", out _), "known duration alone does not fabricate Discord playback progress");
        using (var web = JsonDocument.Parse("""{"items":[{"title":"Москва","contentDescriptor":{"kind":"song"},"subtitleLinks":[{"title":"AQYLA"}],"duration":null,"artwork":{"dictionary":{"url":"https://is1-ssl.mzstatic.com/cover/{w}x{h}bb.{f}"}}}]}"""))
        {
            var apple = song with { Title = "Москва", Artist = "AQYLA — Москва - Single", Album = "", Duration = 120 };
            Check(DiscordArtwork.SelectWeb(web.RootElement, apple) == "https://is1-ssl.mzstatic.com/cover/600x600bb.jpg", "Apple web catalog resolves Moscow with combined artist/album metadata and null duration");
            Check(DiscordArtwork.SelectWeb(web.RootElement, apple with { Artist = "Other" }) is null, "web catalog rejects a different performer");
        }
        using (var missing = JsonDocument.Parse(DiscordPresenceService.BuildActivityCommand(song, now, "missing")))
            Check(!missing.RootElement.GetProperty("args").GetProperty("activity").TryGetProperty("assets", out _), "missing artwork does not promote the platform icon into the cover slot");
        using (var limited = JsonDocument.Parse(DiscordPresenceService.BuildActivityCommand(song with { Title = string.Concat(Enumerable.Repeat("🎵", 100)) }, now, "unicode")))
        {
            var title = limited.RootElement.GetProperty("args").GetProperty("activity").GetProperty("details").GetString()!;
            Check(Encoding.UTF8.GetByteCount(title) <= 128 && !title.Contains('\uFFFD'), "RPC text limits preserve Unicode characters");
        }
        Check(DiscordPresenceService.IsValidApplicationId("1566480128328990790") && !DiscordPresenceService.IsValidApplicationId("not-a-token") && !DiscordPresenceService.IsValidApplicationId("123"), "application IDs validated without accepting secrets");
        using (var bytes = new MemoryStream())
        {
            await DiscordPresenceService.WriteFrame(bytes, 1, Encoding.UTF8.GetBytes("hello"), CancellationToken.None);
            using var fragmented = new FragmentedStream(bytes.ToArray());
            var frame = await DiscordPresenceService.ReadFrame(fragmented, CancellationToken.None);
            Check(frame.Opcode == 1 && Encoding.UTF8.GetString(frame.Body) == "hello", "RPC framing handles partial reads");
        }

        string prefix = "glassplayer-test-" + Guid.NewGuid().ToString("N") + "-";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var server = new NamedPipeServerStream(prefix + "0", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var client = new DiscordPresenceService(prefix, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(30));
        client.Update(song);
        client.Configure(true, "123456789012345678");
        await server.WaitForConnectionAsync(timeout.Token);
        var handshake = await DiscordPresenceService.ReadFrame(server, timeout.Token);
        using (var json = JsonDocument.Parse(handshake.Body))
            Check(handshake.Opcode == 0 && json.RootElement.GetProperty("client_id").GetString() == "123456789012345678", "IPC handshake carries application ID");
        await DiscordPresenceService.WriteFrame(server, 1, Encoding.UTF8.GetBytes("{\"cmd\":\"DISPATCH\",\"evt\":\"READY\",\"data\":{}}"), timeout.Token);
        var published = await ReadActivity(server, timeout.Token);
        Check(published.GetProperty("args").GetProperty("activity").GetProperty("state").GetString()!.StartsWith("Apple Music"), "ready connection publishes current source");
        await Acknowledge(server, published, timeout.Token);
        await DiscordPresenceService.WriteFrame(server, 3, Encoding.UTF8.GetBytes("ping"), timeout.Token);
        var pong = await DiscordPresenceService.ReadFrame(server, timeout.Token);
        Check(pong.Opcode == 4 && Encoding.UTF8.GetString(pong.Body) == "ping", "IPC ping receives matching pong");
        client.Update(song with { Playing = false });
        var pause = await ReadActivity(server, timeout.Token);
        Check(pause.GetProperty("args").GetProperty("activity").ValueKind == JsonValueKind.Null, "pause clears activity over IPC");
        await Acknowledge(server, pause, timeout.Token);
        client.Update(song with { Source = "Spotify", Title = "Next song" });
        var switched = await ReadActivity(server, timeout.Token);
        Check(switched.GetProperty("args").GetProperty("activity").GetProperty("state").GetString()!.StartsWith("Spotify"), "switching music apps updates presence");
        await Acknowledge(server, switched, timeout.Token);
        client.Configure(false, "123456789012345678");
        var disabled = await ReadActivity(server, timeout.Token);
        Check(disabled.GetProperty("args").GetProperty("activity").ValueKind == JsonValueKind.Null, "disabling integration clears published activity");
        server.Disconnect();
        var reconnected = server.WaitForConnectionAsync(timeout.Token);
        client.Configure(true, "123456789012345678");
        await reconnected;
        await DiscordPresenceService.ReadFrame(server, timeout.Token);
        await DiscordPresenceService.WriteFrame(server, 1, Encoding.UTF8.GetBytes("{\"evt\":\"READY\",\"data\":{}}"), timeout.Token);
        var resumed = await ReadActivity(server, timeout.Token);
        Check(resumed.GetProperty("args").GetProperty("activity").ValueKind == JsonValueKind.Object, "integration reconnects after being re-enabled");
        await Acknowledge(server, resumed, timeout.Token);
        server.Disconnect();
        await server.WaitForConnectionAsync(timeout.Token);
        await DiscordPresenceService.ReadFrame(server, timeout.Token);
        await DiscordPresenceService.WriteFrame(server, 1, Encoding.UTF8.GetBytes("{\"evt\":\"READY\",\"data\":{}}"), timeout.Token);
        var restarted = await ReadActivity(server, timeout.Token);
        Check(restarted.GetProperty("args").GetProperty("activity").ValueKind == JsonValueKind.Object, "Discord restart triggers reconnection and republishes latest track");
        await Acknowledge(server, restarted, timeout.Token);
        var stopping = client.DisposeAsync().AsTask();
        var shutdownClear = await ReadActivity(server, timeout.Token);
        await stopping;
        Check(shutdownClear.GetProperty("args").GetProperty("activity").ValueKind == JsonValueKind.Null, "closing the player clears activity before disconnecting");
        Console.WriteLine("All 29 Discord checks passed.");
    }
    private static async Task<JsonElement> ReadActivity(Stream stream, CancellationToken token)
    {
        var frame = await DiscordPresenceService.ReadFrame(stream, token);
        using var json = JsonDocument.Parse(frame.Body);
        return json.RootElement.Clone();
    }
    private static Task Acknowledge(Stream stream, JsonElement activity, CancellationToken token) => DiscordPresenceService.WriteFrame(stream, 1, JsonSerializer.SerializeToUtf8Bytes(new { cmd = "SET_ACTIVITY", nonce = activity.GetProperty("nonce").GetString(), data = new { } }), token);
    private static void Check(bool result, string message) { if (!result) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => base.ReadAsync(buffer[..Math.Min(2, buffer.Length)], token);
    }
}
