using LiveLingo.Desktop.Composer;

namespace LiveLingo.Desktop.Tests.Composer;

public class SignalMonitorTests
{
    private static float[] Frame(float level) => Enumerable.Repeat(level, 640).ToArray();

    private static bool? Feed(SignalMonitor monitor, float level, int milliseconds)
    {
        bool? change = null;
        for (var elapsed = 0; elapsed < milliseconds; elapsed += 40)
            change = monitor.Observe(Frame(level)) ?? change;
        return change;
    }

    [Fact]
    public void ContinuousSilence_IsReportedOnceAfterTheTimeout()
    {
        var monitor = new SignalMonitor(TimeSpan.FromSeconds(3));

        Assert.Null(Feed(monitor, 0, 2900));
        var changes = new List<bool>();
        for (var i = 0; i < 200; i++)
        {
            if (monitor.Observe(Frame(0)) is { } change)
                changes.Add(change);
        }

        Assert.Equal([true], changes);
    }

    [Fact]
    public void Silence_IsReported_ExactlyWhenTheTimeoutElapses()
    {
        var monitor = new SignalMonitor(TimeSpan.FromSeconds(1));
        var reported = -1;
        for (var i = 0; i < 100; i++)
        {
            if (monitor.Observe(Frame(0)) == true)
            {
                reported = i;
                break;
            }
        }

        Assert.Equal(24, reported); // 25 frames of 40 ms = 1 s
    }

    [Fact]
    public void Speech_ClearsTheReport_AndSilenceCanBeReportedAgain()
    {
        var monitor = new SignalMonitor(TimeSpan.FromSeconds(1));
        Assert.True(Feed(monitor, 0, 1200));

        Assert.False(monitor.Observe(Frame(0.2f)));
        Assert.Null(monitor.Observe(Frame(0.2f)));
        Assert.True(Feed(monitor, 0, 1200));
    }

    [Fact]
    public void ASignalNowAndThen_KeepsTheMonitorQuiet()
    {
        var monitor = new SignalMonitor(TimeSpan.FromSeconds(1));
        for (var i = 0; i < 10; i++)
        {
            Assert.Null(Feed(monitor, 0, 800));
            Assert.Null(monitor.Observe(Frame(0.1f)));
        }
    }

    [Fact]
    public void BackgroundNoiseBelowTheThreshold_CountsAsSilence()
    {
        var monitor = new SignalMonitor(TimeSpan.FromSeconds(1));

        Assert.True(Feed(monitor, SignalMonitor.SpeechPeak / 2, 1200));
    }

    [Fact]
    public void NegativeSamples_CountByTheirMagnitude()
    {
        var monitor = new SignalMonitor(TimeSpan.FromSeconds(1));

        Assert.Null(Feed(monitor, -0.3f, 3000));
    }
}
