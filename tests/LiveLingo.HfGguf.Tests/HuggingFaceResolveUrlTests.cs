using LiveLingo.HfGguf;

namespace LiveLingo.HfGguf.Tests;

public class HuggingFaceResolveUrlTests
{
    [Fact]
    public void TryParse_GgufSingleSegment()
    {
        var ok = HuggingFaceResolveUrl.TryParse(
            "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen.gguf",
            out var repo,
            out var rev,
            out var path);
        Assert.True(ok);
        Assert.Equal("Qwen/Qwen2.5-1.5B-Instruct-GGUF", repo);
        Assert.Equal("main", rev);
        Assert.Equal("qwen.gguf", path);
    }

    [Fact]
    public void TryParse_NestedOnnxPath()
    {
        var ok = HuggingFaceResolveUrl.TryParse(
            "https://huggingface.co/Xenova/foo/resolve/main/onnx/encoder_model.onnx",
            out var repo,
            out var rev,
            out var path);
        Assert.True(ok);
        Assert.Equal("Xenova/foo", repo);
        Assert.Equal("main", rev);
        Assert.Equal("onnx/encoder_model.onnx", path);
    }

    [Fact]
    public void TryParse_MirrorHost_StillParsesRepo()
    {
        var ok = HuggingFaceResolveUrl.TryParse(
            "https://hf-mirror.com/a/b/resolve/refs%2Fpr%2F1/model.gguf",
            out var repo,
            out var rev,
            out var path);
        Assert.True(ok);
        Assert.Equal("a/b", repo);
        Assert.Equal("refs%2Fpr%2F1", rev);
        Assert.Equal("model.gguf", path);
    }

    [Fact]
    public void TryParse_NonResolve_ReturnsFalse()
    {
        Assert.False(HuggingFaceResolveUrl.TryParse("http://fake/model.bin", out _, out _, out _));
    }
}
