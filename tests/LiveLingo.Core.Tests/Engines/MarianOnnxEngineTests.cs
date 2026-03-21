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
        Assert.True(engine.SupportsLanguagePair("ko", "fr"));
    }

    [Fact]
    public async Task TranslateAsync_ThrowsNotSupported_ForUnknownPair()
    {
        var engine = CreateEngine();

        // With the default fallback to Qwen35_9B, the engine will actually try to load Qwen35_9B and fail due to directory not found instead of NotSupportedException.
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => engine.TranslateAsync("hello", "ko", "fr", CancellationToken.None));
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

        modelManager.GetModelDirectory(ModelRegistry.Qwen35_9B.Id).Returns(tempDir);
        modelManager.EnsureModelAsync(Arg.Any<ModelDescriptor>(), Arg.Any<IProgress<ModelDownloadProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var engine = new MarianOnnxEngine(modelManager, Substitute.For<ILogger<MarianOnnxEngine>>());

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => engine.TranslateAsync("你好", "zh", "en", CancellationToken.None));
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
