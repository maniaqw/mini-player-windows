using System.Windows;
using System.Windows.Media;

namespace GlassPlayer;

public sealed class ThemePreview : FrameworkElement
{
    public static readonly DependencyProperty ThemeProperty = DependencyProperty.Register(nameof(Theme), typeof(PlayerTheme), typeof(ThemePreview), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public PlayerTheme? Theme { get => (PlayerTheme?)GetValue(ThemeProperty); set => SetValue(ThemeProperty, value); }
    protected override void OnRender(DrawingContext context)
    {
        if (Theme is null) return;
        double w = ActualWidth, h = ActualHeight;
        void Line(double x, double y, double length, System.Windows.Media.Brush brush, double opacity = 1)
        {
            context.PushOpacity(opacity);
            context.DrawRoundedRectangle(brush, null, new Rect(x, y, length, 1.6), 0.8, 0.8);
            context.Pop();
        }
        Line(0, 4, w * 0.29, Theme.Accent);
        Line(0, 12, w * 0.2, Theme.Secondary, 0.7);
        Line(0, 20, w * 0.25, Theme.Secondary, 0.5);
        Line(w * 0.36, 4, w * 0.5, Theme.Text, 0.9);
        Line(w * 0.36, 12, w * 0.34, Theme.Secondary, 0.7);
        Line(w * 0.36, 20, w * 0.43, Theme.Secondary, 0.5);
        context.DrawRoundedRectangle(Theme.Accent, null, new Rect(w - 9, h - 5, 8, 3.5), 1.75, 1.75);
    }
}
