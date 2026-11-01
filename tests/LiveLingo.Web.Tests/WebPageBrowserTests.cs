namespace LiveLingo.Web.Tests;

public sealed class WebPageBrowserTests(BrowserFixture browsers)
{
    public static TheoryData<string> Engines => new() { "webkit", "chromium" };

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Page_RendersTheRevOfTheLatestMessage(string engine)
    {
        await using var session = await browsers.OpenAsync(engine, WebPageTests.Nonce);

        await session.Page.EvaluateAsync("void LL.receive(JSON.stringify({ rev: 7 }))");

        Assert.Equal("rev 7", await session.Page.Locator("#rev").InnerTextAsync());
        Assert.Empty(session.ConsoleErrors);
        Assert.Empty(session.Requests);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Csp_BlocksScriptWithoutTheNonce(string engine)
    {
        await using var session = await browsers.OpenAsync(engine, WebPageTests.Nonce);

        // Playwright's own evaluate bypasses the page CSP, so eval() cannot be probed here; the absence of
        // 'unsafe-eval' is asserted on the policy string in WebPageTests.
        var injected = await session.Page.EvaluateAsync<string>(
            "() => { const s = document.createElement('script'); s.textContent = 'window.__injected = 1'; document.body.append(s); return String(window.__injected); }");

        Assert.Equal("undefined", injected);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Csp_BlocksNetworkRequests(string engine)
    {
        await using var session = await browsers.OpenAsync(engine, WebPageTests.Nonce);

        var outcome = await session.Page.EvaluateAsync<string>(
            "async () => { try { await fetch('https://example.org/x'); return 'sent'; } catch (e) { return 'blocked'; } }");

        Assert.Equal("blocked", outcome);
    }
}
