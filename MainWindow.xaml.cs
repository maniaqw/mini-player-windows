using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Media;
using Windows.Storage.Streams;
using MediaSession = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

namespace GlassPlayer;

public partial class MainWindow : Window
{
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private MediaSession? session;
    private string? pinnedSource;
    private string trackKey = "";
    private string fallbackArtist = "Ожидание медиасессии Windows";
    private string lyricsStatus = "Ожидание трека";
    private bool updating, closing, playing;
    private bool previousEnabled, nextEnabled;
    private double duration, positionBase, positionClock, rate = 1, lyricOffsetSeconds;
    private double discVelocity, coverChangedAt = -1, targetWidth;
    private double expansion, timelineStartSeconds;
    private bool hasTimelinePosition;
    private readonly MediaTimeline mediaTimeline = new();
    private bool expandUp, seekDragging;
    private string seekTrackKey = "";
    private readonly ArcSegment ringArc = new() { Size = new System.Windows.Size(25.5, 25.5), SweepDirection = SweepDirection.Clockwise, IsStroked = true };
    private readonly PathGeometry ringGeometry = new();
    private readonly EllipseGeometry fullRing = new(new Point(28.5, 28.5), 25.5, 25.5);
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly RenderFrameClock renderClock = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly LyricsService lyricsService = new();
    private CancellationTokenSource? lyricsCancellation;
    private IReadOnlyList<LyricLine> lyrics = [];
    private readonly System.Windows.Forms.NotifyIcon tray;
    private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassPlayer", "settings.json");
    private readonly bool demo = Environment.GetCommandLineArgs().Contains("--demo");
    private SettingsWindow? settingsWindow;
    private LiquidBackdrop? liquidBackdrop;
    public PlayerDesign CurrentDesign { get; private set; } = PlayerDesign.Classic;
    public bool IsLiquidGlass => CurrentDesign == PlayerDesign.LiquidGlass;
    private readonly DiscordPresenceService discord = new();
    public bool DiscordEnabled { get; private set; } = true;
    public string DiscordApplicationId { get; private set; } = DiscordPresenceService.DefaultApplicationId;
    public string DiscordStatus => discord.Status;
    public string DiscordDiagnostic => discord.Diagnostic;
    public PresenceTrack? CurrentDiscordTrack { get; private set; }
    public PlayerTheme CurrentTheme { get; private set; } = PlayerTheme.Find("Lagoon");
    public double PlayerScale { get; private set; } = 1;

    public MainWindow() : this(false) { }

