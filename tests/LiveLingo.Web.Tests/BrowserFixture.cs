using System.Text.Json.Nodes;
using LiveLingo.Desktop.Web;
using Microsoft.Playwright;

[assembly: AssemblyFixture(typeof(LiveLingo.Web.Tests.BrowserFixture))]

namespace LiveLingo.Web.Tests;

/// <summary>
/// One Playwright instance per test run with both engines the app can run on:
/// WebKit (WKWebView on macOS) and Chromium (WebView2 on Windows).
/// </summary>
public sealed class BrowserFixture : IAsyncLifetime
{
    /// <summary>The page the window gives the web view: the 620 px panel plus its 18 px shadow margin on both sides.</summary>
    public const int NarrowWidth = 656;
    public const int WideWidth = 976;

    private IPlaywright? _playwright;
    private readonly Dictionary<string, IBrowser> _browsers = [];

    public async ValueTask InitializeAsync()
    {
        // Idempotent: downloads the engines the first time and is a no-op afterwards.
        var exit = Program.Main(["install", "webkit", "chromium"]);
        if (exit != 0)
            throw new InvalidOperationException($"Playwright browser install failed with exit code {exit}");

        _playwright = await Playwright.CreateAsync();
        _browsers["webkit"] = await _playwright.Webkit.LaunchAsync();
        _browsers["chromium"] = await _playwright.Chromium.LaunchAsync();
    }

    /// <summary>Opens the page without any snapshot: C# has not answered <c>ready</c> yet.</summary>
    public Task<ComposerPage> OpenBareAsync(string engine, int width = NarrowWidth, ColorScheme scheme = ColorScheme.Light) =>
        OpenBareAsync(engine, WebPage.ComposerEntry, width, 900, scheme);

    private async Task<ComposerPage> OpenBareAsync(string engine, string entry, int width, int height, ColorScheme scheme)
    {
        var page = await _browsers[engine].NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            ColorScheme = scheme,
        });
        var session = new ComposerPage(page, entry == WebPage.ComposerEntry ? "composer" : "settings");
        // The host injects invokeCSharpAction into every page; here it records what the page would have sent.
        await page.AddInitScriptAsync("window.__raw = []; window.invokeCSharpAction = (s) => { window.__raw.push(s); };");
        // Served from the base URI the host uses, so the page runs with the same origin and the init script applies.
        await page.RouteAsync(ComposerPage.BaseUri, route => route.FulfillAsync(new RouteFulfillOptions
        {
            ContentType = "text/html; charset=utf-8",
            Body = WebPage.Build(WebPageTests.Nonce, entry),
        }));
        await page.GotoAsync(ComposerPage.BaseUri);
        await session.WaitForIntentAsync("ready");
        return session;
    }

    /// <summary>Opens the page and plays the host's part of the handshake: strings, then the first snapshot.</summary>
    public async Task<ComposerPage> OpenAsync(string engine, string fixture = "empty", int width = NarrowWidth,
        ColorScheme scheme = ColorScheme.Light)
    {
        var session = await OpenBareAsync(engine, width, scheme);
        await session.PushStringsAsync();
        await session.PushAsync(fixture);
        return session;
    }

    /// <summary>The settings page without any snapshot.</summary>
    public Task<ComposerPage> OpenBareSettingsAsync(string engine) =>
        OpenBareAsync(engine, WebPage.SettingsEntry, 920, 660, ColorScheme.Light);

    /// <summary>The settings window: 920 x 660, opened like the composer with the strings and a golden snapshot.</summary>
    public async Task<ComposerPage> OpenSettingsAsync(string engine, string fixture = "settings-general", ColorScheme scheme = ColorScheme.Light)
    {
        var session = await OpenBareAsync(engine, WebPage.SettingsEntry, 920, 660, scheme);
        await session.PushStringsAsync("settings-strings");
        await session.PushAsync(fixture);
        return session;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var browser in _browsers.Values)
            await browser.CloseAsync();
        _playwright?.Dispose();
    }
}

/// <summary>An open page, the host messages it receives and everything it reported while it ran.</summary>
public sealed class ComposerPage : IAsyncDisposable
{
    private readonly string _stateType;
    private int _rev;

    /// <param name="stateType">The message type of this page's snapshots: "composer" or "settings".</param>
    public ComposerPage(IPage page, string stateType)
    {
        Page = page;
        _stateType = stateType;
        page.Console += (_, m) =>
        {
            if (m.Type == "error")
                ConsoleErrors.Add(m.Text);
        };
        page.PageError += (_, e) => ConsoleErrors.Add(e);
        page.Request += (_, r) =>
        {
            if (r.Url != BaseUri)
                Requests.Add(r.Url);
        };
    }

    public const string BaseUri = "https://livelingo.invalid/";

    public IPage Page { get; }
    public List<string> ConsoleErrors { get; } = [];
    public List<string> Requests { get; } = [];

    public static string FixtureText(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".json"));

    /// <summary>A label of the strings message, so tests name controls the way the user sees them.</summary>
    public static string Str(string key, string fixture = "strings") =>
        JsonNode.Parse(FixtureText(fixture))!["strings"]![key]!.GetValue<string>();

    /// <summary>A label of the settings page.</summary>
    public static string SettingsStr(string key) => Str(key, "settings-strings");

    public Task PushStringsAsync(string fixture = "strings")
    {
        var data = JsonNode.Parse(FixtureText(fixture))!;
        return ReceiveAsync("strings", data);
    }

    /// <summary>Pushes a golden snapshot with the next revision; <paramref name="edit"/> may adjust it first.</summary>
    public Task PushAsync(string fixture, Action<JsonObject>? edit = null)
    {
        var snapshot = JsonNode.Parse(FixtureText(fixture))!.AsObject();
        edit?.Invoke(snapshot);
        snapshot["rev"] = ++_rev;
        return ReceiveAsync(_stateType, snapshot);
    }

    public Task ReceiveAsync(string type, JsonNode? data)
    {
        var envelope = new JsonObject { ["v"] = 1, ["type"] = type, ["data"] = data };
        return Page.EvaluateAsync("json => LL.receive(json)", envelope.ToJsonString());
    }

    /// <summary>Everything the page sent since the last <see cref="ClearIntentsAsync"/>, parsed by the real host parser.</summary>
    internal async Task<IReadOnlyList<WebIntent>> IntentsAsync()
    {
        var raw = await Page.EvaluateAsync<string[]>("window.__raw");
        return [.. raw.Select(json =>
            WebIntent.Parse(json, out var reason) ?? throw new InvalidOperationException($"The host would reject {json}: {reason}"))];
    }

    /// <summary>
    /// Forgets what the page sent so far. The page measures its size in an animation frame, so a report that a render
    /// has queued would otherwise arrive after the clearing and look like the reaction to the next thing a test does.
    /// </summary>
    public async Task ClearIntentsAsync()
    {
        await Page.EvaluateAsync("() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done)))");
        await Page.EvaluateAsync("window.__raw.length = 0");
    }

    internal async Task<T> LastIntentAsync<T>() where T : WebIntent => (await IntentsAsync()).OfType<T>().Last();

    internal async Task<int> CountAsync<T>() where T : WebIntent => (await IntentsAsync()).OfType<T>().Count();

    public Task WaitForIntentAsync(string name) =>
        Page.WaitForFunctionAsync("name => window.__raw.some(s => JSON.parse(s).name === name)", name);

    public async ValueTask DisposeAsync() => await Page.CloseAsync();
}
