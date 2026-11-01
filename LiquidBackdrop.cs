using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace GlassPlayer;

// A nonactivating compositor surface below the layered WPF window. Keeping the
// backdrop separate lets Windows blur other apps without capturing the desktop.
internal sealed class LiquidBackdrop : IDisposable
{
    private MaterialSurface? surface;
    private CompositionGlass? composition;
    private readonly MainWindow player;
    private (int X, int Y, int W, int H, int Radius, bool Light)? lastBounds;
    public bool IsAvailable { get; private set; }
    public LiquidBackdrop(MainWindow player) { this.player = player; }

    public void Update(bool force = false)
    {
        var owner = new WindowInteropHelper(player).Handle;
        if (owner == IntPtr.Zero) return;
        if (!player.IsVisible || !player.IsLiquidGlass || player.WindowState == WindowState.Minimized)
        {
            if (surface is not null) ShowWindow(surface.Handle, 0);
            lastBounds = null;
            return;
        }
        var dpi = VisualTreeHelper.GetDpi(player);
        if (!GetWindowRect(owner, out var bounds)) return;
        int padX = (int)Math.Round(12 * player.PlayerScale * dpi.DpiScaleX);
        int padY = (int)Math.Round(12 * player.PlayerScale * dpi.DpiScaleY);
        int width = Math.Max(1, bounds.Right - bounds.Left - 2 * padX);
        int height = Math.Max(1, bounds.Bottom - bounds.Top - 2 * padY);
        int radius = (int)Math.Round(Math.Min(58, player.ActualHeight / player.PlayerScale / 2 - 12) * player.PlayerScale * dpi.DpiScaleX);
        bool light = player.CurrentTheme.Name == "Porcelain";
        var next = (bounds.Left + padX, bounds.Top + padY, width, height, radius, light);
        if (!force && next == lastBounds) return;
        if (surface is null)
        {
            surface = new MaterialSurface(width, height);
            try
            {
                ApplyMaterial(surface.Handle, light);
                composition = new CompositionGlass(surface.Handle);
                IsAvailable = true;
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or NotSupportedException)
            {
                System.Diagnostics.Debug.WriteLine("Liquid backdrop: " + ex.Message);
                IsAvailable = false;
            }
        }
        if (lastBounds is null || lastBounds.Value.Light != light)
        {
            if (!IsAvailable) { ShowWindow(surface.Handle, 0); return; }
        }
        composition?.Resize(width, height, radius);
        // Insert directly below the player; this follows its topmost band without activation.
        SetWindowPos(surface.Handle, owner, next.Item1, next.Item2, width, height, 0x0010 | 0x0040 | 0x0200);
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
        if (SetWindowRgn(surface.Handle, region, true) == 0) DeleteObject(region);
        lastBounds = next;
    }
    private static bool ApplyMaterial(IntPtr window, bool light)
    {
        int dark = light ? 0 : 1;
        DwmSetWindowAttribute(window, 20, ref dark, sizeof(int));
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            int enabled = 1;
            if (DwmSetWindowAttribute(window, 17, ref enabled, sizeof(int)) == 0) return true;
        }
        // Windows 10 doesn't expose the newer system-backdrop attribute.
        if (SetComposition is null) return false;
        var accent = new Accent { State = 5 };
        IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf<Accent>());
        try
        {
            Marshal.StructureToPtr(accent, memory, false);
            var data = new CompositionData { Attribute = 19, Data = memory, Size = Marshal.SizeOf<Accent>() };
            return SetComposition(window, ref data);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    private sealed class MaterialSurface : System.Windows.Forms.NativeWindow, IDisposable
    {
        public MaterialSurface(int width, int height) => CreateHandle(new System.Windows.Forms.CreateParams
        {
            Caption = "Glass Player material", Width = width, Height = height,
            Style = unchecked((int)0x80000000), ExStyle = 0x082000A0,
        });
        protected override void WndProc(ref System.Windows.Forms.Message message)
        {
            if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; }
            if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; }
            if (message.Msg == 0x0014) { message.Result = new IntPtr(1); return; }
            base.WndProc(ref message);
        }
        public void Dispose() => DestroyHandle();
    }
    public void Dispose() { composition?.Dispose(); composition = null; surface?.Dispose(); surface = null; IsAvailable = false; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Accent { public int State, Flags; public uint Color; public int Animation; }
    [StructLayout(LayoutKind.Sequential)] private struct CompositionData { public int Attribute; public IntPtr Data; public int Size; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] [return: MarshalAs(UnmanagedType.Bool)] private delegate bool SetCompositionDelegate(IntPtr window, ref CompositionData data);
    private static readonly SetCompositionDelegate? SetComposition = LoadComposition();
    private static SetCompositionDelegate? LoadComposition() => NativeLibrary.TryLoad("user32.dll", out var library) && NativeLibrary.TryGetExport(library, "SetWindowCompositionAttribute", out var function) ? Marshal.GetDelegateForFunctionPointer<SetCompositionDelegate>(function) : null;
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