    public MainWindow(bool previewOnly)
    {
        demo = demo || previewOnly;
        InitializeComponent();
        var ringFigure = new PathFigure { StartPoint = new Point(28.5, 3), IsClosed = false, IsFilled = false };
        ringFigure.Segments.Add(ringArc);
        ringGeometry.Figures.Add(ringFigure);
        fullRing.Freeze();
        Left = SystemParameters.WorkArea.Right - Width - 30;
        Top = SystemParameters.WorkArea.Bottom - Height - 30;
        try
        {
            var saved = JsonSerializer.Deserialize<Position>(File.ReadAllText(settingsPath));
            if (saved is not null)
            {
                Left = saved.Left; Top = saved.Top; lyricOffsetSeconds = saved.LyricOffset;
                CurrentTheme = PlayerTheme.Find(saved.Theme);
                CurrentDesign = PlayerDesigns.Parse(saved.Design);
                PlayerScale = double.IsFinite(saved.Scale) ? Math.Clamp(saved.Scale, 0.6, 1.5) : 1;
                Localization.SetLanguage(saved.Language);
                DiscordEnabled = saved.DiscordEnabled;
                DiscordApplicationId = saved.DiscordApplicationId ?? "";
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        var requestedLanguage = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--language="))?.Split('=')[1];
        Localization.SetLanguage(requestedLanguage ?? Localization.Language);
        fallbackArtist = Localization.T("Ожидание медиасессии Windows");
        discord.StatusChanged += (_, _) =>
        {
            if (!closing && !Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() => settingsWindow?.RefreshDiscordStatus());
        };
        if (!demo) discord.Configure(DiscordEnabled, DiscordApplicationId);
        if (Environment.GetCommandLineArgs().Contains("--liquid-glass")) CurrentDesign = PlayerDesign.LiquidGlass;
        Width *= PlayerScale;
        targetWidth = Width;
        Height = 144 * PlayerScale;
        ApplyTheme();
        ClampPosition();
        SizeChanged += (_, _) => UpdateCanvas();
        SourceInitialized += (_, _) => { liquidBackdrop = new LiquidBackdrop(this); Dispatcher.BeginInvoke(() => liquidBackdrop.Update()); };
        LocationChanged += (_, _) => liquidBackdrop?.RequestUpdate();
        IsVisibleChanged += (_, _) => { renderClock.Reset(); liquidBackdrop?.Update(); };
        StateChanged += (_, _) => liquidBackdrop?.Update();
        Activated += (_, _) => liquidBackdrop?.Update(true);
        UpdateCanvas();
        timer.Tick += async (_, _) => await Refresh();
        CompositionTarget.Rendering += RenderFrame;
        using var iconResource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/GlassPlayer;component/Assets/GlassPlayer.ico")).Stream;
        using var appIcon = new System.Drawing.Icon(iconResource);
        tray = new System.Windows.Forms.NotifyIcon { Icon = (System.Drawing.Icon)appIcon.Clone(), Text = "Glass Player", Visible = true };
        tray.MouseUp += (_, e) =>
        {
            if (e.Button != System.Windows.Forms.MouseButtons.Right) return;
            Dispatcher.Invoke(() =>
            {
                var menu = CreateThemedMenu();
                var show = new MenuItem { Header = Localization.T("Показать плеер"), Icon = "↗" };
                show.Click += (_, _) => { Show(); Activate(); };
                var settings = new MenuItem { Header = Localization.T("Настройки"), Icon = "⚙" };
                settings.Click += (_, _) => OpenSettings();
                var exit = new MenuItem { Header = Localization.T("Выход"), Icon = "×" };
                exit.Click += (_, _) => Close();
                menu.Items.Add(show); menu.Items.Add(settings); menu.Items.Add(MenuSeparator()); menu.Items.Add(exit);
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                menu.IsOpen = true;
            });
        };
        tray.DoubleClick += (_, _) => { Show(); Activate(); };
        Loaded += async (_, _) =>
        {
            if (demo) { StartDemo(); return; }
            await Connect();
        };
        Closing += (_, _) =>
        {
            closing = true;
            liquidBackdrop?.Dispose();
            liquidBackdrop = null;
            timer.Stop();
            CompositionTarget.Rendering -= RenderFrame;
            lyricsCancellation?.Cancel();
            var trayIcon = tray.Icon;
            tray.Dispose();
            trayIcon?.Dispose();
            lyricsService.Dispose();
            try { discord.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2)); }
            catch (AggregateException ex) { Debug.WriteLine(ex); }
            SaveSettings();
        };
        if (requestedLanguage is not null) SaveSettings();
    }

    private async Task Connect()
    {
        try
        {
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            await Refresh();
            timer.Start();
        }
        catch (Exception ex)
        {
            TrackTitle.Text = Localization.T("Нет доступа к медиасессиям");
            ShowLine(Localization.T("Нажмите ⋯, чтобы повторить"));
            ToolTip = ex.Message;
        }
    }

