using System;
using System.Linq;
using Avalonia;
using LiveLingo.App.Services.Platform.Windows;

namespace LiveLingo.App;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--test-inject"))
        {
            InjectionTest.Run();
            return;
        }

        if (args.Contains("--diag-window"))
        {
            var idx = Array.IndexOf(args, "--diag-window");
            var filter = idx + 1 < args.Length ? args[idx + 1] : null;
            WindowDiagnostic.Run(filter);
            return;
        }

        if (args.Contains("--test-slack"))
        {
            SlackAutoTest.Run();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
