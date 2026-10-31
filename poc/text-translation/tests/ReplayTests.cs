namespace TranslationPoc.Tests;

public class ReplayTests
{
    [Fact]
    public async Task Configurable_replay_translates_every_source_unit_once()
    {
        var result = await Evaluation.ReplayAsync(new Immediate(), "test", true, default, count: 8, intervalMs: 0);
        Assert.Equal(8, result.Calls.Count);
        Assert.Equal(result.Source.Length, result.TranslatedSourceChars);
        Assert.DoesNotContain("错误", result.Final);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, -1)]
    public async Task Invalid_replay_timing_is_rejected(int count, int interval)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Evaluation.ReplayAsync(new Immediate(), "test", true, default, count, interval));
    }

    private sealed class Immediate : ITranslator
    {
        public Task<Translation> TranslateAsync(Request request, CancellationToken ct) =>
            Task.FromResult(new Translation(request.Source, 0, 0));
    }
}
