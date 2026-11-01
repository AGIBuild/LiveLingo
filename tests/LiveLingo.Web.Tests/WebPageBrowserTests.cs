namespace LiveLingo.Web.Tests;

/// <summary>The page's security boundary, checked in both engines.</summary>
public sealed class WebPageBrowserTests(BrowserFixture browsers)
{
    public static TheoryData<string> Engines => new() { "webkit", "chromium" };

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Csp_BlocksScriptWithoutTheNonce(string engine)
    {
        await using var page = await browsers.OpenBareAsync(engine);

        // Playwright's own evaluate bypasses the page CSP, so eval() cannot be probed here; the absence of
        // 'unsafe-eval' is asserted on the policy string in WebPageTests.
        var injected = await page.Page.EvaluateAsync<string>(
            "() => { const s = document.createElement('script'); s.textContent = 'window.__injected = 1'; document.body.append(s); return String(window.__injected); }");

        Assert.Equal("undefined", injected);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Csp_BlocksNetworkRequests(string engine)
    {
        await using var page = await browsers.OpenBareAsync(engine);

        var outcome = await page.Page.EvaluateAsync<string>(
            "async () => { try { await fetch('https://example.org/x'); return 'sent'; } catch (e) { return 'blocked'; } }");

        Assert.Equal("blocked", outcome);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task HostText_IsNeverInterpretedAsMarkup(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "translate-ready");

        await page.PushAsync("translate-ready", s => s["result"]!["rows"]![0]!["text"] = "<img src=x onerror=\"window.__pwned=1\"><b>bold</b>");

        Assert.Equal(0, await page.Page.Locator("#rows img, #rows b").CountAsync());
        Assert.Contains("<b>bold</b>", await page.Page.Locator("#rows .row-text").First.InnerTextAsync());
        Assert.Empty(page.ConsoleErrors);
    }
}
