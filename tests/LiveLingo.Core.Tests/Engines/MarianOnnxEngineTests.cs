using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LiveLingo.Core.Tests.Engines;

public class MarianOnnxEngineTests
{
    [Fact]
    public void SupportsLanguagePair_ReturnsTrue_ForRegisteredPair()
    {
        var engine = CreateEngine();
        Assert.True(engine.SupportsLanguagePair("zh", "en"));
    }

    [Fact]
    public void SupportsLanguagePair_ReturnsFalse_ForUnknownPair()
    {
        var engine = CreateEngine();
        Assert.False(engine.SupportsLanguagePair("ko", "fr"));
    }

    [Fact]
    public async Task TranslateAsync_ThrowsNotSupported_ForUnknownPair()
    {
        var engine = CreateEngine();

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => engine.TranslateAsync("hello", "ko", "fr", CancellationToken.None));

        Assert.Contains("ko→fr", ex.Message);
    }

    [Fact]
    public async Task TranslateAsync_ThrowsOnCancellation()
    {
        var engine = CreateEngine();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => engine.TranslateAsync("hello", "zh", "en", cts.Token));
    }

    [Fact]
    public async Task TranslateAsync_ThrowsClearError_WhenModelFilesMissing()
    {
        var modelManager = Substitute.For<IModelManager>();
        var tempDir = Path.Combine(Path.GetTempPath(), $"marian-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        modelManager.GetModelDirectory(ModelRegistry.MarianZhEn.Id).Returns(tempDir);
        modelManager.EnsureModelAsync(Arg.Any<ModelDescriptor>(), Arg.Any<IProgress<ModelDownloadProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var engine = new MarianOnnxEngine(modelManager, Substitute.For<ILogger<MarianOnnxEngine>>());

        var ex = await Assert.ThrowsAsync<FileNotFoundException>(
            () => engine.TranslateAsync("你好", "zh", "en", CancellationToken.None));

        Assert.Contains("encoder_model.onnx", ex.Message);
        Assert.Contains("decoder_model_merged.onnx", ex.Message);
        Assert.Contains("source.spm", ex.Message);
        Assert.Contains("target.spm", ex.Message);
    }

    [Fact]
    public void SupportedLanguages_ContainsDistinctLanguageCodes()
    {
        var engine = CreateEngine();
        var codes = engine.SupportedLanguages.Select(l => l.Code).ToList();

        Assert.NotEmpty(codes);
        Assert.Equal(codes.Count, codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(codes, c => string.Equals(c, "zh", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(codes, c => string.Equals(c, "en", StringComparison.OrdinalIgnoreCase));
    }

    private static MarianOnnxEngine CreateEngine()
    {
        var modelManager = Substitute.For<IModelManager>();
        modelManager.GetModelDirectory(Arg.Any<string>())
            .Returns(call => Path.Combine(Path.GetTempPath(), call.ArgAt<string>(0)));
        return new MarianOnnxEngine(modelManager, Substitute.For<ILogger<MarianOnnxEngine>>());
    }
}
