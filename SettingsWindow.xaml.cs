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
        ApplyTheme();
        Loaded += (_, _) => { SelectCard(player.CurrentTheme.Name); ready = true; };
    }
    private void ThemeSelected(object sender, RoutedEventArgs e)
    {
        if (!ready || ((RadioButton)sender).Tag is not PlayerTheme theme) return;
        player.SetAppearance(theme.Name, ScaleSlider.Value / 100);
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
        ready = true;
        player.SetAppearance("Lagoon", 1);
    }
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
