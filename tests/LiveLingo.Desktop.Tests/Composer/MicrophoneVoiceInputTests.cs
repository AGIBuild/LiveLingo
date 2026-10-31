using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Platform;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Composer;

public class MicrophoneVoiceInputTests
{
    private sealed class SilentLive : ILiveRecognizer
    {
        public string CurrentText => "";
        public void Accept(ReadOnlySpan<float> samples) { }
        public string CloseSegment() => "";
        public void Dispose() { }
    }

    private sealed class SilentVad : IVoiceActivityDetector
    {
        public bool IsSpeechActive => false;
        public void Accept(ReadOnlySpan<float> samples) { }
        public bool TryTake(out SpeechSpan span)
        {
            span = default;
            return false;
        }

        public void Flush() { }
        public void Dispose() { }
    }

    private static (MicrophoneVoiceInput Input, IMicrophone Mic, DraftSession Draft, IDictationSessionFactory Factory) Create()
    {
        var draft = new DraftSession(hasTarget: true);
        var factory = Substitute.For<IDictationSessionFactory>();
        factory.CreateAsync(draft, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ => new DictationSession(new SilentLive(), new SilentVad(), null, draft));
        var mic = Substitute.For<IMicrophone>();
        return (new MicrophoneVoiceInput(factory, mic), mic, draft, factory);
    }

    [Fact]
    public async Task Start_CreatesASession_AndOpensTheMicrophoneWithItsPushHandler()
    {
        var (input, mic, draft, factory) = Create();
        Action<float[]>? onFrame = null;
        mic.Start(Arg.Do<Action<float[]>>(handler => onFrame = handler)).Returns("MacBook Microphone");

        await input.StartAsync(draft, ["Redis"], CancellationToken.None);

        await factory.Received(1).CreateAsync(draft, Arg.Is<IReadOnlyList<string>>(g => g.SequenceEqual(new[] { "Redis" })), Arg.Any<CancellationToken>());
        Assert.NotNull(onFrame);
        Assert.True(draft.Read(d => d.Recording));
        onFrame!(new float[640]);

        await input.StopAsync();
        Assert.False(draft.Read(d => d.Recording));
    }

    [Fact]
    public async Task Stop_ClosesTheMicrophoneBeforeTheSessionStops()
    {
        var (input, mic, draft, _) = Create();
        await input.StartAsync(draft, [], CancellationToken.None);

        await input.StopAsync();

        mic.Received(1).Stop();
        await input.StopAsync();
        mic.Received(1).Stop();
    }

    [Fact]
    public async Task AMicrophoneThatCannotOpen_ReleasesTheSession_AndRethrows()
    {
        var (input, mic, draft, _) = Create();
        mic.Start(Arg.Any<Action<float[]>>()).Returns(_ => throw new InvalidOperationException("No default input device."));

        await Assert.ThrowsAsync<InvalidOperationException>(() => input.StartAsync(draft, [], CancellationToken.None));

        Assert.False(draft.Read(d => d.Recording));
        await input.StopAsync();
        mic.DidNotReceive().Stop();
    }

    [Fact]
    public async Task StartingTwice_IsAnError()
    {
        var (input, _, draft, _) = Create();
        await input.StartAsync(draft, [], CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => input.StartAsync(draft, [], CancellationToken.None));
        await input.DisposeAsync();
    }

    [Fact]
    public async Task SilentFrames_RaiseNoSignal_AndSoundClearsIt()
    {
        var (input, mic, draft, _) = Create();
        Action<float[]>? onFrame = null;
        mic.Start(Arg.Do<Action<float[]>>(handler => onFrame = handler)).Returns("mic");
        var changes = new List<bool>();
        input.NoSignalChanged += changes.Add;
        await input.StartAsync(draft, [], CancellationToken.None);

        for (var i = 0; i < 150; i++)
            onFrame!(new float[640]);
        onFrame!(Enumerable.Repeat(0.3f, 640).ToArray());

        Assert.Equal([true, false], changes);
        await input.StopAsync();
    }
}
