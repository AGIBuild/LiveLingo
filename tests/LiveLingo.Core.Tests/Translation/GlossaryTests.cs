using LiveLingo.Core.Translation;

namespace LiveLingo.Core.Tests.Translation;

public class GlossaryTests
{
    [Fact]
    public void Parse_ReadsTermsAndPairs_FromLinesAndCommonSeparators()
    {
        var glossary = Glossary.Parse("Avalonia, ViewModel；缓存穿透=cache penetration\n幂等键 -> idempotency key\n接口：endpoint");

        Assert.Equal(["Avalonia", "ViewModel", "缓存穿透", "幂等键", "接口"], glossary.Terms);
        Assert.Equal(
            [new GlossaryTerm("缓存穿透", "cache penetration"), new GlossaryTerm("幂等键", "idempotency key"), new GlossaryTerm("接口", "endpoint")],
            glossary.Pairs);
    }

    [Fact]
    public void Parse_IgnoresCommentsBlankLinesAndIncompletePairs()
    {
        var glossary = Glossary.Parse("# my terms\n\n  Redis  \n缓存=\n=cache\n#another");

        Assert.Equal(["Redis", "缓存"], glossary.Terms);
        Assert.Empty(glossary.Pairs);
    }

    [Fact]
    public void Parse_RemovesDuplicates_IgnoringCase()
    {
        var glossary = Glossary.Parse("Redis\nredis\nRedis=Redis cache");

        Assert.Equal(["Redis"], glossary.Terms);
    }

    [Fact]
    public void TermsFor_OrientsPairsTowardTheTargetLanguage()
    {
        var glossary = Glossary.Parse("缓存穿透=cache penetration");

        Assert.Equal([new GlossaryTerm("缓存穿透", "cache penetration")], glossary.TermsFor(TargetLanguage.English));
        Assert.Equal([new GlossaryTerm("cache penetration", "缓存穿透")], glossary.TermsFor(TargetLanguage.Chinese));
    }

    [Fact]
    public void Empty_HasNothing()
    {
        Assert.Empty(Glossary.Empty.Terms);
        Assert.Empty(Glossary.Empty.Corrections);
        Assert.Empty(Glossary.Empty.TermsFor(TargetLanguage.English));
        Assert.Same(Glossary.Empty, Glossary.Parse("  "));
        Assert.Same(Glossary.Empty, Glossary.Parse(null));
    }

    [Fact]
    public void Parse_ReadsAccentCorrections_AndAddsOnlyCanonicalTermsToHotwords()
    {
        var glossary = Glossary.Parse("Scale => Skill\n六零九⇒LiveLingo\n缓存穿透=cache penetration\nincomplete=>");

        Assert.Equal(
            [new GlossaryCorrection("Scale", "Skill"), new GlossaryCorrection("六零九", "LiveLingo")],
            glossary.Corrections);
        Assert.Equal(["Skill", "LiveLingo", "缓存穿透"], glossary.Terms);
        Assert.DoesNotContain("Scale", glossary.Terms);
        Assert.DoesNotContain("六零九", glossary.Terms);
        Assert.DoesNotContain("incomplete", glossary.Terms);
    }

    [Fact]
    public void BuiltInTerms_FollowUserTerms_AndSpeechHotwordsStayBelowQwen3AsrTokenLimit()
    {
        var glossary = Glossary.Parse("MyCustomAgent, CommunityToolkit, OnlineRecognizer\nScale=>Skill", includeBuiltIn: true);

        Assert.Equal("MyCustomAgent", glossary.Terms[0]);
        Assert.Contains("Avalonia", glossary.Terms);
        Assert.Contains("VS Code", glossary.Terms);
        Assert.Equal(1, glossary.Terms.Count(t => string.Equals(t, "Skill", StringComparison.OrdinalIgnoreCase)));

        var defaultHotwords = glossary.SpeechHotwords();
        Assert.Equal("MyCustomAgent", defaultHotwords[0]);
        Assert.True(defaultHotwords.Count <= Glossary.MaxSpeechHotwords);
        Assert.True(defaultHotwords.Sum(Glossary.EstimateHotwordTokens) <= Glossary.MaxSpeechHotwordTokens);

        var bounded = glossary.SpeechHotwords(maxTerms: 5, maxChars: 40);
        Assert.True(bounded.Count <= 5);
        Assert.Equal("MyCustomAgent", bounded[0]);
        Assert.True(string.Join(", ", bounded).Length <= 40);
    }

    [Theory]
    [InlineData("呃，Agent用色描述，或者说Scale机制。", "呃，Agent用色描述，或者说Skill机制。", "Scale", "Skill")]
    [InlineData("呃，Agent用色描述。", "呃，Agent润色描述。", "用色", "润色")]
    [InlineData("把六零九打开。", "把 LiveLingo 打开。", "六零九", "LiveLingo")]
    public void SuggestCorrection_ExtractsWordBoundedSpokenToCanonicalRule(
        string original, string edited, string expectedSpoken, string expectedCanonical)
    {
        var suggestion = Glossary.Empty.SuggestCorrection(original, edited);

        Assert.Equal(new GlossaryCorrection(expectedSpoken, expectedCanonical), suggestion);
        var saved = Glossary.Parse($"{expectedSpoken}=>{expectedCanonical}");
        Assert.Null(saved.SuggestCorrection(original, edited));
    }

    [Fact]
    public void CloudSpeechVocabulary_AssignsSuperWeightToUserTerms_AndStandardWeightToBuiltIns_WhileEnforcingLimits()
    {
        var glossary = Glossary.Parse(
            "祺元, 璇枢=XuanShu, Scale=>Skill, 这是一个超过十五个字符的超长中文热词测试条目, one two three four five six seven eight",
            includeBuiltIn: true);

        var vocab = glossary.CloudSpeechVocabulary();

        // User-configured terms and correction canonicals get P0 super-hotword weight (50).
        Assert.Equal(Glossary.CloudSuperHotwordWeight, vocab["祺元"]);
        Assert.Equal(Glossary.CloudSuperHotwordWeight, vocab["璇枢"]);
        Assert.Equal(Glossary.CloudSuperHotwordWeight, vocab["Skill"]);

        // Built-in developer terms stay in the P1 candidate pool (weight 4), uncapped by local ONNX's 24-word ceiling.
        Assert.Equal(Glossary.CloudStandardHotwordWeight, vocab["Agent"]);
        Assert.Equal(Glossary.CloudStandardHotwordWeight, vocab["Avalonia"]);
        Assert.True(vocab.Count > Glossary.MaxSpeechHotwords);

        // Spoken error forms and entries violating DashScope's 15-char / 7-word hotword limits are excluded.
        Assert.False(vocab.ContainsKey("Scale"));
        Assert.False(vocab.ContainsKey("这是一个超过十五个字符的超长中文热词测试条目"));
        Assert.False(vocab.ContainsKey("one two three four five six seven eight"));
    }
}
