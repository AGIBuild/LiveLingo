using Avalonia;
using Velopack;

namespace LiveLingo.Desktop;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var path = Path.Combine(Path.GetTempPath(), "livelingo-crash.log");
            File.AppendAllText(path, $"\n[{DateTime.Now:O}] UnhandledException:\n{e.ExceptionObject}\n");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            var path = Path.Combine(Path.GetTempPath(), "livelingo-crash.log");
            File.AppendAllText(path, $"\n[{DateTime.Now:O}] UnobservedTaskException:\n{e.Exception}\n");
            e.SetObserved();
        };

        VelopackApp.Build().Run();

        if (OperatingSystem.IsMacOS())
            SetMacAgentMode();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private static void SetMacAgentMode()
    {
        var nsApp = Platform.macOS.MacNativeMethods.objc_getClass("NSApplication");
        var shared = Platform.macOS.MacNativeMethods.objc_msgSend(
            nsApp, Platform.macOS.MacNativeMethods.sel_registerName("sharedApplication"));
        // NSApplicationActivationPolicyAccessory = 1 → hides Dock icon, menu-bar only
        Platform.macOS.MacNativeMethods.objc_msgSend(
            shared, Platform.macOS.MacNativeMethods.sel_registerName("setActivationPolicy:"), (IntPtr)1);
    }
}
