using System.Numerics;
using System.Runtime.InteropServices;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace GlassPlayer;

internal sealed class CompositionGlass : IDisposable
{
    private readonly Compositor compositor;
    private readonly DesktopWindowTarget target;
    private readonly SpriteVisual visual;
    private readonly CompositionRoundedRectangleGeometry geometry;
    private readonly CompositionGeometricClip clip;
    private readonly CompositionBackdropBrush brush;
    private static IntPtr queueController;

    public CompositionGlass(IntPtr window)
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is null)
        {
            var options = new QueueOptions { Size = Marshal.SizeOf<QueueOptions>(), Thread = 2, Apartment = 2 };
            Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out queueController));
        }
        compositor = new Compositor();
        var interop = compositor.As<ICompositorDesktopInterop>();
        interop.CreateDesktopWindowTarget(window, true, out var raw);
        try { target = MarshalInspectable<DesktopWindowTarget>.FromAbi(raw); }
        finally { Marshal.Release(raw); }
        brush = compositor.CreateHostBackdropBrush();
        geometry = compositor.CreateRoundedRectangleGeometry();
        clip = compositor.CreateGeometricClip(geometry);
        visual = compositor.CreateSpriteVisual();
        visual.Brush = brush;
        visual.Clip = clip;
        target.Root = visual;
    }
    public void Resize(int width, int height, int radius)
    {
        visual.Size = geometry.Size = new Vector2(width, height);
        geometry.CornerRadius = new Vector2(radius);
    }
    public void Dispose()
    {
        target.Root = null;
        target.Dispose(); visual.Dispose(); clip.Dispose(); geometry.Dispose(); brush.Dispose(); compositor.Dispose();
    }
    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICompositorDesktopInterop
    {
        void CreateDesktopWindowTarget(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool topmost, out IntPtr target);
    }
    [StructLayout(LayoutKind.Sequential)] private struct QueueOptions { public int Size, Thread, Apartment; }
    [DllImport("CoreMessaging.dll")] private static extern int CreateDispatcherQueueController(QueueOptions options, out IntPtr controller);
}
