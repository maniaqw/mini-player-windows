using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace GlassPlayer;

public sealed record PresenceTrack(string Title, string Artist, string Source, double Position, double Duration, bool Playing, DateTimeOffset ObservedAt, string Album = "", string? ArtworkUrl = null, bool HasTimelinePosition = true);

public sealed class DiscordPresenceService : IAsyncDisposable
{
    public const string DefaultApplicationId = "1566480128328990790";
    private sealed record Configuration(bool Enabled, string ApplicationId);
    private Configuration configuration = new(false, "");
    private PresenceTrack? track;
    private readonly DiscordArtwork artwork = new();
    private readonly CancellationTokenSource shutdown = new();
    private readonly SemaphoreSlim changed = new(0, 1);
    private readonly string pipePrefix;
    private readonly TimeSpan retryDelay, updateInterval;
    private Task? worker;
    private volatile string status = "Discord Rich Presence выключен";
    public string Status => status;
    public string Diagnostic { get; private set; } = "";
    public event EventHandler? StatusChanged;

    public DiscordPresenceService(string pipePrefix = "discord-ipc-", TimeSpan? retryDelay = null, TimeSpan? updateInterval = null)
    {
        this.pipePrefix = pipePrefix;
        this.retryDelay = retryDelay ?? TimeSpan.FromSeconds(8);
        this.updateInterval = updateInterval ?? TimeSpan.FromSeconds(5);
    }
    public static bool IsValidApplicationId(string id) => id.Length is >= 16 and <= 20 && id.All(char.IsAsciiDigit) && ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0;
    public void Configure(bool enabled, string applicationId)
    {
        var next = new Configuration(enabled, applicationId.Trim());
        if (next == Volatile.Read(ref configuration) && worker is not null) return;
        Volatile.Write(ref configuration, next);
        worker ??= Task.Run(Run);
        Signal();
    }
    public void Update(PresenceTrack? next) { Volatile.Write(ref track, next); }
    private void Signal() { try { changed.Release(); } catch (SemaphoreFullException) { } }
    private void SetStatus(string value)
    {
        if (status == value) return;
        status = value;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
    private async Task Run()
    {
        while (!shutdown.IsCancellationRequested)
        {
            try
            {
                var config = Volatile.Read(ref configuration);
                if (!config.Enabled || !IsValidApplicationId(config.ApplicationId))
                {
                    SetStatus(config.Enabled ? "Укажите Discord Application ID" : "Discord Rich Presence выключен");
                    await changed.WaitAsync(shutdown.Token);
                    continue;
                }
                SetStatus("Подключение к Discord…");
                using var pipe = await Connect(shutdown.Token);
                if (pipe is null) SetStatus("Ожидание запуска Discord");
                else await Session(pipe, config, shutdown.Token);
                if (Volatile.Read(ref configuration) != config) continue;
                await changed.WaitAsync(retryDelay, shutdown.Token);
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or JsonException or TimeoutException or UnauthorizedAccessException or OperationCanceledException)
            {
                Debug.WriteLine("Discord RPC: " + ex.Message);
                Diagnostic = ex.Message;
                SetStatus(ex is RpcException ? "Discord отклонил подключение или статус" : "Соединение с Discord потеряно");
                try { await changed.WaitAsync(retryDelay, shutdown.Token); } catch (OperationCanceledException) { break; }
            }
        }
    }
    private async Task<NamedPipeClientStream?> Connect(CancellationToken token)
    {
        for (int i = 0; i < 10; i++)
        {
            var pipe = new NamedPipeClientStream(".", pipePrefix + i, PipeDirection.InOut, PipeOptions.Asynchronous);
            try { await pipe.ConnectAsync(120, token); return pipe; }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException) { pipe.Dispose(); }
            catch { pipe.Dispose(); throw; }
        }
        return null;
    }
    private async Task Session(NamedPipeClientStream pipe, Configuration config, CancellationToken token)
    {
        Task<RpcFrame>? read = null;
        bool published = false;
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            await WriteFrame(pipe, 0, JsonSerializer.SerializeToUtf8Bytes(new { v = 1, client_id = config.ApplicationId }), token);
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                while (true)
                {
                    var frame = await ReadFrame(pipe, timeout.Token);
                    if (frame.Opcode == 3) { await WriteFrame(pipe, 4, frame.Body, timeout.Token); continue; }
                    if (frame.Opcode != 1) throw new RpcException(DescribeError(frame.Body));
                    using var ready = JsonDocument.Parse(frame.Body);
                    if (Event(ready.RootElement) == "READY") break;
                    if (Event(ready.RootElement) == "ERROR") throw new RpcException(DescribeError(frame.Body));
                }
            }
            SetStatus("Discord подключён");
            Diagnostic = "";
            read = ReadFrame(pipe, connection.Token);
            string? sentKey = null;
            long sentStart = 0;
            var lastSend = DateTimeOffset.MinValue;
            string? pendingNonce = null;
            var pendingAt = DateTimeOffset.MinValue;
            bool sent = false;
            while (!token.IsCancellationRequested && Volatile.Read(ref configuration) == config)
            {
                if (read.IsCompleted)
                {
                    var response = await read;
                    if (response.Opcode == 3) await WriteFrame(pipe, 4, response.Body, token);
                    else if (response.Opcode == 2) throw new RpcException(DescribeError(response.Body));
                    else if (response.Opcode == 1)
                    {
                        using var document = JsonDocument.Parse(response.Body);
                        if (Event(document.RootElement) == "ERROR") throw new RpcException(DescribeError(response.Body));
                        if (document.RootElement.TryGetProperty("nonce", out var nonce) && nonce.GetString() == pendingNonce) pendingNonce = null;
                    }
                    read = ReadFrame(pipe, connection.Token);
                }
                if (pendingNonce is not null && DateTimeOffset.UtcNow - pendingAt > TimeSpan.FromSeconds(10)) throw new TimeoutException("Discord did not acknowledge activity");
                var latest = Volatile.Read(ref track);
                bool active = latest is { Playing: true } && !string.IsNullOrWhiteSpace(latest.Title);
                if (active && latest!.ArtworkUrl is null && pipePrefix == "discord-ipc-")
                {
                    var cover = artwork.Resolve(latest);
                    if (cover.IsCompletedSuccessfully) latest = latest with { ArtworkUrl = cover.Result };
                }
                var key = active ? $"{latest!.Title}\n{latest.Artist}\n{latest.Source}\n{latest.Duration:0}\n{latest.ArtworkUrl}\n{latest.HasTimelinePosition}" : "";
                var now = DateTimeOffset.UtcNow;
                long start = active ? now.ToUnixTimeSeconds() - (long)CurrentPosition(latest!, now) : 0;
                bool needsUpdate = !sent || key != sentKey || active && Math.Abs(start - sentStart) > 3;
                if (pendingNonce is null && needsUpdate && (!sent || !active || now - lastSend >= updateInterval))
                {
                    pendingNonce = Guid.NewGuid().ToString("N");
                    await WriteFrame(pipe, 1, BuildActivityCommand(active ? latest : null, now, pendingNonce), token);
                    published = active;
                    sent = true; sentKey = key; sentStart = start; lastSend = pendingAt = now;
                }
                SetStatus(pendingNonce is not null ? "Обновление статуса Discord…" : active ? "Музыка отображается в Discord" : latest is null ? "Discord подключён · ожидание музыки" : "Музыка на паузе · статус скрыт");
                await Task.Delay(100, token);
            }
        }
        finally
        {
            if (published && pipe.IsConnected)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
                    await WriteFrame(pipe, 1, BuildActivityCommand(null, DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N")), timeout.Token);
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
            }
            connection.Cancel();
            if (read is not null) { try { await read; } catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { } }
        }
    }
    private static string? Event(JsonElement value) => value.TryGetProperty("evt", out var evt) && evt.ValueKind == JsonValueKind.String ? evt.GetString() : null;
    private static string DescribeError(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var data = document.RootElement.TryGetProperty("data", out var nested) ? nested : document.RootElement;
            if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("message", out var message)) return message.GetString() ?? "Discord rejected the request";
        }
        catch (JsonException) { }
        return "Discord rejected the request";
    }
    private static double CurrentPosition(PresenceTrack track, DateTimeOffset now) => MediaTimeline.ClampPosition(track.Position + Math.Max(0, (now - track.ObservedAt).TotalSeconds), track.Duration);
    public static byte[] BuildActivityCommand(PresenceTrack? track, DateTimeOffset now, string nonce)
    {
        object? activity = null;
        if (track is { Playing: true } && !string.IsNullOrWhiteSpace(track.Title))
        {
            track = track with { Source = MediaSource.DisplayName(track.Source) };
            long start = now.ToUnixTimeSeconds() - (long)CurrentPosition(track, now);
            var payload = new Dictionary<string, object> { ["type"] = 2, ["name"] = Limit(track.Source), ["status_display_type"] = 0, ["details"] = Limit(track.Title), ["state"] = Limit(string.IsNullOrWhiteSpace(track.Artist) ? track.Source : track.Source + " · " + track.Artist), ["instance"] = false };
            if (track.HasTimelinePosition && MediaTimeline.ValidDuration(track.Duration)) payload["timestamps"] = new { start, end = start + (long)track.Duration };
            var assets = new Dictionary<string, string>();
            if (track.ArtworkUrl is { } cover && Uri.TryCreate(cover, UriKind.Absolute, out var uri) && uri.Scheme == "https")
            {
                assets["large_image"] = cover;
                assets["large_text"] = Limit(track.Title + " · " + track.Artist);
            }
            if (assets.ContainsKey("large_image") && DiscordArtwork.PlatformIcon(track.Source) is { } icon)
            {
                assets["small_image"] = icon;
                assets["small_text"] = Limit(track.Source);
            }
            if (assets.Count > 0) payload["assets"] = assets;
            activity = payload;
        }
        return JsonSerializer.SerializeToUtf8Bytes(new { cmd = "SET_ACTIVITY", args = new { pid = Environment.ProcessId, activity }, nonce });
    }
    private static string Limit(string value)
    {
        var builder = new StringBuilder();
        int bytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsControl(rune)) continue;
            if (bytes + rune.Utf8SequenceLength > 128) break;
            builder.Append(rune.ToString()); bytes += rune.Utf8SequenceLength;
        }
        return builder.ToString().Trim();
    }
    public sealed record RpcFrame(int Opcode, byte[] Body);
    public static async Task WriteFrame(Stream stream, int opcode, byte[] body, CancellationToken token)
    {
        var frame = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, opcode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), body.Length);
        body.CopyTo(frame, 8);
        await stream.WriteAsync(frame, token);
        await stream.FlushAsync(token);
    }
    public static async Task<RpcFrame> ReadFrame(Stream stream, CancellationToken token)
    {
        var header = new byte[8];
        await stream.ReadExactlyAsync(header, token);
        int opcode = BinaryPrimitives.ReadInt32LittleEndian(header), length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (length is < 0 or > 1_048_576 || opcode is < 0 or > 4) throw new IOException("Invalid Discord RPC frame");
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, token);
        return new(opcode, body);
    }
    public async ValueTask DisposeAsync()
    {
        shutdown.Cancel();
        Signal();
        if (worker is not null) await worker.ConfigureAwait(false);
    }
    private sealed class RpcException(string message) : IOException(message);
}
