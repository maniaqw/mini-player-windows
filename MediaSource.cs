using System.IO;

namespace GlassPlayer;

public static class MediaSource
{
    public static string DisplayName(string id)
    {
        (string Match, string Name)[] platforms =
        [
            ("soundcloud", "SoundCloud"), ("spotify", "Spotify"),
            ("apple", "Apple Music"), ("youtubemusic", "YouTube Music"),
            ("youtube music", "YouTube Music"), ("deezer", "Deezer"),
            ("tidal", "TIDAL"), ("yandex", "Yandex Music"),
            ("amazonmusic", "Amazon Music"), ("amazon music", "Amazon Music"),
            ("zune", "Media Player"), ("wmplayer", "Windows Media Player"),
            ("vlc", "VLC"), ("foobar", "foobar2000"),
            ("chrome", "Google Chrome"), ("msedge", "Microsoft Edge"),
            ("firefox", "Mozilla Firefox")
        ];
        foreach (var platform in platforms)
            if (id.Contains(platform.Match, StringComparison.OrdinalIgnoreCase)) return platform.Name;
        if (id.Contains('!') || id.Contains('_') || id.Length > 48) return "Music";
        string name = id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(id) : id;
        return string.IsNullOrWhiteSpace(name) ? "Music" : name;
    }
}
