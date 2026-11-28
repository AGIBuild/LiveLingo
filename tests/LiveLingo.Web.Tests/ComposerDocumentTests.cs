using LiveLingo.Desktop.Web;

namespace LiveLingo.Web.Tests;

public sealed class ComposerDocumentTests(BrowserFixture browsers)
{
    [Theory]
    [InlineData("webkit")]
    [InlineData("chromium")]
    public async Task LongFinalText_PreservesParagraphsAndScrollsWithoutHidingWriteControls(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "document-ready");
        await page.Page.SetViewportSizeAsync(656, 500);
        await page.ReceiveAsync("frame", new System.Text.Json.Nodes.JsonObject { ["fixedHeight"] = true });
        var text = string.Join("\n\n", Enumerable.Repeat(
            "请在状态检查发现问题时提供提示，并允许用户手动修复。需要选择合适的交互位置，不能只提示而不解决问题。", 20));
        await page.PushAsync("document-ready", state => state["result"]!["document"]!["text"] = text);

        Assert.Equal(text, await page.Page.Locator("#final-text").TextContentAsync());
        Assert.True(await page.Page.Locator(".document-body").EvaluateAsync<bool>("el => el.scrollHeight > el.clientHeight"));
        var commit = await page.Page.Locator("#commit").BoundingBoxAsync();
        Assert.NotNull(commit);
        Assert.InRange(commit.Y + commit.Height, 1, 500);
        Assert.True(await page.Page.Locator("#commit").IsEnabledAsync());
        Assert.Empty(page.ConsoleErrors);
    }

    [Theory]
    [InlineData("webkit")]
    [InlineData("chromium")]
    public async Task FinalDocument_IsContinuousAndOriginalCanBeExpanded(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "document-ready", BrowserFixture.WideWidth);

        Assert.Contains("wide", await page.Page.Locator("#composer").GetAttributeAsync("class"));
        Assert.Equal(0, await page.Page.Locator("#res .unit").CountAsync());
        Assert.Equal("T:先检查缓存。然后更新索引。", await page.Page.Locator("#final-text").InnerTextAsync());
        Assert.True(await page.Page.Locator("#src").IsVisibleAsync());
        await page.Page.Locator("#edit-full").ClickAsync();
        Assert.Equal(1, await page.CountAsync<EditDocumentIntent>());

        // When switched to narrow stacked mode, the finished document collapses the original pane until expanded.
        await page.PushAsync("document-ready", state =>
        {
            state["layout"]!["wide"] = false;
            state["source"]!["collapsible"] = true;
        });
        Assert.False(await page.Page.Locator("#src").IsVisibleAsync());
        await page.Page.Locator(".source-toggle").ClickAsync();
        Assert.True(await page.Page.Locator("#src").IsVisibleAsync());
        Assert.Empty(page.ConsoleErrors);
    }

    [Theory]
    [InlineData("webkit")]
    [InlineData("chromium")]
    public async Task FullEditor_EnterInsertsNewline_CommandEnterOnlySaves_AndSnapshotsDoNotReplaceText(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "document-editing");
        var editor = page.Page.Locator("#edit-document");
        await editor.FillAsync("My edited text");
        await editor.PressAsync("End");
        await editor.PressAsync("Enter");
        Assert.Contains("\n", await editor.InputValueAsync());
        Assert.Equal(0, await page.CountAsync<SaveEditIntent>());
        await page.PushAsync("document-editing");
        Assert.StartsWith("My edited text", await editor.InputValueAsync());
        await page.ClearIntentsAsync();
        await editor.PressAsync("Meta+Enter");
        Assert.Equal(1, await page.CountAsync<SaveEditIntent>());
        Assert.Equal(0, await page.CountAsync<CommitIntent>());
        Assert.True(await page.Page.Locator("#commit").IsDisabledAsync());
        Assert.True(await page.Page.Locator("#copy").IsDisabledAsync());
    }

    [Theory]
    [InlineData("webkit")]
    [InlineData("chromium")]
    public async Task IncompleteFinal_OffersActualRepairsWithoutEnablingWrite(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "document-failed");
        Assert.Contains("全文处理未完成", await page.Page.Locator(".document-notice").InnerTextAsync());
        Assert.True(await page.Page.Locator("#commit").IsDisabledAsync());
        Assert.True(await page.Page.Locator("#copy").IsDisabledAsync());
        await page.Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = ComposerPage.Str("documentRetry") }).ClickAsync();
        Assert.Equal(1, await page.CountAsync<RetryDocumentIntent>());
        Assert.Equal(0, await page.Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = ComposerPage.Str("documentAccept") }).CountAsync());
        Assert.Contains("如果原话已经是目标语言", await page.Page.Locator(".document-notice").InnerTextAsync());
        await page.Page.Locator("#clean-original").ClickAsync();
        Assert.Equal(LiveLingo.Desktop.Composer.ComposerOutput.Clean, (await page.LastIntentAsync<OutputIntent>()).Value);
        await page.PushAsync("document-failed", state =>
        {
            state["result"]!["document"]!["canAcceptPreview"] = true;
            state["result"]!["document"]!["text"] = "完整的分段预览。";
        });
        await page.Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = ComposerPage.Str("documentAccept") }).ClickAsync();
        Assert.Equal(1, await page.CountAsync<AcceptPreviewIntent>());
    }

    [Theory]
    [InlineData("webkit")]
    [InlineData("chromium")]
    public async Task FullEditor_ImeConfirmDoesNotSaveOrWrite(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "document-editing");
        await page.ClearIntentsAsync();
        await page.Page.EvaluateAsync("""
            () => {
              const el = document.getElementById('edit-document');
              el.dispatchEvent(new CompositionEvent('compositionstart', { bubbles: true }));
              el.value = '中文候选';
              el.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', keyCode: 229, metaKey: true, bubbles: true }));
              el.dispatchEvent(new CompositionEvent('compositionend', { bubbles: true }));
            }
            """);
        Assert.Equal(0, await page.CountAsync<SaveEditIntent>());
        Assert.Equal(0, await page.CountAsync<CommitIntent>());
        Assert.Equal("中文候选", (await page.LastIntentAsync<EditTextIntent>()).Text);
    }

    [Theory]
    [InlineData("webkit")]
    [InlineData("chromium")]
    public async Task ScenarioPolishModes_RenderNonDestructiveStructuredHighlightsGateBadgeAndGroupedMenu(string engine)
    {
        await using var page = await browsers.OpenAsync(engine, "document-ai-task");

        Assert.Contains("AI 任务指令", await page.Page.Locator("#out-pill").InnerTextAsync());
        Assert.Contains("事实锚点已校验", await page.Page.Locator("#gate-badge").InnerTextAsync());
        var expectedText = "### 目标\n把 `AuthMiddleware` 的重试关掉。\n\n### 限制与护栏\n- 不要改动 `login.ts`";
        Assert.Equal(expectedText, await page.Page.Locator("#final-text").TextContentAsync());
        Assert.Equal(2, await page.Page.Locator("#final-text .md-h").CountAsync());
        Assert.Equal(2, await page.Page.Locator("#final-text .md-code").CountAsync());

        await page.Page.Locator("#out-pill").ClickAsync();
        Assert.Equal(1, await page.Page.Locator(".menu-group[role=group]").CountAsync());
        await page.Page.Locator(".menu button", new() { HasText = "待办与派单" }).ClickAsync();
        Assert.Equal("todo", (await page.LastIntentAsync<PolishModeIntent>()).Id);

        // Immediately upon switching modes while the LLM works, the result pane shows the generating badge & shimmer.
        await page.PushAsync("document-ai-task", state =>
        {
            state["result"]!["label"] = "将写入 · 待办与派单";
            state["result"]!["document"]!["state"] = "pending";
            state["result"]!["document"]!["gateBadge"] = "正在生成…";
            state["result"]!["document"]!["gateTone"] = "info";
            state["result"]!["document"]!["notice"] = "正在按「待办与派单」生成全文…";
        });
        Assert.Equal("正在生成…", (await page.Page.Locator("#gate-badge").InnerTextAsync()).Trim());
        Assert.Equal(1, await page.Page.Locator(".doc-generating").CountAsync());
        Assert.Contains("previewing", await page.Page.Locator("#final-text").GetAttributeAsync("class"));

        // Once SSE tokens start streaming, shimmer bars yield to crisp live-highlighted text + caret while keeping edit gated.
        await page.PushAsync("document-ai-task", state =>
        {
            state["result"]!["label"] = "将写入 · 待办与派单";
            state["result"]!["document"]!["state"] = "pending";
            state["result"]!["document"]!["streaming"] = true;
            state["result"]!["document"]!["text"] = "- [ ] 先排查 `AuthMiddleware`";
            state["result"]!["document"]!["gateBadge"] = "正在生成…";
            state["result"]!["document"]!["gateTone"] = "info";
            state["result"]!["document"]!["notice"] = "正在按「待办与派单」生成全文…";
        });
        Assert.Equal(0, await page.Page.Locator(".doc-generating").CountAsync());
        Assert.Contains("streaming", await page.Page.Locator("#final-text").GetAttributeAsync("class"));
        Assert.Equal(1, await page.Page.Locator("#final-text .stream-caret").CountAsync());
        Assert.Equal("- [ ] 先排查 `AuthMiddleware`", await page.Page.Locator("#final-text").TextContentAsync());
        Assert.Equal(1, await page.Page.Locator("#final-text .md-cb").CountAsync());
        Assert.Equal(1, await page.Page.Locator("#final-text .md-code").CountAsync());
        Assert.True(await page.Page.Locator("#edit-full").IsDisabledAsync());
    }
}