    private async Task Refresh()
    {
        if (updating || closing || manager is null) return;
        updating = true;
        try
        {
            var sessions = manager.GetSessions();
            var selected = pinnedSource is null ? null : sessions.FirstOrDefault(s => s.SourceAppUserModelId == pinnedSource);
            selected ??= sessions.FirstOrDefault(s => s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                ?? manager.GetCurrentSession() ?? sessions.FirstOrDefault();
            session = selected;
            if (selected is null)
            {
                CurrentDiscordTrack = null;
                if (!demo) discord.Update(null);
                settingsWindow?.RefreshDiscordStatus();
                playing = false;
                duration = positionBase = 0;
                hasTimelinePosition = false;
                mediaTimeline.Reset();
                Play.IsEnabled = previousEnabled = nextEnabled = false;
                SeekSlider.IsEnabled = PreviousButton.IsEnabled = NextButton.IsEnabled = ShuffleButton.IsEnabled = RepeatButton.IsEnabled = ExpandedPlayButton.IsEnabled = false;
                if (trackKey != "" || TrackTitle.Text != Localization.T("Включите музыку"))
                {
                    trackKey = "";
                    lyricsCancellation?.Cancel();
                    lyrics = [];
                    lyricsStatus = "Ожидание трека";
                    TrackTitle.Text = Localization.T("Включите музыку");
                    ShowLine(Localization.T("Ожидание медиасессии Windows"));
                    SetCover(null);
                }
                return;
            }
            var info = selected.GetPlaybackInfo();
            Play.IsEnabled = info.Controls.IsPlayPauseToggleEnabled;
            previousEnabled = info.Controls.IsPreviousEnabled;
            nextEnabled = info.Controls.IsNextEnabled;
            PreviousButton.IsEnabled = previousEnabled;
            NextButton.IsEnabled = nextEnabled;
            ExpandedPlayButton.IsEnabled = Play.IsEnabled;
            SeekSlider.IsEnabled = info.Controls.IsPlaybackPositionEnabled;
            ShuffleButton.IsEnabled = info.Controls.IsShuffleEnabled;
            RepeatButton.IsEnabled = info.Controls.IsRepeatEnabled;
            ShuffleButton.Foreground = info.IsShuffleActive == true ? CurrentTheme.Accent : CurrentTheme.Secondary;
            RepeatButton.Foreground = info.AutoRepeatMode is not null and not MediaPlaybackAutoRepeatMode.None ? CurrentTheme.Accent : CurrentTheme.Secondary;
            RepeatButton.Content = info.AutoRepeatMode == MediaPlaybackAutoRepeatMode.Track ? "↻₁" : "↻";
            playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            rate = MediaTimeline.PlaybackRate(info.PlaybackRate);
            var properties = await selected.TryGetMediaPropertiesAsync();
            if (closing) return;
            var key = string.Join('\n', selected.SourceAppUserModelId, properties.Title, properties.Artist, properties.AlbumTitle);
            var timeline = selected.GetTimelineProperties();
            positionClock = clock.Elapsed.TotalSeconds;
            mediaTimeline.Update(key, timeline.StartTime, timeline.EndTime, timeline.Position, timeline.MaxSeekTime,
                timeline.LastUpdatedTime, playing, rate, DateTimeOffset.UtcNow, positionClock);
            duration = mediaTimeline.Duration;
            timelineStartSeconds = mediaTimeline.Start;
            hasTimelinePosition = mediaTimeline.HasPosition;
            positionBase = mediaTimeline.PositionAt(positionClock);
            SeekSlider.IsEnabled = info.Controls.IsPlaybackPositionEnabled && hasTimelinePosition && duration > 0;
            CurrentDiscordTrack = new PresenceTrack(properties.Title, properties.Artist, SourceName(selected.SourceAppUserModelId), CurrentPosition, duration, playing, DateTimeOffset.UtcNow, properties.AlbumTitle, HasTimelinePosition: hasTimelinePosition);
            if (!demo) discord.Update(CurrentDiscordTrack);
            settingsWindow?.RefreshDiscordStatus();
            if (trackKey != key)
            {
                trackKey = key;
                lyricsCancellation?.Cancel();
                lyricsCancellation?.Dispose();
                lyricsCancellation = new CancellationTokenSource();
                lyrics = [];
                TrackTitle.Text = string.IsNullOrWhiteSpace(properties.Title) ? Localization.T("Без названия") : properties.Title;
                fallbackArtist = string.IsNullOrWhiteSpace(properties.Artist) ? SourceName(selected.SourceAppUserModelId) : properties.Artist;
                ShowLine(fallbackArtist);
                FitWidth(fallbackArtist);
                lyricsStatus = "Поиск синхронизированного текста…";
                string lookupArtist = string.IsNullOrWhiteSpace(properties.AlbumArtist) ? properties.Artist : properties.AlbumArtist;
                lookupArtist = lookupArtist.Split(" — ")[0].Trim();
                _ = LoadLyrics(key, properties.Title, lookupArtist, properties.AlbumTitle, duration, lyricsCancellation.Token);
                bool hasArtwork = false;
                try
                {
                    if (properties.Thumbnail is not null)
                    {
                        using var stream = await properties.Thumbnail.OpenReadAsync();
                        if (stream.Size is > 0 and < 20_000_000)
                        {
                            using var reader = new DataReader(stream);
                            await reader.LoadAsync((uint)stream.Size);
                            byte[] bytes = new byte[(int)stream.Size];
                            reader.ReadBytes(bytes);
                            using var memory = new MemoryStream(bytes);
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.StreamSource = memory;
                            bitmap.DecodePixelWidth = 160;
                            bitmap.EndInit();
                            bitmap.Freeze();
                            if (closing) return;
                            SetCover(bitmap);
                            hasArtwork = true;
                        }
                    }
                }
                catch (Exception ex) { Debug.WriteLine("Artwork: " + ex.Message); }
                if (!hasArtwork) SetCover(null);
            }
            ToolTip = $"{properties.Title}\n{properties.Artist}\n{SourceName(selected.SourceAppUserModelId)}\n{Localization.T(lyricsStatus)}\n{Localization.T("Перетащите панель, чтобы переместить")}";
        }
        catch (Exception ex) { discord.Update(null); ToolTip = Localization.T("Медиасессия обновляется: ") + ex.Message; }
        finally { updating = false; }
    }

    private async Task LoadLyrics(string key, string title, string artist, string album, double seconds, CancellationToken token)
    {
        try
        {
            var result = await lyricsService.Find(title, artist, album, seconds, token);
            if (closing || token.IsCancellationRequested || key != trackKey) return;
            lyrics = result.Lines;
            lyricsStatus = result.Status;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closing && key == trackKey) lyricsStatus = "Текст недоступен"; Debug.WriteLine(ex); }
    }

