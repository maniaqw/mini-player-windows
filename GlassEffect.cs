using System.Windows;
using System.Windows.Media.Effects;
using System.Windows.Media;

namespace GlassPlayer;

public sealed class GlassEffect : ShaderEffect
{
    private static readonly PixelShader Shader = new()
    {
        UriSource = new Uri("pack://application:,,,/GlassPlayer;component/Shaders/Glass.ps"),
    };
    public static readonly DependencyProperty TimeProperty = DependencyProperty.Register(nameof(Time), typeof(double), typeof(GlassEffect), new UIPropertyMetadata(0.0, PixelShaderConstantCallback(0)));
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(nameof(Size), typeof(Point), typeof(GlassEffect), new UIPropertyMetadata(new Point(650, 144), PixelShaderConstantCallback(1)));
    public static readonly DependencyProperty TintProperty = DependencyProperty.Register(nameof(Tint), typeof(Color), typeof(GlassEffect), new UIPropertyMetadata(PlayerTheme.Find("Lagoon").BackgroundColor, PixelShaderConstantCallback(2)));
    public static readonly DependencyProperty LightProperty = DependencyProperty.Register(nameof(Light), typeof(Color), typeof(GlassEffect), new UIPropertyMetadata(PlayerTheme.Find("Lagoon").LightColor, PixelShaderConstantCallback(3)));
    public double Time { get => (double)GetValue(TimeProperty); set => SetValue(TimeProperty, value); }
    public Point Size { get => (Point)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public Color Tint { get => (Color)GetValue(TintProperty); set => SetValue(TintProperty, value); }
    public Color Light { get => (Color)GetValue(LightProperty); set => SetValue(LightProperty, value); }
    public static readonly DependencyProperty LiquidProperty = DependencyProperty.Register(nameof(Liquid), typeof(double), typeof(GlassEffect), new UIPropertyMetadata(0.0, PixelShaderConstantCallback(4)));
    public static readonly DependencyProperty PointerProperty = DependencyProperty.Register(nameof(Pointer), typeof(Point), typeof(GlassEffect), new UIPropertyMetadata(new Point(0.75, 0.15), PixelShaderConstantCallback(5)));
    public double Liquid { get => (double)GetValue(LiquidProperty); set => SetValue(LiquidProperty, value); }
    public Point Pointer { get => (Point)GetValue(PointerProperty); set => SetValue(PointerProperty, value); }
    public GlassEffect()
    {
        PixelShader = Shader;
        UpdateShaderValue(TimeProperty);
        UpdateShaderValue(SizeProperty);
        UpdateShaderValue(TintProperty);
        UpdateShaderValue(LightProperty);
        UpdateShaderValue(LiquidProperty);
        UpdateShaderValue(PointerProperty);
    }
}
