using LiveLingo.Core.Dictation;

namespace LiveLingo.Core.Tests.Dictation;

public class TextTidyTests
{
    [Theory]
    [InlineData("设成 三十 秒， 不对", "设成三十秒，不对")]
    [InlineData("昨天是 Monday， today is 礼拜二", "昨天是 Monday，today is 礼拜二")]
    [InlineData("  用 Redis 缓存  ", "用 Redis 缓存")]
    public void RemovesRecognizerSpacing_ButKeepsSpacesAroundEnglishWords(string input, string expected) =>
        Assert.Equal(expected, TextTidy.Apply(input));

    [Theory]
    [InlineData("还是在指导工作方面，都可。<|endoftext|>Human Rights Watch 是美国的组织。有相应的减轻负担。", "还是在指导工作方面，都可。")]
    [InlineData("<|zh|>你好<|en|>", "你好")]
    public void DropsSpecialTokens_AndEverythingAModelInventsAfterItsEndOfText(string input, string expected) =>
        Assert.Equal(expected, TextTidy.Apply(input));
}

public class AudioBufferTests
{
    [Fact]
    public void Slice_SpansSeveralChunks()
    {
        var buffer = new AudioBuffer();
        buffer.Append([1f, 2f, 3f]);
        buffer.Append([4f, 5f]);
        buffer.Append([6f, 7f, 8f]);

        Assert.Equal([3f, 4f, 5f, 6f], buffer.Slice(2, 6));
        Assert.Equal(8, buffer.TotalSamples);
    }

    [Fact]
    public void Slice_IsClampedToAvailableAudio()
    {
        var buffer = new AudioBuffer();
        buffer.Append([1f, 2f, 3f]);

        Assert.Equal([1f, 2f, 3f], buffer.Slice(-5, 99));
        Assert.Empty(buffer.Slice(5, 9));
    }

    [Fact]
    public void AudioFormat_ConvertsBetweenSamplesAndMilliseconds()
    {
        Assert.Equal(16000, AudioFormat.MsToSamples(1000));
        Assert.Equal(40, AudioFormat.SamplesToMs(640));
    }
}
