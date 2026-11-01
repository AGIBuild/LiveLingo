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

    public async Task<PageSession> OpenAsync(string engine, string nonce)
    {
        var page = await _browsers[engine].NewPageAsync();
        var session = new PageSession(page);
        await page.SetContentAsync(WebPage.Build(nonce));
        return session;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var browser in _browsers.Values)
            await browser.CloseAsync();
        _playwright?.Dispose();
    }
}

/// <summary>An open page plus everything the page reported while it ran.</summary>
public sealed class PageSession : IAsyncDisposable
{
    public PageSession(IPage page)
    {
        Page = page;
        page.Console += (_, m) =>
        {
            if (m.Type == "error")
                ConsoleErrors.Add(m.Text);
        };
        page.PageError += (_, e) => ConsoleErrors.Add(e);
        page.Request += (_, r) => Requests.Add(r.Url);
    }

    public IPage Page { get; }
    public List<string> ConsoleErrors { get; } = [];
    public List<string> Requests { get; } = [];

    public async ValueTask DisposeAsync() => await Page.CloseAsync();
}
