using LiveLingo.Core.Inference;
using LiveLingo.Core.Models;
using LiveLingo.Core.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LiveLingo.Core.Tests.Inference;

public class LlamaServerHostTests
{
    private sealed class FakeProcess : ILlamaProcess
    {
        public bool HasExited { get; set; }
        public int? ExitCode { get; set; }
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            HasExited = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeLauncher : ILlamaProcessLauncher
    {
        public List<(LlamaServerLaunch Launch, FakeProcess Process)> Started { get; } = [];
        public bool ExitImmediately { get; set; }

        public ILlamaProcess Start(LlamaServerLaunch launch)
        {
            var process = new FakeProcess();
            if (ExitImmediately)
            {
                process.HasExited = true;
                process.ExitCode = 3;
            }
            Started.Add((launch, process));
            return process;
        }
    }

    private sealed class FakeProbe : IServerHealthProbe
    {
        public int ReadyAfterPolls { get; set; }
        public int Polls { get; private set; }

        public Task<bool> IsReadyAsync(Uri endpoint, CancellationToken ct) => Task.FromResult(++Polls > ReadyAfterPolls);
    }

    private sealed class SequentialPorts : IPortAllocator
    {
        private int _next = 41000;

        public int NextLoopbackPort() => _next++;
    }

    private sealed class Harness : IAsyncDisposable
    {
        public IModelManager Models { get; } = Substitute.For<IModelManager>();
        public FakeLauncher Launcher { get; } = new();
        public FakeProbe Probe { get; } = new();
        public ManualTimeProvider Time { get; } = new();
        public CoreOptions Core { get; } = new();
        public ModelDescriptor Runtime { get; } = ModelCatalog.LlamaRuntimeFor("osx-arm64");
        public LlamaServerHost Host { get; }

        public Harness(LlamaServerHostOptions? options = null)
        {
            Models.HasAllExpectedLocalAssets(Arg.Any<ModelDescriptor>()).Returns(true);
            Models.GetModelDirectory(Arg.Any<string>()).Returns(call => Path.Combine("/models", call.Arg<string>()));
            Host = new LlamaServerHost(
                Models,
                Runtime,
                Launcher,
                Probe,
                new SequentialPorts(),
                Core,
                options ?? new LlamaServerHostOptions(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(1)),
                Time,
                NullLogger<LlamaServerHost>.Instance);
        }

        public ValueTask DisposeAsync() => Host.DisposeAsync();
    }

    private static readonly ModelDescriptor Fast = ModelCatalog.TranslatorFast;
    private static readonly ModelDescriptor Quality = ModelCatalog.TranslatorQuality;

    [Fact]
    public async Task FirstAcquire_StartsAServerOnALoopbackPort()
    {
        await using var h = new Harness();

        using var lease = await h.Host.AcquireAsync(InferenceRole.Translator, Fast, CancellationToken.None);

        var (launch, _) = Assert.Single(h.Launcher.Started);
        Assert.Equal(Path.Combine("/models", h.Runtime.Id, "llama-server"), launch.ExecutablePath);
        Assert.Equal("http://127.0.0.1:41000/", launch.Endpoint.ToString());
        Assert.Equal(
            ["--model", Path.Combine("/models", Fast.Id, "Hy-MT2-1.8B-Q4_K_M.gguf"),
             "--host", "127.0.0.1", "--port", "41000", "--ctx-size", "8192", "--parallel", "1",
             "--n-gpu-layers", "99", "--reasoning-budget", "0"],
            launch.Arguments);
        Assert.NotNull(lease.Client);
    }

    [Fact]
    public async Task ConfiguredThreads_ArePassedToTheServer()
    {
        await using var h = new Harness();
        h.Core.InferenceThreads = 6;

        using var lease = await h.Host.AcquireAsync(InferenceRole.Translator, Fast, CancellationToken.None);

        var arguments = h.Launcher.Started[0].Launch.Arguments;
        var index = arguments.ToList().IndexOf("--threads");
        Assert.Equal("6", arguments[index + 1]);
    }

    [Fact]
    public async Task WaitsUntilTheModelIsLoaded()
    {
        await using var h = new Harness();
        h.Probe.ReadyAfterPolls = 3;

        using var lease = await h.Host.AcquireAsync(InferenceRole.Translator, Fast, CancellationToken.None);

        Assert.Equal(4, h.Probe.Polls);
    }

    [Fact]
    public async Task SameModel_ReusesTheRunningServer()
    {
        await using var h = new Harness();

        using var first = await h.Host.AcquireAsync(InferenceRole.Translator, Fast, CancellationToken.None);
        using var second = await h.Host.AcquireAsync(InferenceRole.Translator, Fast, CancellationToken.None);

        Assert.Single(h.Launcher.Started);
        Assert.Same(first.Client, second.Client);
    }

    [Fact]
    public async Task DifferentModel_ReplacesTheServerOfThatRole()
    {
        await using var h = new Harness();
        (await h.Host.AcquireAsync(InferenceRole.Translator, Fast, CancellationToken.None)).Dispose();

        using var lease = await h.Host.AcquireAsync(InferenceRole.Translator, Quality, CancellationToken.None);

        Assert.Equal(2, h.Launcher.Started.Count);
        Assert.True(h.Launcher.Started[0].Process.Disposed);
        Assert.Contains("Hy-MT2-7B-Q4_K_M.gguf", h.Launcher.Started[1].Launch.Arguments[1]);
    }

