using LiveLingo.Core.Inference;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiveLingo.Core.Tests.Inference;

public class ServerPidRegistryTests : IDisposable
{
    private sealed class FakeInspector : IProcessInspector
    {
        public Dictionary<int, string> Running { get; } = [];
        public List<int> Killed { get; } = [];

        public string? NameOf(int pid) => Running.GetValueOrDefault(pid);

        public void Kill(int pid)
        {
            Killed.Add(pid);
            Running.Remove(pid);
        }
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pids-" + Guid.NewGuid().ToString("N"));
    private readonly FakeInspector _inspector = new();

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private ServerPidRegistry Registry() => new(_dir, _inspector, NullLogger<ServerPidRegistry>.Instance);

    [Fact]
    public void RecordedServerStillRunning_IsStopped_AndTheRecordCleared()
    {
        var registry = Registry();
        _inspector.Running[4242] = "llama-server";
        registry.Record(4242);

        Assert.Equal(1, registry.ReapOrphans());

        Assert.Equal([4242], _inspector.Killed);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void ARecycledPid_NamingAnotherProgram_IsNeverKilled()
    {
        var registry = Registry();
        _inspector.Running[4242] = "Safari";
        registry.Record(4242);

        Assert.Equal(0, registry.ReapOrphans());

        Assert.Empty(_inspector.Killed);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void ARecordOfAProcessThatIsGone_IsJustCleared()
    {
        var registry = Registry();
        registry.Record(4242);

        Assert.Equal(0, registry.ReapOrphans());

        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void RemovedRecords_AreNotReaped()
    {
        var registry = Registry();
        _inspector.Running[4242] = "llama-server";
        registry.Record(4242);
        registry.Remove(4242);

        Assert.Equal(0, registry.ReapOrphans());
        Assert.Empty(_inspector.Killed);
    }

    [Fact]
    public void WithoutADirectory_NothingHappens()
    {
        Assert.Equal(0, Registry().ReapOrphans());
        Registry().Remove(1);
    }

    [Fact]
    public void ForeignFiles_AreIgnoredButStillCleared()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "notapid.pid"), "");

        Assert.Equal(0, Registry().ReapOrphans());
    }

    [Fact]
    public void SystemInspector_ReportsTheCurrentProcess_AndNullForMissingOnes()
    {
        var inspector = new SystemProcessInspector();

        Assert.NotNull(inspector.NameOf(Environment.ProcessId));
        Assert.Null(inspector.NameOf(int.MaxValue));
    }
}
