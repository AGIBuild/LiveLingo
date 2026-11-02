using LiveLingo.Core.Drafting;

namespace LiveLingo.Core.Tests.Drafting;

public class TextUnitsTests
{
    private static string Identity(string text) =>
        TextUnits.Join(TextUnits.Split(text).Select(u => (u.Lead, u.Body, u.Trail)));

    [Fact]
    public void CompleteSentences_AreSeparateFromTheActiveTail()
    {
        var units = TextUnits.Split("先检查缓存。然后更新");

        Assert.Equal(2, units.Count);
        Assert.True(units[0].IsComplete);
        Assert.False(units[1].IsComplete);
        Assert.Equal("先检查缓存。", units[0].Body);
        Assert.Equal("然后更新", units[1].Body);
    }

    [Theory]
    [InlineData("Upgrade to 10.0.301. Then retry", "Upgrade to 10.0.301.")]
    [InlineData("Dr. Smith will check the API. Wait", "Dr. Smith will check the API.")]
    [InlineData("版本是 `v1.2.3`。继续", "版本是 `v1.2.3`。")]
    [InlineData("Visit https://example.com/v1.2 then retry. Wait", "Visit https://example.com/v1.2 then retry.")]
    [InlineData("Visit https://example.com/v1?q=1&x=2 before retrying. Wait", "Visit https://example.com/v1?q=1&x=2 before retrying.")]
    public void DoesNotCut_Decimals_Abbreviations_Identifiers_OrUrls(string input, string first)
    {
        var units = TextUnits.Split(input);

        Assert.Equal(first, units[0].Body);
        Assert.Equal(2, units.Count);
    }

    [Fact]
    public void FencedCode_IsNotSplitIntoSentences()
    {
        const string fence = "```csharp\nvar x = \"a.b\";\n```";

        var units = TextUnits.Split(fence + "\n然后说明。");

        Assert.Equal([fence, "然后说明。"], units.Select(u => u.Body));
        Assert.Equal("\n", units[1].Lead);
    }

    [Fact]
    public void LineBreaks_AreUnitBoundaries_KeptAsTheLeadOfTheNextUnit()
    {
        var units = TextUnits.Split("Chapter One\n\nIt rained. Then it stopped.\n  - item");

        Assert.Equal(["Chapter One", "It rained.", "Then it stopped.", "- item"], units.Select(u => u.Body));
        Assert.Equal(["", "\n\n", "", "\n  "], units.Select(u => u.Lead));
        Assert.True(units[0].IsComplete);
        Assert.False(units[3].IsComplete);
    }

    [Theory]
    [InlineData("  John Doe @@ \nCEO, Inc. @@   123 Main St\n @@ SF, CA @@ \n  a@b.com @@ VIP.  ")]
    [InlineData("Usage:\n    run [options] @@ \n\n  Commands:\n    start   @@ Start it ; \n    stop    @@ Stop it\n")]
    [InlineData("Upgrade to 10.0.301. Then retry")]
    [InlineData("先检查缓存。然后更新")]
    public void WhitespaceAroundEveryUnit_IsLayout_AndSurvivesAnIdentityPass(string source)
    {
        Assert.Equal(source, Identity(source));
    }

    [Fact]
    public void Join_AddsASpaceOnlyBetweenAsciiSeams()
    {
        Assert.Equal("Un. Deux.", TextUnits.Join([("", "Un.", ""), ("", "Deux.", "")]));
        Assert.Equal("一。二。", TextUnits.Join([("", "一。", ""), ("", "二。", "")]));
        Assert.Equal("用 Redis。然后 retry", TextUnits.Join([("", "用 Redis。", ""), ("", "然后 retry", "")]));
        Assert.Equal("Done.好的。", TextUnits.Join([("", "Done.", ""), ("", "好的。", "")]));
    }

    [Theory]
    [InlineData("先不要合并。等 CI 跑完！好吗？", new[] { "先不要合并。", "等 CI 跑完！", "好吗？" })]
    [InlineData("他说：“好。”然后走了。", new[] { "他说：“好。”", "然后走了。" })]
    [InlineData("Set the timeout to 3.5 seconds. Then restart.", new[] { "Set the timeout to 3.5 seconds.", "Then restart." })]
    [InlineData("第一行\n\n第二行\n", new[] { "第一行", "第二行" })]
    [InlineData("   ", new string[0])]
    public void Split_HandlesChineseEnglishQuotesDecimalsAndNewlines(string text, string[] expected)
    {
        Assert.Equal(expected, TextUnits.Split(text).Select(u => u.Body));
    }

