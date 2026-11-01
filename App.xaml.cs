namespace GlassPlayer;
public partial class App : System.Windows.Application
{
    private Mutex? singleInstance;
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        Localization.SetLanguage("ru");
        if (!e.Args.Contains("--demo"))
        {
            singleInstance = new Mutex(true, "Local\\GlassPlayer.Desktop", out bool created);
            if (!created)
            {
                var window = FindWindow(null, "Glass Player");
                if (window != IntPtr.Zero) { ShowWindow(window, 5); SetForegroundWindow(window); }
                Shutdown();
                return;
            }
        }
        base.OnStartup(e);
    }
    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        singleInstance?.Dispose();
        base.OnExit(e);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string title);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
}
