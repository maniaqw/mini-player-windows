using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

namespace GlassPlayer;

public sealed class AnimatedLine : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(AnimatedLine), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, Changed));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public bool IsLyric { get; set; }
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private string previous = "";
    private double changedAt, hoverAt;
    private bool dirty = true;
    private Run? currentRun, previousRun;
    private FormattedText? clippedRun;
    private double clippedWidth = -1;
    private bool contentChanged;
    private double PixelsPerDip => VisualTreeHelper.GetDpi(this).PixelsPerDip;

    public AnimatedLine()
    {
        ClipToBounds = true;
        MouseEnter += (_, _) => { hoverAt = Clock.Elapsed.TotalSeconds; InvalidateVisual(); };
        MouseLeave += (_, _) => InvalidateVisual();
    }
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var line = (AnimatedLine)d;
        line.previous = (string?)e.OldValue ?? "";
        line.changedAt = Clock.Elapsed.TotalSeconds;
        line.dirty = true;
        line.contentChanged = true;
        line.hoverAt = line.changedAt;
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ForegroundProperty || e.Property == FontSizeProperty || e.Property == FontWeightProperty || e.Property == FontFamilyProperty)
        { dirty = true; contentChanged = false; InvalidateVisual(); }
    }
    private FormattedText Format(string text) => new(text, CultureInfo.CurrentUICulture, FlowDirection, new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Foreground, PixelsPerDip);
    private Run Layout(string text)
    {
        var letters = new List<Letter>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        var prefix = "";
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            letters.Add(new(Format(element), prefix.Length == 0 ? 0 : Format(prefix).WidthIncludingTrailingWhitespace));
            prefix += element;
        }
        return new(Format(text), letters);
    }
    private void EnsureLayout()
    {
        if (!dirty) return;
        previousRun = contentChanged && currentRun?.Full.Text == previous ? currentRun : Layout(previous);
        currentRun = Layout(Text ?? "");
        clippedRun = null;
        clippedWidth = -1;
        contentChanged = false;
        dirty = false;
    }
    public void Advance()
    {
        if (Clock.Elapsed.TotalSeconds - changedAt < Math.Max(0.3 + (Text?.Length ?? 0) * 0.01, 0.2 + previous.Length * 0.005) + 0.08 || IsMouseOver && currentRun?.Full.WidthIncludingTrailingWhitespace > ActualWidth)
            InvalidateVisual();
    }
    protected override System.Windows.Size MeasureOverride(System.Windows.Size constraint)
    {
        EnsureLayout();
        return new System.Windows.Size(Math.Min(constraint.Width, currentRun!.Full.WidthIncludingTrailingWhitespace), FontSize * 1.4);
    }
    protected override void OnRender(DrawingContext context)
    {
        EnsureLayout();
        var age = (Clock.Elapsed.TotalSeconds - changedAt) * 1000;
        var delay = previous.Length > 0 && !IsLyric ? 60 : 0;
        if (age >= 300 + currentRun!.Letters.Count * 10 + delay)
        {
            DrawSettled(context, currentRun.Full);
            return;
        }
        DrawLetters(context, previousRun!, age, false);
        DrawLetters(context, currentRun!, age - delay, true);
    }
    private void DrawLetters(DrawingContext context, Run run, double age, bool incoming)
    {
        for (int i = 0; i < run.Letters.Count; i++)
        {
            var letter = run.Letters[i];
            if (letter.X > ActualWidth) break;
            var frame = HudMotion.Letter(age, i, FontSize, incoming);
            if (frame.Opacity <= 0.001) continue;
            context.PushOpacity(frame.Opacity);
            context.DrawText(letter.Text, new Point(letter.X + frame.X, frame.Y));
            context.Pop();
        }
    }
    private void DrawSettled(DrawingContext context, FormattedText text)
    {
        if (ActualWidth <= 0) return;
        if (text.WidthIncludingTrailingWhitespace <= ActualWidth) { context.DrawText(text, new Point()); return; }
        if (IsMouseOver)
        {
            double travel = text.WidthIncludingTrailingWhitespace + 75;
            double time = Math.Max(0, Clock.Elapsed.TotalSeconds - hoverAt);
            double cycle = time % (travel / 92 + 0.9);
            double offset = cycle <= 0.9 ? 0 : (cycle - 0.9) * 92;
            context.DrawText(text, new Point(-offset, 0));
            context.DrawText(text, new Point(travel - offset, 0));
        }
        else
        {
            if (clippedRun is null || clippedWidth != ActualWidth)
            {
                clippedRun ??= Format(Text);
                clippedRun.MaxTextWidth = ActualWidth;
                clippedRun.MaxLineCount = 1;
                clippedRun.Trimming = TextTrimming.CharacterEllipsis;
                clippedWidth = ActualWidth;
            }
            context.DrawText(clippedRun, new Point());
        }
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new LinePeer(this);
    private sealed class LinePeer(AnimatedLine owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetNameCore() => ((AnimatedLine)Owner).Text;
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
    }
    private sealed record Letter(FormattedText Text, double X);
    private sealed record Run(FormattedText Full, List<Letter> Letters);
}
