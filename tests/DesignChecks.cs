using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using GlassPlayer;

internal static class DesignChecks
{
    public static void Run(MainWindow window)
    {
        Check(PlayerDesigns.Parse(null) == PlayerDesign.Classic && PlayerDesigns.Parse("future") == PlayerDesign.Classic, "older settings and unknown designs preserve the original appearance");
        var preferences = typeof(MainWindow).GetNestedType("Position", System.Reflection.BindingFlags.NonPublic)!;
        var old = JsonSerializer.Deserialize("{\"Left\":100,\"Top\":100}", preferences)!;
        Check((string)preferences.GetProperty("Design")!.GetValue(old)! == "Classic", "existing preference files default to the original design");
        var saved = JsonSerializer.Deserialize("{\"Left\":100,\"Top\":100,\"Design\":\"LiquidGlass\"}", preferences)!;
        Check(JsonSerializer.Serialize(saved, preferences).Contains("LiquidGlass"), "liquid design survives preference serialization");
        window.SetDesign(PlayerDesign.LiquidGlass);
        var shader = (GlassEffect)window.FindName("GlassShader");
        Check(shader.Liquid == 1 && ((System.Windows.Controls.Grid)window.FindName("Disc")).Clip is RectangleGeometry, "liquid mode applies its shader and rounded square artwork");
        window.SetAppearance("Glacier", 1);
        Check(window.IsLiquidGlass && window.CurrentTheme.Name == "Glacier", "themes and scale do not reset the selected design");
        window.SetLanguage("en");
        Check((string)Application.Current.Resources["UiOriginalDesign"] == "Original", "design selector is localized in English");
        window.SetLanguage("ru");

        var pattern = new DrawingGroup();
        using (var dc = pattern.Open())
        {
            dc.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0, 0, 600, 220));
            for (int i = 0; i < 60; i++) dc.DrawRectangle(i % 2 == 0 ? Brushes.CornflowerBlue : Brushes.IndianRed, null, new Rect(i * 10, 0, 5, 220));
        }
        var scene = new Window { Left = 350, Top = 350, Width = 600, Height = 220, WindowStyle = WindowStyle.None, ShowInTaskbar = false, Topmost = true, Background = new DrawingBrush(pattern) };
        scene.Show();
        window.Left = 380; window.Top = 400;
        window.Show(); window.Activate();
        Pump(650);
        IntPtr material = FindMaterial();
        Check(material != IntPtr.Zero && IsWindowVisible(material), "liquid mode creates a visible native blur surface");
        var clip = CreateRectRgn(0, 0, 0, 0);
        int clipKind = GetWindowRgn(material, clip);
        bool outside = !PtInRegion(clip, 2, 2);
        DeleteObject(clip);
        Check(clipKind == 3 && outside, "native material is clipped to the capsule instead of a rectangular panel");
        string directory = System.IO.Path.GetFullPath(".tools");
        System.IO.Directory.CreateDirectory(directory);
        var topLeft = window.PointToScreen(new Point());
        var bottomRight = window.PointToScreen(new Point(window.ActualWidth, window.ActualHeight));
        System.Drawing.Color corner;
        using (var bitmap = new System.Drawing.Bitmap((int)(bottomRight.X - topLeft.X), (int)(bottomRight.Y - topLeft.Y)))
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen((int)topLeft.X, (int)topLeft.Y, 0, 0, bitmap.Size);
            corner = bitmap.GetPixel(13, 13);
            bitmap.Save(System.IO.Path.Combine(directory, "liquid-pattern-check.png"));
        }
        window.Hide(); Pump(50);
        using (var pixel = new System.Drawing.Bitmap(1, 1))
        using (var graphics = System.Drawing.Graphics.FromImage(pixel))
        {
            graphics.CopyFromScreen((int)topLeft.X + 13, (int)topLeft.Y + 13, 0, 0, pixel.Size);
            var background = pixel.GetPixel(0, 0);
            Check(Math.Abs(background.R - corner.R) + Math.Abs(background.G - corner.G) + Math.Abs(background.B - corner.B) < 20, "native blur leaves the desktop outside curved corners untouched");
        }
        window.Show(); Pump(50);
        GetWindowRect(material, out var before);
        window.Left += 60;
        Pump(100);
        GetWindowRect(material, out var after);
        Check(after.Left > before.Left, "native glass follows player movement");
        window.Hide(); Pump(50);
        Check(!IsWindowVisible(material), "hiding the player also hides its native glass");
        window.Show(); Pump(50);
        Check(IsWindowVisible(material), "restoring the player restores the glass");
        window.SetDesign(PlayerDesign.Classic); Pump(50);
        Check(!IsWindowVisible(material) && shader.Liquid == 0 && ((System.Windows.Controls.Grid)window.FindName("Disc")).Clip is EllipseGeometry, "switching back restores vinyl and removes the liquid backdrop");
        window.SetDesign(PlayerDesign.LiquidGlass); Pump(50);
        Check(FindMaterial() == material && IsWindowVisible(material), "switching designs reuses one backdrop without duplicates");
        window.Close(); Pump(50);
        Check(FindMaterial() == IntPtr.Zero, "closing the player destroys the native glass surface");
        scene.Close();
        Console.WriteLine("All 15 design checks passed.");
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static IntPtr FindMaterial()
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint pid);
            var title = new StringBuilder(100);
            GetWindowText(window, title, title.Capacity);
            if (pid == Environment.ProcessId && title.ToString() == "Glass Player material") result = window;
            return true;
        }, IntPtr.Zero);
        return result;
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    private delegate bool Callback(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window, IntPtr region);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(IntPtr region, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
}