    private double CurrentPosition => hasTimelinePosition ? MediaTimeline.ClampPosition(positionBase + (playing ? (clock.Elapsed.TotalSeconds - positionClock) * rate : 0), duration) : 0;

    private void RenderFrame(object? sender, EventArgs e)
    {
        if (closing || !IsVisible) return;
        if (e is not RenderingEventArgs rendering || !renderClock.Advance(rendering.RenderingTime, out var elapsed)) return;
        double now = clock.Elapsed.TotalSeconds;
        GlassShader.Time = now;
        discVelocity = HudMotion.Follow(discVelocity, playing ? 55 : 0, elapsed, playing ? 2.2 : 3.5);
        if (!playing && discVelocity < 0.01) discVelocity = 0;
        DiscRotation.Angle = IsLiquidGlass ? 0 : (DiscRotation.Angle + elapsed * discVelocity) % 360;
        if (IsLiquidGlass)
        {
            var pointer = IsMouseOver ? Mouse.GetPosition(PlayerCanvas) : new Point(PlayerCanvas.Width * 0.72, PlayerCanvas.Height * 0.12);
            GlassShader.Pointer = new Point(HudMotion.Follow(GlassShader.Pointer.X, Math.Clamp(pointer.X / PlayerCanvas.Width, 0, 1), elapsed, 10), HudMotion.Follow(GlassShader.Pointer.Y, Math.Clamp(pointer.Y / PlayerCanvas.Height, 0, 1), elapsed, 10));
        }
        AdvanceCover(now);
        TrackTitle.Advance();
        Artist.Advance();
        AdvanceWidth(elapsed);
        AdvanceExpansion(elapsed);
        PauseIcon.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        PlayIcon.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        ExpandedPlayButton.Content = playing ? "Ⅱ" : "▶";
        double position = seekDragging ? SeekSlider.Value * duration : CurrentPosition;
        if (!seekDragging) SeekSlider.Value = duration > 0 ? position / duration : 0;
        ElapsedTime.Text = hasTimelinePosition ? FormatTime(position) : "--:--";
        RemainingTime.Text = "−" + FormatTime(Math.Max(0, duration - position));
        DrawRing(duration > 0 ? position / duration : 0);
        Dot1.Opacity = playing ? 0.5 + 0.35 * Math.Sin(now * 2.8) : 0.5;
        Dot2.Opacity = playing ? 0.5 + 0.35 * Math.Sin(now * 2.8 - 0.7) : 0.5;
        Dot3.Opacity = playing ? 0.5 + 0.35 * Math.Sin(now * 2.8 - 1.4) : 0.5;
        if (lyrics.Count > 0)
        {
            // Upper bound search also handles seeking backwards and repeated timestamps.
            int low = 0, high = lyrics.Count;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (lyrics[middle].Seconds <= position + HudMotion.LyricsLeadSeconds + lyricOffsetSeconds) low = middle + 1;
                else high = middle;
            }
            string text = low > 0 ? lyrics[low - 1].Text : fallbackArtist;
            ShowLine(string.IsNullOrWhiteSpace(text) ? fallbackArtist : text, low > 0 && !string.IsNullOrWhiteSpace(text));
        }
    }

    private void DrawRing(double fraction)
    {
        fraction = double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0;
        if (fraction <= 0) { ProgressRing.Data = null; return; }
        if (fraction >= 0.99999) { ProgressRing.Data = fullRing; return; }
        double angle = fraction * Math.PI * 2;
        ringArc.Point = new Point(28.5 + Math.Sin(angle) * 25.5, 28.5 - Math.Cos(angle) * 25.5);
        ringArc.IsLargeArc = fraction > 0.5;
        ProgressRing.Data = ringGeometry;
    }

    private void ShowLine(string text, bool isLyric = false)
    {
        Artist.IsLyric = isLyric;
        if (Artist.Text == text) return;
        Artist.Text = text;
        FitWidth(text);
    }

    private void FitWidth(string text)
    {
        double Measure(string value, double fontSize) => new FormattedText(value, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface(new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), fontSize, System.Windows.Media.Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;
        var screen = ScreenArea();
        targetWidth = Math.Clamp((Math.Max(Measure(TrackTitle.Text, 25), Measure(text, 23)) + 306) * PlayerScale, 440 * PlayerScale, Math.Max(440 * PlayerScale, Math.Min(1050 * PlayerScale, screen.Width - 32)));
    }

    private void AdvanceWidth(double elapsed)
    {
        if (Math.Abs(targetWidth - Width) < 0.01) return;
        var center = Left + Width / 2;
        var next = HudMotion.Follow(Width, targetWidth, elapsed, 7);
        if (Math.Abs(targetWidth - next) < 0.15 * PlayerScale) next = targetWidth;
        var screen = ScreenArea();
        Width = next;
        Left = Math.Clamp(center - next / 2, screen.Left, Math.Max(screen.Left, screen.Right - next));
    }

    private void SetCover(ImageSource? artwork)
    {
        PreviousCover.Source = Cover.Source;
        Cover.Source = artwork;
        coverChangedAt = clock.Elapsed.TotalSeconds;
        OldCoverLayer.Opacity = PreviousCover.Source is null ? 0 : 1;
        NewCoverLayer.Opacity = 0;
        VinylDetails.Visibility = artwork is null || IsLiquidGlass ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AdvanceCover(double now)
    {
        if (coverChangedAt < 0) return;
        var frame = HudMotion.Cover(now - coverChangedAt);
        NewCoverScale.ScaleX = NewCoverScale.ScaleY = frame.NewScale;
        OldCoverScale.ScaleX = OldCoverScale.ScaleY = frame.OldScale;
        NewCoverLayer.Opacity = frame.Opacity;
        OldCoverLayer.Opacity = PreviousCover.Source is null ? 0 : 1 - frame.Opacity;
        if (frame.Opacity >= 1) { PreviousCover.Source = null; coverChangedAt = -1; }
    }

    private void AdvanceExpansion(double elapsed)
    {
        bool hover = (IsMouseOver || seekDragging) && (session is not null || demo);
        if (hover && expansion == 0) expandUp = Top + (144 + 96) * PlayerScale > ScreenArea().Bottom;
        double next = HudMotion.Follow(expansion, hover ? 1 : 0, elapsed, 12);
        if (Math.Abs(next - (hover ? 1 : 0)) < 0.001) next = hover ? 1 : 0;
        if (next == expansion) return;
        double oldHeight = Height;
        expansion = next;
        Height = (144 + 96 * expansion) * PlayerScale;
        if (expandUp) Top += oldHeight - Height;
        ExpandedControls.Opacity = expansion;
        ExpandedControls.IsHitTestVisible = expansion > 0.8;
    }

    private static string FormatTime(double seconds) => MediaTimeline.Format(seconds);
    private void SeekStart(object sender, MouseButtonEventArgs e)
    {
        if (!SeekSlider.IsEnabled || duration <= 0) return;
        seekDragging = true;
        seekTrackKey = trackKey;
        SeekSlider.CaptureMouse();
        UpdateSeekPointer(e.GetPosition(SeekSlider).X);
        e.Handled = true;
    }
    private void UpdateSeekPointer(double x) => SeekSlider.Value = Math.Clamp((x - 6.5) / Math.Max(1, SeekSlider.ActualWidth - 13), 0, 1);
    private void SeekMove(object sender, MouseEventArgs e)
    {
        if (seekDragging && e.LeftButton == MouseButtonState.Pressed) UpdateSeekPointer(e.GetPosition(SeekSlider).X);
    }
    private async void SeekEnd(object sender, MouseButtonEventArgs e)
    {
        if (seekDragging) UpdateSeekPointer(e.GetPosition(SeekSlider).X);
        await CommitSeek();
        SeekSlider.ReleaseMouseCapture();
        e.Handled = true;
    }
    private async void SeekLostCapture(object sender, MouseEventArgs e) => await CommitSeek();
    private async Task CommitSeek()
    {
        if (!seekDragging) return;
        seekDragging = false;
        if (seekTrackKey != trackKey || duration <= 0) return;
        double position = SeekSlider.Value * duration;
        if (demo) { positionBase = position; positionClock = clock.Elapsed.TotalSeconds; return; }
        mediaTimeline.Seek(position, clock.Elapsed.TotalSeconds);
        await Control(async s => await s.TryChangePlaybackPositionAsync(TimeSpan.FromSeconds(timelineStartSeconds + position).Ticks));
    }
    private async void PreviousClick(object sender, RoutedEventArgs e) => await Control(async s => await s.TrySkipPreviousAsync());
    private async void NextClick(object sender, RoutedEventArgs e) => await Control(async s => await s.TrySkipNextAsync());
    private async void ShuffleClick(object sender, RoutedEventArgs e) => await Control(async s => await s.TryChangeShuffleActiveAsync(s.GetPlaybackInfo().IsShuffleActive != true));
    private async void RepeatClick(object sender, RoutedEventArgs e) => await Control(async s => await s.TryChangeAutoRepeatModeAsync(s.GetPlaybackInfo().AutoRepeatMode switch { MediaPlaybackAutoRepeatMode.None => MediaPlaybackAutoRepeatMode.List, MediaPlaybackAutoRepeatMode.List => MediaPlaybackAutoRepeatMode.Track, _ => MediaPlaybackAutoRepeatMode.None }));

    private static string SourceName(string id) => MediaSource.DisplayName(id);

    private async Task Control(Func<MediaSession, Task<bool>> action)
    {
        if (demo) { positionBase = CurrentPosition; positionClock = clock.Elapsed.TotalSeconds; playing = !playing; return; }
        if (session is null) return;
        try { if (!await action(session)) ToolTip = Localization.T("Приложение не выполнило команду"); }
        catch (Exception ex) { ToolTip = Localization.T("Не удалось выполнить команду: ") + ex.Message; }
        await Refresh();
    }
    private async void PlayClick(object sender, RoutedEventArgs e) => await Control(async s => await s.TryTogglePlayPauseAsync());

    private async void MenuClick(object sender, RoutedEventArgs e)
    {
        if (manager is null && !demo) await Connect();
        var menu = CreateThemedMenu();
        void Add(string label, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = Localization.T(label), IsEnabled = enabled, Icon = label.StartsWith("Настройки") ? "⚙" : label.StartsWith("Предыдущий") ? "‹" : label.StartsWith("Следующий") ? "›" : label.StartsWith("Загрузить") ? "♪" : label == "Выход" ? "×" : label.StartsWith("Скрыть") ? "−" : null };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Add("Настройки плеера", OpenSettings);
        menu.Items.Add(MenuSeparator());
        Add("Предыдущий трек", async () => await Control(async s => await s.TrySkipPreviousAsync()), previousEnabled);
        Add("Следующий трек", async () => await Control(async s => await s.TrySkipNextAsync()), nextEnabled);
        menu.Items.Add(MenuSeparator());
        var automatic = new MenuItem { Header = Localization.T("Автоматический выбор"), IsCheckable = true, IsChecked = pinnedSource is null };
        automatic.Click += async (_, _) => { pinnedSource = null; await Refresh(); };
        menu.Items.Add(automatic);
        if (manager is not null)
            foreach (var source in manager.GetSessions())
            {
                string id = source.SourceAppUserModelId;
                var item = new MenuItem { Header = SourceName(id), IsCheckable = true, IsChecked = pinnedSource == id };
                item.Click += async (_, _) => { pinnedSource = id; await Refresh(); };
                menu.Items.Add(item);
            }
        menu.Items.Add(MenuSeparator());
        Add(lyricsStatus, () => { }, false);
        Add("Загрузить текст из .lrc…", LoadLocalLyrics);
        Add($"{Localization.T("Текст раньше на 0,25 с")} ({Localization.T("Сдвиг")}: {lyricOffsetSeconds:+0.00;-0.00;0.00})", () => lyricOffsetSeconds += 0.25);
        Add("Текст позже на 0,25 с", () => lyricOffsetSeconds -= 0.25);
        Add("Сбросить сдвиг текста", () => lyricOffsetSeconds = 0);
        menu.Items.Add(MenuSeparator());
        Add("Скрыть в трей", Hide);
        Add("Выход", Close);
        menu.PlacementTarget = (System.Windows.Controls.Button)sender;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.VerticalOffset = 2;
        menu.IsOpen = true;
    }

    private System.Windows.Controls.ContextMenu CreateThemedMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu
        {
            Style = (Style)FindResource("GlassContextMenu"),
        };
        menu.Resources["MenuBackground"] = CurrentTheme.Background;
        menu.Resources["MenuText"] = CurrentTheme.Text;
        menu.Resources["MenuSecondary"] = CurrentTheme.Secondary;
        menu.Resources["MenuAccent"] = CurrentTheme.Accent;
        var border = CurrentTheme.LightColor; border.A = 65;
        var hover = CurrentTheme.LightColor; hover.A = CurrentTheme.Name == "Porcelain" ? (byte)190 : (byte)24;
        menu.Resources["MenuBorder"] = new SolidColorBrush(border);
        menu.Resources["MenuHover"] = new SolidColorBrush(hover);
        return menu;
    }

    private Separator MenuSeparator() => new() { Style = (Style)FindResource("GlassMenuSeparator") };

    private void LoadLocalLyrics()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = Localization.T("Синхронизированный текст (*.lrc)|*.lrc"), Title = Localization.T("Текст текущего трека") };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var parsed = LyricsService.Parse(File.ReadAllText(dialog.FileName));
            if (parsed.Count == 0) { System.Windows.MessageBox.Show(this, Localization.T("В файле нет строк с временными метками [мм:сс.xx]."), "Glass Player"); return; }
            lyricsCancellation?.Cancel();
            lyrics = parsed;
            lyricsStatus = "Текст из файла .lrc";
        }
        catch (Exception ex) { System.Windows.MessageBox.Show(this, ex.Message, Localization.T("Не удалось открыть текст")); }
    }

    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject target)
            for (var parent = target; parent is not null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is System.Windows.Controls.Button or Slider) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var width = Width; var left = Left;
        BeginAnimation(WidthProperty, null); BeginAnimation(LeftProperty, null);
        Width = width; Left = left;
        DragMove();
        ClampPosition();
    }

    private Rect ScreenArea()
    {
        // Transform physical monitor work areas to WPF logical coordinates.
        var dpi = VisualTreeHelper.GetDpi(this);
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)((Left + Width / 2) * dpi.DpiScaleX), (int)((Top + Height / 2) * dpi.DpiScaleY))).WorkingArea;
        return new Rect(screen.Left / dpi.DpiScaleX, screen.Top / dpi.DpiScaleY, screen.Width / dpi.DpiScaleX, screen.Height / dpi.DpiScaleY);
    }
    private void ClampPosition()
    {
        var screen = ScreenArea();
        Left = Math.Clamp(Left, screen.Left, Math.Max(screen.Left, screen.Right - Width));
        Top = Math.Clamp(Top, screen.Top, Math.Max(screen.Top, screen.Bottom - Height));
    }

    private void StartDemo()
    {
        TrackTitle.Text = "Glass Player";
        fallbackArtist = Localization.T("Проверка анимации");
        lyrics = [new(0, Localization.T("Короткая строка")), new(3, Localization.T("Плавно появляется следующая строка")), new(6, Localization.T("И снова короче")), new(9, Localization.T("Свет движется по закруглённому контуру"))];
        lyricsStatus = "Демонстрация анимации";
        duration = 15; hasTimelinePosition = true; playing = true; positionBase = 0; positionClock = clock.Elapsed.TotalSeconds;
        SeekSlider.IsEnabled = true;
        PreviousButton.IsEnabled = NextButton.IsEnabled = ShuffleButton.IsEnabled = RepeatButton.IsEnabled = false;
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            context.DrawRectangle(new LinearGradientBrush(System.Windows.Media.Color.FromRgb(231, 99, 76), System.Windows.Media.Color.FromRgb(29, 83, 92), 45), null, new Rect(0, 0, 76, 76));
            context.DrawEllipse(System.Windows.Media.Brushes.Cornsilk, null, new Point(22, 21), 13, 13);
            context.DrawEllipse(System.Windows.Media.Brushes.Teal, null, new Point(51, 55), 22, 22);
        }
        SetCover(new DrawingImage(drawing));
    }
    private void UpdateCanvas()
    {
        PlayerCanvas.Width = Math.Max(1, ActualWidth / PlayerScale);
        PlayerCanvas.Height = Math.Max(144, ActualHeight / PlayerScale);
        GlassShader.Size = new Point(PlayerCanvas.Width, PlayerCanvas.Height);
        liquidBackdrop?.RequestUpdate();
    }

    private void ApplyTheme()
    {
        GlassShader.Liquid = IsLiquidGlass ? 1 : 0;
        LiquidPlaySurface.Visibility = IsLiquidGlass ? Visibility.Visible : Visibility.Collapsed;
        Disc.Clip = IsLiquidGlass ? new RectangleGeometry(new Rect(0, 0, 76, 76), 19, 19) : new EllipseGeometry(new Point(38, 38), 38, 38);
        VinylDetails.Visibility = !IsLiquidGlass && Cover.Source is not null ? Visibility.Visible : Visibility.Collapsed;
        GlassShader.Tint = CurrentTheme.BackgroundColor;
        GlassShader.Light = CurrentTheme.LightColor;
        TrackTitle.Foreground = CurrentTheme.Text;
        Artist.Foreground = IsLiquidGlass ? CurrentTheme.Text : CurrentTheme.Secondary;
        ProgressRing.Stroke = Dot1.Fill = Dot2.Fill = Dot3.Fill = CurrentTheme.Accent;
        PauseBar1.Background = PauseBar2.Background = PlayIcon.Fill = CurrentTheme.Secondary;
        var trackColor = CurrentTheme.LightColor; trackColor.A = 40;
        RingTrack.Stroke = new SolidColorBrush(trackColor);
        Resources["PlayerAccent"] = CurrentTheme.Accent;
        Resources["PlayerText"] = CurrentTheme.Text;
        Resources["PlayerSecondary"] = IsLiquidGlass ? CurrentTheme.Text : CurrentTheme.Secondary;
        Resources["PlayerTrack"] = RingTrack.Stroke;
        var hoverColor = CurrentTheme.Accent.Color; hoverColor.A = 30;
        Resources["PlayerHover"] = new SolidColorBrush(hoverColor);
        var discColor = CurrentTheme.Name == "Porcelain" ? System.Windows.Media.Color.FromArgb(85, 192, 201, 217) : System.Windows.Media.Color.FromArgb(80, 0, 0, 0);
        DiscBackdrop.Background = IsLiquidGlass ? System.Windows.Media.Brushes.Transparent : new SolidColorBrush(discColor);
        liquidBackdrop?.Update(true);
    }

    public void SetDesign(PlayerDesign design)
    {
        CurrentDesign = design == PlayerDesign.LiquidGlass ? design : PlayerDesign.Classic;
        ApplyTheme();
        SaveSettings();
    }

    public void SetAppearance(string themeName, double scale)
    {
        CurrentTheme = PlayerTheme.Find(themeName);
        PlayerScale = double.IsFinite(scale) ? Math.Clamp(scale, 0.6, 1.5) : 1;
        var centerY = Top + Height / 2;
        Height = (144 + 96 * expansion) * PlayerScale;
        Top = centerY - Height / 2;
        ApplyTheme();
        settingsWindow?.ApplyTheme();
        UpdateCanvas();
        FitWidth(Artist.Text);
        ClampPosition();
        SaveSettings();
    }

    private void OpenSettings()
    {
        if (settingsWindow is not null) { settingsWindow.Activate(); return; }
        settingsWindow = new SettingsWindow(this) { Owner = this };
        settingsWindow.Closed += (_, _) => settingsWindow = null;
        settingsWindow.Show();
    }

    public void SetLanguage(string language)
    {
        Localization.SetLanguage(language);
        if (trackKey.Length == 0 && !demo)
        {
            TrackTitle.Text = Localization.T("Включите музыку");
            fallbackArtist = Localization.T("Ожидание медиасессии Windows");
            ShowLine(fallbackArtist);
        }
        SaveSettings();
    }

    public bool SetDiscord(bool enabled, string applicationId)
    {
        applicationId = applicationId.Trim();
        if (applicationId.Length > 0 && !DiscordPresenceService.IsValidApplicationId(applicationId)) return false;
        DiscordEnabled = enabled;
        DiscordApplicationId = applicationId;
        if (!demo) discord.Configure(enabled, applicationId);
        SaveSettings();
        return true;
    }

    private void SaveSettings()
    {
        if (demo) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(new Position(Left, Top, lyricOffsetSeconds, CurrentTheme.Name, PlayerScale, Localization.Language, DiscordEnabled, DiscordApplicationId, CurrentDesign.ToString())));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine(ex); }
    }
    private sealed record Position(double Left, double Top, double LyricOffset = 0, string Theme = "Lagoon", double Scale = 1, string Language = "ru", bool DiscordEnabled = true, string DiscordApplicationId = DiscordPresenceService.DefaultApplicationId, string Design = "Classic");
}
