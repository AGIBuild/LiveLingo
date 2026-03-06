using System.Net;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LiveLingo.Core.Tests.Models;

public class ModelManagerEdgeCaseTests : IDisposable
{
    private readonly string _tempDir;

    public ModelManagerEdgeCaseTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"LiveLingoTest_{Guid.NewGuid():N}");
    }

    private ModelManager CreateManager(HttpMessageHandler? handler = null)
    {
        Directory.CreateDirectory(_tempDir);
        var options = Options.Create(new CoreOptions { ModelStoragePath = _tempDir });
        var http = new HttpClient(handler ?? new FakeHttpMessageHandler());
        var logger = Substitute.For<ILogger<ModelManager>>();
        return new ModelManager(options, http, logger);
    }

    [Fact]
    public void ListInstalled_ReturnsEmpty_WhenDirNotExists()
    {
        var options = Options.Create(new CoreOptions { ModelStoragePath = Path.Combine(_tempDir, "nonexist") });
        var http = new HttpClient(new FakeHttpMessageHandler());
        var logger = Substitute.For<ILogger<ModelManager>>();
        var mgr = new ModelManager(options, http, logger);

        Assert.Empty(mgr.ListInstalled());
    }

    [Fact]
    public void GetTotalDiskUsage_ReturnsZero_WhenDirNotExists()
    {
        var options = Options.Create(new CoreOptions { ModelStoragePath = Path.Combine(_tempDir, "nonexist") });
        var http = new HttpClient(new FakeHttpMessageHandler());
        var logger = Substitute.For<ILogger<ModelManager>>();
        var mgr = new ModelManager(options, http, logger);

        Assert.Equal(0, mgr.GetTotalDiskUsage());
    }

    [Fact]
    public async Task EnsureModelAsync_ConcurrentCalls_OnlyDownloadsOnce()
    {
        var callCount = 0;
        var handler = new CountingHandler(() => Interlocked.Increment(ref callCount));
        var mgr = CreateManager(handler);
        var desc = new ModelDescriptor("concurrent", "Test", "http://fake/m", 100, ModelType.Translation);

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => mgr.EnsureModelAsync(desc, null, CancellationToken.None));
        await Task.WhenAll(tasks);

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task EnsureModelAsync_ThrowsOnCancellation()
    {
        using var cts = new CancellationTokenSource();
        var handler = new SlowHandler();
        var mgr = CreateManager(handler);
        var desc = new ModelDescriptor("cancel", "Test", "http://fake/m", 100, ModelType.Translation);

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => mgr.EnsureModelAsync(desc, null, cts.Token));
    }

    [Fact]
    public async Task EnsureModelAsync_ResumesFromPartFile()
    {
        var mgr = CreateManager();
        var modelDir = Path.Combine(_tempDir, "resume-test");
        Directory.CreateDirectory(modelDir);

        var partPath = Path.Combine(modelDir, "manifest.json.part");
        await File.WriteAllBytesAsync(partPath, new byte[50]);

        var desc = new ModelDescriptor("resume-test", "Resume", "http://fake/model", 200, ModelType.Translation);
        await mgr.EnsureModelAsync(desc, null, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(modelDir, "manifest.json")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var content = new byte[100];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly Action _onCall;
        public CountingHandler(Action onCall) => _onCall = onCall;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            _onCall();
            await Task.Delay(50, ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[100])
            };
        }
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(5000, ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[100])
            };
        }
    }
}
