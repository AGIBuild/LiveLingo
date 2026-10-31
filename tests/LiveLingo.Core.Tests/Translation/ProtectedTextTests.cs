using LiveLingo.Core.Translation;

namespace LiveLingo.Core.Tests.Translation;

public class ProtectedTextTests
{
    [Fact]
    public void FencedCode_Urls_AndPlaceholders_AreRestoredExactly()
    {
        const string original = "说明\n```cs\nvar x = \"a.b\";\n```\n地址 https://example.com/v1?q=1，使用 {name} 和 ${API_KEY}，类型 `ViewModel`。";

        var protectedText = ProtectedText.Encode(original);

        Assert.Equal(5, protectedText.Literals.Count);
        Assert.DoesNotContain("var x", protectedText.Source);
        Assert.DoesNotContain("https://", protectedText.Source);
        Assert.Equal(original, protectedText.Restore(protectedText.Source));
    }

    [Fact]
    public void TextWithoutLiterals_IsUnchanged()
    {
        var protectedText = ProtectedText.Encode("没有需要保护的内容。");

        Assert.Empty(protectedText.Literals);
        Assert.Equal("没有需要保护的内容。", protectedText.Source);
    }

    [Fact]
    public void MissingOrDuplicatedLiteralToken_IsAnExplicitFailure()
    {
        var text = ProtectedText.Encode("保留 `CancellationToken`。");
        var token = Assert.Single(text.Literals).Key;

        Assert.Throws<InvalidDataException>(() => text.Restore("No identifier."));
        Assert.Throws<InvalidDataException>(() => text.Restore(token + token));
    }

    [Fact]
    public void UserText_CannotCollideWithGeneratedTokens()
    {
        var text = ProtectedText.Encode("literal __KEEP_0__ and `x`");

        Assert.Equal("literal __KEEP_0__ and `x`", text.Restore(text.Source));
    }
}