    [Fact]
    public async Task AServerThatExited_IsRestartedByTheNextAcquire()
    {
        await using var h = new Harness();
        (await h.Host.AcquireAsync(InferenceRole.Translator, Fast, CancellationToken.None)).Dispose();
        h.Launcher.Started[0].Process.HasExited = true;

        using var lease = await h.Host.AcquireAsync(InferenceRole.Translator, Fast, CancellationToken.None);

        Assert.Equal(2, h.Launcher.Started.Count);
        Assert.Equal("http://127.0.0.1:41001/", h.Launcher.Started[1].Launch.Endpoint.ToString());
    }

    [Fact]
    public async Task MissingRuntime_IsAnExplicitError_AndNothingIsStarted()
    {
        await using var h = new Harness();
        h.Models.HasAllExpectedLocalAssets(h.Runtime).Returns(false);

        var ex = await Assert.ThrowsAsync<ModelNotReadyException>(
            () => h.Host.AcquireAsync(InferenceRole.Translator, Fast, CancellationToken.None));

        Assert.Equal(h.Runtime.Id, ex.ModelId);
        Assert.Empty(h.Launcher.Started);
    }

    [Fact]
    public async Task MissingModel_IsAnExplicitError_AndNothingIsStarted()
    {
        await using var h = new Harness();
        h.Models.HasAllExpectedLocalAssets(Fast).Returns(false);

        var ex = await Assert.ThrowsAsync<ModelNotReadyException>(
            () => h.Host.AcquireAsync(InferenceRole.Translator, Fast, CancellationToken.None));

        Assert.Equal(Fast.Id, ex.ModelId);
        Assert.Empty(h.Launcher.Started);
    }

    [Fact]
    public async Task ServerThatExitsDuringStart_FailsWithItsExitCode()
    {
        await using var h = new Harness();
        h.Launcher.ExitImmediately = true;

        var ex = await Assert.ThrowsAsync<LlamaServerStartException>(
            () => h.Host.AcquireAsync(InferenceRole.Translator, Fast, CancellationToken.None));

        Assert.Contains("code 3", ex.Message);
        Assert.True(h.Launcher.Started[0].Process.Disposed);
    }

    [Fact]
    public async Task ServerThatNeverBecomesReady_TimesOut_AndIsStopped()
    {
        await using var h = new Harness(new LlamaServerHostOptions(
            TimeSpan.FromMinutes(10), TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(1)));
        h.Probe.ReadyAfterPolls = int.MaxValue;

        await Assert.ThrowsAsync<LlamaServerStartException>(
            () => h.Host.AcquireAsync(InferenceRole.Translator, Fast, CancellationToken.None));

        Assert.True(h.Launcher.Started[0].Process.Disposed);
    }

    [Fact]
    public async Task CancelledStart_StopsTheServer()
    {
        await using var h = new Harness();
        h.Probe.ReadyAfterPolls = int.MaxValue;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => h.Host.AcquireAsync(InferenceRole.Translator, Fast, cts.Token));

        Assert.True(h.Launcher.Started[0].Process.Disposed);
    }

    [Fact]
    public async Task AfterTheLastLeaseIsReleased_TheServerIsUnloadedWhenIdle()
    {
        await using var h = new Harness();
        var lease = await h.Host.AcquireAsync(InferenceRole.Translator, Fast, CancellationToken.None);
        Assert.Equal(Timeout.InfiniteTimeSpan, h.Time.Timer.Due);

        lease.Dispose();
        Assert.Equal(TimeSpan.FromMinutes(10), h.Time.Timer.Due);
        h.Time.Timer.Fire();

        await WaitUntil(() => h.Launcher.Started[0].Process.Disposed);
        using var again = await h.Host.AcquireAsync(InferenceRole.Translator, Fast, CancellationToken.None);
        Assert.Equal(2, h.Launcher.Started.Count);
    }

    [Fact]
    public async Task AServerWithActiveLeases_IsNeverUnloaded()
    {
        await using var h = new Harness();
        var first = await h.Host.AcquireAsync(InferenceRole.Translator, Fast, CancellationToken.None);
        var second = await h.Host.AcquireAsync(InferenceRole.Translator, Fast, CancellationToken.None);

        first.Dispose();
        Assert.Equal(Timeout.InfiniteTimeSpan, h.Time.Timer.Due);
        h.Time.Timer.Fire();
        await Task.Delay(50);

        Assert.False(h.Launcher.Started[0].Process.Disposed);
        second.Dispose();
        Assert.Equal(TimeSpan.FromMinutes(10), h.Time.Timer.Due);
    }

    [Fact]
    public async Task DisposingALeaseTwice_ReleasesItOnce()
    {
        await using var h = new Harness();
        var first = await h.Host.AcquireAsync(InferenceRole.Translator, Fast, CancellationToken.None);
        var second = await h.Host.AcquireAsync(InferenceRole.Translator, Fast, CancellationToken.None);

        first.Dispose();
        first.Dispose();

        Assert.Equal(Timeout.InfiniteTimeSpan, h.Time.Timer.Due);
        second.Dispose();
    }

    [Fact]
    public async Task ALeaseReleasedAfterTheServerWasReplaced_DoesNotFail()
    {
        await using var h = new Harness();
        var old = await h.Host.AcquireAsync(InferenceRole.Translator, Fast, CancellationToken.None);
        using var replacement = await h.Host.AcquireAsync(InferenceRole.Translator, Quality, CancellationToken.None);

        old.Dispose();

        Assert.True(h.Launcher.Started[0].Process.Disposed);
    }

    [Fact]
    public async Task Dispose_StopsEveryServer_AndRejectsNewRequests()
    {
        var h = new Harness();
        (await h.Host.AcquireAsync(InferenceRole.Translator, Fast, CancellationToken.None)).Dispose();

        await h.DisposeAsync();

        Assert.True(h.Launcher.Started[0].Process.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => h.Host.AcquireAsync(InferenceRole.Translator, Fast, CancellationToken.None));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(5);
        }
    }
}
