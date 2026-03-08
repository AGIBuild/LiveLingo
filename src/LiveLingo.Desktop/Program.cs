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

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
