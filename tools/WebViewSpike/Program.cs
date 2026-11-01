using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

// Usage:
//   dotnet run --project tools/WebViewSpike            visual mode: look at the window, type Chinese with an IME, press the button
//   dotnet run --project tools/WebViewSpike -- --auto  bridge mode: runs the scripted checks, prints PASS/FAIL, exits
internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        AppBuilder.Configure<SpikeApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
}

internal sealed class SpikeApp : Application
{
    // The page is the panel of the prototype: transparent body, rounded translucent panel, shadow inside an 18 px margin.
    private const string Html = """
        <!doctype html><meta charset="utf-8">
        <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src https://example.com">
        <style>
          html,body{margin:0;background:transparent;font:14px -apple-system,system-ui,'Segoe UI'}
          .p{margin:18px;height:240px;border-radius:22px;background:rgba(255,255,255,.86);border:1px solid rgba(0,0,0,.1);
             box-shadow:0 24px 64px rgba(20,30,60,.35);padding:16px;box-sizing:border-box}
          textarea{width:100%;height:90px;font:16px inherit;border:1px solid #ccc;border-radius:10px;padding:8px;box-sizing:border-box}
        </style>
        <div class="p"><b id="t">LiveLingo spike</b>
          <textarea id="i" autofocus placeholder="type here; test a Chinese IME and Enter to confirm a candidate"></textarea>
          <button onclick="invokeCSharpAction(JSON.stringify({intent:'click'}))">send</button>
          <span id="o"></span>
        </div>
        <script>
          document.getElementById('i').addEventListener('keydown', e => {
            if (e.key === 'Enter') invokeCSharpAction(JSON.stringify({intent:'enter', composing: e.isComposing}));
          });
          window.__ll = { receive(json) { document.getElementById('o').textContent = json; } };
        </script>
        """;

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        var auto = Environment.GetCommandLineArgs().Contains("--auto");
        var web = new NativeWebView { Background = Brushes.Transparent };
        var received = new List<string>();
        web.WebMessageReceived += (_, e) =>
        {
            received.Add(e.Body ?? "");
            Console.WriteLine("from web: " + e.Body);
        };
        web.AdapterCreated += (_, _) =>
        {
            Console.WriteLine("adapter: " + web.AdapterInfo);
            Dispatcher.UIThread.Post(() => web.NavigateToString(Html, new Uri("https://livelingo.invalid/")));
        };
        web.NavigationCompleted += async (_, e) =>
        {
            Console.WriteLine("navigated ok=" + e.IsSuccess);
            if (auto)
                await RunChecksAsync(web, received);
        };

        var window = new Window
        {
            Width = 656, Height = 276, Position = new PixelPoint(300, 200),
            WindowDecorations = WindowDecorations.None,
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Topmost = true,
            ShowInTaskbar = false,
            Content = web
        };
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = window;
        window.Show();
        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RunChecksAsync(NativeWebView web, List<string> received)
    {
        // C# -> JS: a script that returns a primitive works; one that returns a Promise throws on macOS, so wrap with void().
        var title = await web.InvokeScript("document.getElementById('t').textContent");
        Report("InvokeScript returns the value", title?.Contains("LiveLingo spike") == true);

        await web.InvokeScript("void window.__ll.receive(JSON.stringify({rev:1}))");
        var echoed = await web.InvokeScript("document.getElementById('o').textContent");
        Report("C# -> JS state message", echoed?.Contains("rev") == true);

        // JS -> C#: the helper is only available in pages loaded through the control.
        await web.InvokeScript("void invokeCSharpAction(JSON.stringify({intent:'ping'}))");
        await Task.Delay(300);
        Report("JS -> C# message", received.Any(m => m.Contains("ping")));

        // The CSP only allows https://example.com; a request to any other host must raise a CSP violation.
        await web.InvokeScript("void (document.addEventListener('securitypolicyviolation', e => invokeCSharpAction('csp-violation:' + e.blockedURI)), fetch('https://example.org/x').catch(() => {}))");
        await Task.Delay(1500);
        Report("CSP blocks other hosts", received.Any(m => m.StartsWith("csp-violation:")));

        Dispatcher.UIThread.Post(() => ((IClassicDesktopStyleApplicationLifetime)Current!.ApplicationLifetime!).Shutdown());
    }

    private static void Report(string name, bool ok) => Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
}
