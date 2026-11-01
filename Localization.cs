using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace GlassPlayer;

public static class Localization
{
    private static readonly Dictionary<string, string> English = ReadEnglish();
    public static string Language { get; private set; } = "ru";
    public static string T(string russian) => Language == "en" && English.TryGetValue(russian, out var translated) ? translated : russian;
    public static void SetLanguage(string language)
    {
        Language = language == "en" ? "en" : "ru";
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(Language);
        if (Application.Current is null) return;
        foreach (var entry in XamlKeys) Application.Current.Resources[entry.Key] = T(entry.Value);
    }
    private static Dictionary<string, string> ReadEnglish()
    {
        using var stream = typeof(Localization).Assembly.GetManifestResourceStream("GlassPlayer.Localization.en.json") ?? throw new InvalidDataException("Missing English localization");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
    private static readonly Dictionary<string, string> XamlKeys = new()
    {
        ["UiWaiting"] = "Ожидание медиасессии Windows", ["UiPlayMusic"] = "Включите музыку",
        ["UiSources"] = "Источники и настройки", ["UiPlayPause"] = "Воспроизведение / пауза",
        ["UiSettingsTitle"] = "Настройки Glass Player", ["UiAppearance"] = "Внешний вид",
        ["UiCloseSettings"] = "Закрыть настройки", ["UiScale"] = "Масштаб плеера",
        ["UiStyles"] = "С Т И Л И", ["UiSaved"] = "Изменения сохраняются автоматически",
        ["UiReset"] = "Сбросить", ["UiLanguage"] = "Язык интерфейса",
        ["UiSeek"] = "Перемотка", ["UiShuffle"] = "Случайный порядок", ["UiRepeat"] = "Повтор",
        ["UiPrevious"] = "Предыдущий трек", ["UiNext"] = "Следующий трек",
    };
}
