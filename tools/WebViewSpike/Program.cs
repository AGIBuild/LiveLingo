using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
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
    public static void Main(string[] args)
    {
        // --headless runs under the Avalonia headless platform (what a CI test host would use), to see which web adapter the control picks.
        var builder = AppBuilder.Configure<SpikeApp>();
        (args.Contains("--headless")
            ? builder.UseHeadless(new Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
            : builder.UsePlatformDetect()).StartWithClassicDesktopLifetime(args);
    }
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
          // S8c: report every key and IME event, so the console shows what WKWebView sends when a pinyin candidate is confirmed with Enter.
          const i = document.getElementById('i');
          const send = o => invokeCSharpAction(JSON.stringify(o));
          i.addEventListener('keydown', e => send({intent: e.key === 'Enter' ? 'enter' : 'keydown', key: e.key, code: e.code, keyCode: e.keyCode, composing: e.isComposing}));
          for (const t of ['compositionstart', 'compositionend']) i.addEventListener(t, e => send({intent: t, data: e.data}));
          window.__ll = { receive(json) { document.getElementById('o').textContent = json; } };
        </script>
        """;

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        var args = Environment.GetCommandLineArgs();
        var auto = args.Contains("--auto");
        var bench = args.Contains("--bench");
        var headless = args.Contains("--headless");
        var webKitBefore = WebProcesses().Select(p => p.Id).ToHashSet();
        var web = new NativeWebView { Background = Brushes.Transparent };
        var received = new List<string>();
        web.WebMessageReceived += (_, e) =>
        {
            received.Add(e.Body ?? "");
            Console.WriteLine("from web: " + e.Body);
        };
        var adapterCreated = false;
        web.AdapterCreated += (_, _) =>
        {
            adapterCreated = true;
            Console.WriteLine("adapter: " + web.AdapterInfo);
            Dispatcher.UIThread.Post(() => web.NavigateToString(Html, new Uri("https://livelingo.invalid/")));
        };
        Window? window = null;
        web.NavigationCompleted += async (_, e) =>
        {
            Console.WriteLine("navigated ok=" + e.IsSuccess);
            if (headless)
                await RunHeadlessProbeAsync(web, received);
            else if (auto)
                await RunChecksAsync(web, received);
            else if (bench)
                await RunBenchAsync(window!, web, received, webKitBefore);
        };

        window = new Window
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
        if (headless)
            _ = Task.Delay(3000).ContinueWith(_ => Dispatcher.UIThread.Post(async () =>
            {
                Console.WriteLine($"headless diag: AdapterInfo={web.AdapterInfo?.ToString() ?? "<null>"} loaded={web.IsLoaded} adapterCreated={adapterCreated}");
                try
                {
                    web.NavigateToString(Html, new Uri("https://livelingo.invalid/"));
                    var invoke = web.InvokeScript("document.getElementById('t').textContent");
                    var done = await Task.WhenAny(invoke, Task.Delay(3000));
                    Console.WriteLine("headless InvokeScript: " + (done == invoke ? "returned " + (await invoke ?? "<null>") : "timed out"));
                }
                catch (Exception ex)
                {
                    Console.WriteLine("headless diag threw: " + ex.GetType().Name + ": " + ex.Message);
                }
                Shutdown();
            }));
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

        Shutdown();
    }

    // S8g: does the Headless adapter run the page, or is it a stub that needs a caller-supplied script engine?
    private static async Task RunHeadlessProbeAsync(NativeWebView web, List<string> received)
    {
        Console.WriteLine("adapter: " + web.AdapterInfo);
        try
        {
            var title = await web.InvokeScript("document.getElementById('t').textContent");
            Console.WriteLine("headless InvokeScript returned: " + (title ?? "<null>"));
            Report("Headless runs the page's DOM", title?.Contains("LiveLingo spike") == true);
        }
        catch (Exception ex)
        {
            Console.WriteLine("headless InvokeScript threw: " + ex.GetType().Name + ": " + ex.Message);
            Report("Headless runs the page's DOM", false);
        }
        Shutdown();
    }

    // S8d / S8e: show -> first frame with the textarea focused, after the window was hidden; plus the web processes' memory.
    private static async Task RunBenchAsync(Window window, NativeWebView web, List<string> received, HashSet<int> webKitBefore)
    {
        // --park keeps the window alive and shown, only transparent and off screen (the fallback in the design's section 8).
        var park = Environment.GetCommandLineArgs().Contains("--park");
        var hideMs = int.TryParse(Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--hide-ms="))?[10..], out var h) ? h : 400;
        var home = window.Position;
        var scriptMs = new List<double>();
        double lastScriptMs = 0;
        void Hide()
        {
            if (!park) { window.Hide(); return; }
            window.Opacity = 0;
            window.Position = new PixelPoint(-20000, -20000);
        }
        async Task<double> ShowAndWaitAsync()
        {
            var before = received.Count;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (park) { window.Position = home; window.Opacity = 1; }
            else window.Show();
            window.Activate();
            web.Focus();
            await web.InvokeScript("void (document.getElementById('i').focus(), requestAnimationFrame(() => invokeCSharpAction('frame')))");
            lastScriptMs = sw.Elapsed.TotalMilliseconds;
            while (received.Count == before || received[^1] != "frame")
                await Task.Delay(1);
            return sw.Elapsed.TotalMilliseconds;
        }

        var cold = await ShowAndWaitAsync();
        Console.WriteLine($"first frame after navigation: {cold:F0} ms");
        await Task.Delay(1000);
        var samples = new List<double>();
        for (var i = 0; i < 30; i++)
        {
            Hide();
            await Task.Delay(hideMs);
            samples.Add(await ShowAndWaitAsync());
            scriptMs.Add(lastScriptMs);
            await Task.Delay(200);
        }
        Console.WriteLine("focus script done at (ms): " + string.Join(' ', scriptMs.Select(s => s.ToString("F0"))));
        Console.WriteLine("samples in order (ms): " + string.Join(' ', samples.Select(s => s.ToString("F0"))));
        samples.Sort();
        Console.WriteLine($"hide->show to first frame (n=30): p50={samples[15]:F0} ms  p95={samples[28]:F0} ms  max={samples[^1]:F0} ms");
        var focus = await web.InvokeScript("document.activeElement.id + ' hasFocus=' + document.hasFocus()");
        Console.WriteLine("after show: activeElement=" + focus);

        var own = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1048576;
        Console.WriteLine($"app process: {own} MB");
        foreach (var p in WebProcesses().Where(p => !webKitBefore.Contains(p.Id)))
            Console.WriteLine($"new web process {p.ProcessName} pid={p.Id}: {p.WorkingSet64 / 1048576} MB");
        Shutdown();
    }

    // WKWebView helpers live in their own XPC processes; WebView2 spawns msedgewebview2.
    private static IEnumerable<System.Diagnostics.Process> WebProcesses() =>
        System.Diagnostics.Process.GetProcesses().Where(p => p.ProcessName.StartsWith("com.apple.WebKit") || p.ProcessName.StartsWith("msedgewebview2"));

    private static void Shutdown() =>
        Dispatcher.UIThread.Post(() => ((IClassicDesktopStyleApplicationLifetime)Current!.ApplicationLifetime!).Shutdown());

    private static void Report(string name, bool ok) => Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
}
