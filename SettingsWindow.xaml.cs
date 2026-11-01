using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GlassPlayer;

public partial class SettingsWindow : Window
{
    private readonly MainWindow player;
    private bool ready;
    private bool discordIdInvalid;
    public SettingsWindow(MainWindow player)
    {
        this.player = player;
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 32;
        Height = Math.Min(Height, MaxHeight);
        Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - Width) / 2;
        Top = SystemParameters.WorkArea.Top + (SystemParameters.WorkArea.Height - Height) / 2;
        ThemeList.ItemsSource = PlayerTheme.All;
        ScaleSlider.Value = player.PlayerScale * 100;
        ScaleLabel.Text = $"{ScaleSlider.Value:0}%";
        DiscordEnabledToggle.IsChecked = player.DiscordEnabled;
        DiscordIdInput.Text = player.DiscordApplicationId;
        ClassicDesign.IsChecked = !player.IsLiquidGlass;
        LiquidDesign.IsChecked = player.IsLiquidGlass;
        ApplyTheme();
        RefreshDiscordStatus();
        Loaded += (_, _) => { SelectCard(player.CurrentTheme.Name); ready = true; };
    }
    private void ThemeSelected(object sender, RoutedEventArgs e)
    {
        if (!ready || ((RadioButton)sender).Tag is not PlayerTheme theme) return;
        player.SetAppearance(theme.Name, ScaleSlider.Value / 100);
    }
    private void DesignSelected(object sender, RoutedEventArgs e)
    {
        if (ready) player.SetDesign(PlayerDesigns.Parse((string)((RadioButton)sender).Tag));
    }
    private void ScaleChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ScaleLabel is not null) ScaleLabel.Text = $"{e.NewValue:0}%";
        if (ready) player.SetAppearance(player.CurrentTheme.Name, e.NewValue / 100);
    }
    private void ScalePreset(object sender, RoutedEventArgs e) => ScaleSlider.Value = double.Parse((string)((Button)sender).Tag, CultureInfo.InvariantCulture);
    private void LanguageClick(object sender, RoutedEventArgs e)
    {
        player.SetLanguage((string)((Button)sender).Tag);
        UpdateLanguageButtons();
        RefreshDiscordStatus();
    }
    private void UpdateLanguageButtons()
    {
        RussianButton.SetResourceReference(BorderBrushProperty, Localization.Language == "ru" ? "AccentBrush" : "SettingsBorder");
        EnglishButton.SetResourceReference(BorderBrushProperty, Localization.Language == "en" ? "AccentBrush" : "SettingsBorder");
    }
    public void ApplyTheme()
    {
        foreach (var entry in player.CurrentTheme.SettingsBrushes()) Resources[entry.Key] = entry.Value;
        UpdateLanguageButtons();
    }
    private void ResetClick(object sender, RoutedEventArgs e)
    {
        ready = false;
        ScaleSlider.Value = 100;
        SelectCard("Lagoon");
        ClassicDesign.IsChecked = true;
        ready = true;
        player.SetAppearance("Lagoon", 1);
        player.SetDesign(PlayerDesign.Classic);
    }
    public void RefreshDiscordStatus()
    {
        DiscordStatusLabel.Text = Localization.T(discordIdInvalid ? "Application ID должен содержать 16–20 цифр" : player.DiscordStatus);
        DiscordStatusLabel.ToolTip = player.DiscordDiagnostic;
        var track = player.CurrentDiscordTrack;
        DiscordPreview.Text = track is null ? "" : $"{track.Source} · {track.Title}\n{track.Artist}";
    }
    private void DiscordToggleChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        player.SetDiscord(DiscordEnabledToggle.IsChecked == true, player.DiscordApplicationId);
        RefreshDiscordStatus();
    }
    private void DiscordApplyClick(object sender, RoutedEventArgs e) => ApplyDiscordId();
    private void DiscordIdKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { ApplyDiscordId(); e.Handled = true; } }
    private void DiscordIdEdited(object sender, TextChangedEventArgs e) { if (ready) { discordIdInvalid = false; RefreshDiscordStatus(); } }
    private void ApplyDiscordId()
    {
        if (!player.SetDiscord(DiscordEnabledToggle.IsChecked == true, DiscordIdInput.Text))
        {
            discordIdInvalid = true;
            RefreshDiscordStatus();
            return;
        }
        discordIdInvalid = false;
        DiscordIdInput.Text = player.DiscordApplicationId;
        RefreshDiscordStatus();
    }
    private void OpenDiscordPortal(object sender, RoutedEventArgs e) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://discord.com/developers/applications") { UseShellExecute = true });
    private void SelectCard(string name)
    {
        void Visit(DependencyObject element)
        {
            if (element is RadioButton radio && radio.Tag is PlayerTheme theme) radio.IsChecked = theme.Name == name;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) Visit(VisualTreeHelper.GetChild(element, i));
        }
        Visit(ThemeList);
    }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void WindowKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); }
    private void HeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject target)
            for (var parent = target; parent is not null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is Button) return;
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
