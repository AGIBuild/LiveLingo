namespace TranslationPoc.Tests;

public class TextTests
{
    [Fact]
    public void Complete_sentences_are_separate_from_the_active_tail()
    {
        var units = Units.Split("先检查缓存。然后更新");
        Assert.Equal(2, units.Count);
        Assert.True(units[0].Complete);
        Assert.False(units[1].Complete);
        Assert.Equal("先检查缓存。", units[0].Text);
        Assert.Equal("然后更新", units[1].Text);
    }

    [Theory]
    [InlineData("Upgrade to 10.0.301. Then retry", "Upgrade to 10.0.301.")]
    [InlineData("Dr. Smith will check the API. Wait", "Dr. Smith will check the API.")]
    [InlineData("版本是 `v1.2.3`。继续", "版本是 `v1.2.3`。")]
    [InlineData("Visit https://example.com/v1.2 then retry. Wait", "Visit https://example.com/v1.2 then retry.")]
    [InlineData("Visit https://example.com/v1?q=1&x=2 before retrying. Wait", "Visit https://example.com/v1?q=1&x=2 before retrying.")]
    public void Does_not_cut_decimals_abbreviations_identifiers_or_urls(string input, string first)
    {
        var units = Units.Split(input);
        Assert.Equal(first, units[0].Text);
        Assert.Equal(2, units.Count);
    }

    [Fact]
    public void Fenced_code_is_not_split_into_sentences()
    {
        const string fence = "```csharp\nvar x = \"a.b\";\n```";
        var units = Units.Split(fence + "\n然后说明。");
        Assert.Equal([fence, "然后说明。"], units.Select(u => u.Text));
        Assert.Equal("\n", units[1].Break);
    }

    [Fact]
    public void Line_breaks_are_unit_boundaries_and_are_kept_as_the_break_before_the_next_unit()
    {
        var units = Units.Split("Chapter One\n\nIt rained. Then it stopped.\n  - item");
        Assert.Equal(["Chapter One", "It rained.", "Then it stopped.", "- item"], units.Select(u => u.Text));
        Assert.Equal(["", "\n\n", "", "\n  "], units.Select(u => u.Break));
        Assert.True(units[0].Complete);
        Assert.False(units[3].Complete);
    }

    [Fact]
    public void Join_restores_line_breaks_and_uses_target_language_spacing()
    {
        var units = Units.Split("First. Second.\nThird.");
        Assert.Equal("Un. Deux.\nTrois.", Units.Join(units.Zip(new[] { "Un.", "Deux.", "Trois." }, (u, t) => (u.Break, t, u.Trail)), "en"));
        Assert.Equal("一。二。\n三。", Units.Join(units.Zip(new[] { "一。", "二。", "三。" }, (u, t) => (u.Break, t, u.Trail)), "zh"));
    }

    [Theory]
    [InlineData("  John Doe @@ \nCEO, Inc. @@   123 Main St\n @@ SF, CA @@ \n  a@b.com @@ VIP.  ")]
    [InlineData("Usage:\n    run [options] @@ \n\n  Commands:\n    start   @@ Start it ; \n    stop    @@ Stop it\n")]
    public void Whitespace_around_every_unit_is_layout_and_survives_an_identity_translation(string source)
    {
        var units = Units.Split(source);
        Assert.Equal(source, Units.Join(units.Select(u => (u.Break, u.Text, u.Trail)), "en"));
    }

    [Fact]
    public void Only_terms_present_in_the_target_source_are_injected()
    {
        var prompt = Prompt.Build(new Request("缓存穿透会增加 Redis 的负载。", "en",
            [new("缓存穿透", "cache penetration"), new("数据库索引", "database index")], "前文的内容"));
        Assert.Contains("cache penetration", prompt);
        Assert.DoesNotContain("database index", prompt);
        Assert.Contains("English", prompt);
        Assert.Contains("前文的内容", prompt);
        Assert.Contains("only", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Instructions_are_before_the_translation_boundary_not_part_of_the_source()
    {
        const string source = "不要重试。";
        var prompt = Prompt.Build(new Request(source, "en", []));
        const string boundary = "Translate the following text";
        Assert.True(prompt.IndexOf("Preserve meaning", StringComparison.Ordinal) < prompt.IndexOf(boundary, StringComparison.Ordinal));
        Assert.EndsWith(":\n\n" + source, prompt);
    }

    [Fact]
    public void Multi_line_source_is_sent_without_json_escaping()
    {
        var prompt = Prompt.Build(new Request("{\n  \"a\": \"你好\"\n}", "en", []));
        Assert.EndsWith(":\n\n{\n  \"a\": \"你好\"\n}", prompt);
    }

    [Fact]
    public void Glossary_text_parses_equals_and_arrow_pairs_across_delimiters()
    {
        var terms = TermParser.Parse("缓存穿透=cache penetration，缓存击穿 -> cache breakdown\nignored-item; 幂等键:idempotency key");
        Assert.Equal(
        [
            new Term("缓存穿透", "cache penetration"),
            new Term("缓存击穿", "cache breakdown"),
            new Term("幂等键", "idempotency key")
        ], terms);
    }

    [Fact]
    public void Translation_case_store_saves_and_updates_reference_outside_repo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "transpoc-store-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new TranslationCaseStore(dir);
            var saved = store.Save(new SavedTranslationCase(
                Id: "",
                SavedAt: new DateTime(2026, 10, 29, 18, 0, 0),
                Model: "hy-mt2-7b",
                TargetLanguage: "en",
                Glossary: [new Term("接口", "endpoint")],
                Source: "不要重试这个接口。",
                Translated: "Do not retry this endpoint.",
                Reference: "",
                TotalCalls: 1,
                DiscardedResults: 0,
                Units: [new UnitView(0, "不要重试这个接口。", "Do not retry this endpoint.", true, null, false, 100, 250, 0)]));
            Assert.NotEmpty(saved.Id);
            store.SetReference(saved.Id, "Never retry this endpoint.");
            Assert.Equal("Never retry this endpoint.", store.Load(saved.Id).Reference);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
