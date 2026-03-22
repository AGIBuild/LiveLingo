using LiveLingo.Core.Models;

namespace LiveLingo.Core.Tests.Models;

public class HuggingFaceWebUrlsTests
{
    [Fact]
    public void TryGetModelCardUrl_FromResolveUrl_ReturnsRepoRoot()
    {
        var download =
            "https://huggingface.co/Abhiray/Qwen3.5-9B-abliterated-GGUF/resolve/main/Qwen3.5-9B-abliterated-Q4_K_M.gguf";

        Assert.True(HuggingFaceWebUrls.TryGetModelCardUrl(download, out var card));
        Assert.Equal("https://huggingface.co/Abhiray/Qwen3.5-9B-abliterated-GGUF", card);
    }

    [Fact]
    public void TryGetModelCardUrl_NonHf_ReturnsFalse()
    {
        Assert.False(HuggingFaceWebUrls.TryGetModelCardUrl("https://example.com/file.bin", out _));
    }
}
