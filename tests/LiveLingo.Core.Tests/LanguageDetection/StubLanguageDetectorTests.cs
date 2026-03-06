using LiveLingo.Core.LanguageDetection;

namespace LiveLingo.Core.Tests.LanguageDetection;

public class StubLanguageDetectorTests
{
    private readonly StubLanguageDetector _detector = new();

    [Fact]
    public async Task DetectAsync_ReturnsZhWithFullConfidence()
    {
        var result = await _detector.DetectAsync("any text", CancellationToken.None);
        Assert.Equal("zh", result.Language);
        Assert.Equal(1.0f, result.Confidence);
    }

    [Fact]
    public async Task DetectAsync_ThrowsOnCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _detector.DetectAsync("test", cts.Token));
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var d = new StubLanguageDetector();
        d.Dispose();
    }
}
