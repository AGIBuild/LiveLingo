using Avalonia;
using System.Threading;
using Velopack;

namespace LiveLingo.Desktop;

class Program
{
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    public static void Main(string[] args)
    {
        if (!TryAcquireSingleInstanceLock())
            return;

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

        try
        {
            VelopackApp.Build().Run();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            ReleaseSingleInstanceLock();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static bool TryAcquireSingleInstanceLock()
    {
        const string mutexName = "LiveLingo.Desktop.SingleInstance";
        _singleInstanceMutex = new Mutex(initiallyOwned: true, name: mutexName, createdNew: out var createdNew);
        return createdNew;
    }

    private static void ReleaseSingleInstanceLock()
    {
        if (_singleInstanceMutex is null)
            return;

        try
        {
            _singleInstanceMutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Already released; safe to ignore.
        }
        finally
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }
    }
}
