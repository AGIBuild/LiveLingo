using LiveLingo.Core.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LiveLingo.Core.Tests.Processing;

public class ProcessorNameTests
{
    private static QwenModelHost CreateHost()
    {
        var opts = Options.Create(new CoreOptions { ModelStoragePath = "/fake" });
        var logger = Substitute.For<ILogger<QwenModelHost>>();
        return new QwenModelHost(opts, logger);
    }

    [Fact]
    public void SummarizeProcessor_Name()
    {
        using var host = CreateHost();
        var proc = new SummarizeProcessor(host, Substitute.For<ILogger<SummarizeProcessor>>());
        Assert.Equal("summarize", proc.Name);
    }

    [Fact]
    public void OptimizeProcessor_Name()
    {
        using var host = CreateHost();
        var proc = new OptimizeProcessor(host, Substitute.For<ILogger<OptimizeProcessor>>());
        Assert.Equal("optimize", proc.Name);
    }

    [Fact]
    public void ColloquializeProcessor_Name()
    {
        using var host = CreateHost();
        var proc = new ColloquializeProcessor(host, Substitute.For<ILogger<ColloquializeProcessor>>());
        Assert.Equal("colloquialize", proc.Name);
    }

    [Fact]
    public void SummarizeProcessor_Dispose_NoThrow()
    {
        using var host = CreateHost();
        var proc = new SummarizeProcessor(host, Substitute.For<ILogger<SummarizeProcessor>>());
        proc.Dispose();
    }

    [Fact]
    public void OptimizeProcessor_Dispose_NoThrow()
    {
        using var host = CreateHost();
        var proc = new OptimizeProcessor(host, Substitute.For<ILogger<OptimizeProcessor>>());
        proc.Dispose();
    }

    [Fact]
    public void ColloquializeProcessor_Dispose_NoThrow()
    {
        using var host = CreateHost();
        var proc = new ColloquializeProcessor(host, Substitute.For<ILogger<ColloquializeProcessor>>());
        proc.Dispose();
    }
}
