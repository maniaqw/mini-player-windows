using System.Windows;
using System.Windows.Media;

namespace GlassPlayer;

public sealed class DesignPreview : FrameworkElement
{
    public static readonly DependencyProperty LiquidProperty = DependencyProperty.Register(nameof(Liquid), typeof(bool), typeof(DesignPreview), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool Liquid { get => (bool)GetValue(LiquidProperty); set => SetValue(LiquidProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        var body = new Rect(1, 4, Math.Max(1, w - 2), h - 8);
        if (Liquid)
        {
            dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromRgb(54, 90, 113), Color.FromRgb(82, 75, 109), 30), null, new Rect(0, 0, w, h), 9, 9);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 94, 217, 219)), null, new Point(w * 0.25, h * 0.8), 30, 20);
        }
        var fill = Liquid ? new LinearGradientBrush(Color.FromArgb(85, 255, 255, 255), Color.FromArgb(28, 255, 255, 255), 90) : (Brush)PlayerTheme.Brush("#101D28");
        var border = new Pen(PlayerTheme.Brush(Liquid ? "#99FFFFFF" : "#8396B2"), Liquid ? 0.9 : 1.3);
        dc.DrawRoundedRectangle(fill, border, body, Liquid ? 24 : 11, Liquid ? 24 : 11);
        var cover = new LinearGradientBrush(Color.FromRgb(114, 180, 172), Color.FromRgb(69, 92, 127), 55);
        if (Liquid) dc.DrawRoundedRectangle(cover, null, new Rect(12, 14, 28, 28), 8, 8);
        else dc.DrawEllipse(cover, null, new Point(26, 28), 14, 14);
        dc.DrawRoundedRectangle(PlayerTheme.Brush("#DFEAF3"), null, new Rect(49, 22, Math.Max(12, w - 97), 3), 1.5, 1.5);
        dc.DrawRoundedRectangle(PlayerTheme.Brush("#9EB5C9"), null, new Rect(49, 31, Math.Max(10, w - 112), 2), 1, 1);
        var center = new Point(w - 24, 28);
        dc.DrawEllipse(Liquid ? PlayerTheme.Brush("#22FFFFFF") : null, new Pen(PlayerTheme.Brush("#99FFFFFF"), 1), center, 11, 11);
        dc.DrawRectangle(PlayerTheme.Brush("#EAF2F6"), null, new Rect(center.X - 3.5, 24, 2, 8));
        dc.DrawRectangle(PlayerTheme.Brush("#EAF2F6"), null, new Rect(center.X + 1.5, 24, 2, 8));
    }
}
