namespace TranslationPoc.Tests;

public class ProtectionTests
{
    [Fact]
    public void Fenced_code_urls_and_placeholders_are_restored_exactly()
    {
        const string original = "说明\n```cs\nvar x = \"a.b\";\n```\n地址 https://example.com/v1?q=1，使用 {name} 和 ${API_KEY}，类型 `ViewModel`。";
        var protectedText = ProtectedText.Encode(original);
        Assert.Equal(5, protectedText.Literals.Count);
        Assert.DoesNotContain("var x", protectedText.Source);
        Assert.DoesNotContain("https://", protectedText.Source);
        Assert.Equal(original, protectedText.Restore(protectedText.Source));
    }

    [Fact]
    public void Missing_or_duplicated_literal_token_is_explicit_failure()
    {
        var text = ProtectedText.Encode("保留 `CancellationToken`。");
        var token = Assert.Single(text.Literals).Key;
        Assert.Throws<InvalidDataException>(() => text.Restore("No identifier."));
        Assert.Throws<InvalidDataException>(() => text.Restore(token + token));
    }

    [Fact]
    public void User_text_cannot_collide_with_generated_tokens()
    {
        var text = ProtectedText.Encode("literal __KEEP_0__ and `x`");
        Assert.Equal("literal __KEEP_0__ and `x`", text.Restore(text.Source));
    }
}