    [Theory]
    [InlineData("先改接口；再补测试", new[] { "先改接口；", "再补测试" })]
    [InlineData("真的吗？！好的……继续", new[] { "真的吗？！", "好的……", "继续" })]
    [InlineData("他说“行！”，然后走了", new[] { "他说“行！”", "，然后走了" })]
    [InlineData("括号（见上文。）后面", new[] { "括号（见上文。）", "后面" })]
    public void Split_EndsSentencesAtSemicolonsEllipsesAndRepeatedTerminators(string text, string[] expected)
    {
        Assert.Equal(expected, TextUnits.Split(text).Select(u => u.Body));
    }

    [Fact]
    public void Split_KeepsTrailingWhitespaceOfTheLastCompleteUnit()
    {
        var units = TextUnits.Split("好的。 \n");

        Assert.Single(units);
        Assert.Equal(" \n", units[0].Trail);
    }

    [Theory]
    [InlineData("这个 PR 先不要合并。", DraftRoute.Units)]
    [InlineData("看这个：\n```js\nconst a = 1;\n```", DraftRoute.Whole)]
    [InlineData("# 标题\n- 一\n- 二\n- 三", DraftRoute.Whole)]
    [InlineData("第一句。\n第二句。\n第三句。", DraftRoute.Units)]
    [InlineData("1. 一\n2. 二\n3. 三", DraftRoute.Whole)]
    [InlineData("1. 一\n2. 二", DraftRoute.Units)]
    public void RouteFor_ChoosesWholeForCodeOrStructuredText(string text, DraftRoute expected)
    {
        Assert.Equal(expected, TextUnits.RouteFor(text));
    }

    [Fact]
    public void RouteFor_TreatsTextOverTheLimitAsWhole()
    {
        Assert.Equal(DraftRoute.Units, TextUnits.RouteFor(new string('字', TextUnits.WholeTextLength)));
        Assert.Equal(DraftRoute.Whole, TextUnits.RouteFor(new string('字', TextUnits.WholeTextLength + 1)));
    }

    [Fact]
    public void Whole_KeepsOnlyOuterWhitespaceAsLayout()
    {
        var unit = Assert.Single(TextUnits.Whole("  {\n  \"a\": 1\n}\n"));

        Assert.Equal("{\n  \"a\": 1\n}", unit.Body);
        Assert.Equal("  ", unit.Lead);
        Assert.Equal("\n", unit.Trail);
        Assert.Empty(TextUnits.Whole(" \n "));
    }

    [Fact]
    public void LiteralSpans_FindCodeUrlsAndPlaceholders()
    {
        const string text = "用 `x`，见 https://a.io/b，填 {name} 或 ${KEY}。";

        var spans = TextUnits.LiteralSpans(text).Select(s => text[s.Start..s.End]);

        Assert.Equal(["`x`", "https://a.io/b", "{name}", "${KEY}"], spans);
    }

    [Fact]
    public void AnUnclosedBacktick_KeepsTheRestAsOneUnit_BecauseTheCodeNeverEnds()
    {
        var units = TextUnits.Split("先看 `未闭合。后面还有。");

        Assert.Equal("先看 `未闭合。后面还有。", Assert.Single(units).Body);
    }

    [Fact]
    public void ABacktickRunAtTheVeryEnd_IsReadWithoutRunningPastTheText()
    {
        var units = TextUnits.Split("说明。``");

        Assert.Equal(["说明。", "``"], units.Select(u => u.Body).ToArray());
    }

    [Fact]
    public void AFenceOfThreeBackticks_IsOneRunOfTicks_AndTextAfterTheFenceSplitsAgain()
    {
        var units = TextUnits.Split("```\n不拆。\n```\n然后说明。再来一句。");

        Assert.Equal(3, units.Count);
        Assert.Equal("```\n不拆。\n```", units[0].Body);
        Assert.Equal("然后说明。", units[1].Body);
        Assert.Equal("再来一句。", units[2].Body);
    }
}
