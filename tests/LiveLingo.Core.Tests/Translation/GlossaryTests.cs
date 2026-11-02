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
        Assert.Empty(Glossary.Empty.TermsFor(TargetLanguage.English));
        Assert.Same(Glossary.Empty, Glossary.Parse("  "));
        Assert.Same(Glossary.Empty, Glossary.Parse(null));
    }
}
