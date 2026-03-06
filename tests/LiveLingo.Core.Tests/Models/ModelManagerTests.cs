using System.Net;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LiveLingo.Core.Tests.Models;

public class ModelManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ModelManager _manager;

    public ModelManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"LiveLingoTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var options = Options.Create(new CoreOptions { ModelStoragePath = _tempDir });
        var handler = new FakeHttpMessageHandler();
        var http = new HttpClient(handler);
        var logger = Substitute.For<ILogger<ModelManager>>();

        _manager = new ModelManager(options, http, logger);
    }

    [Fact]
    public void ListInstalled_Empty_WhenNoModels()
    {
        Assert.Empty(_manager.ListInstalled());
    }

    [Fact]
    public void GetTotalDiskUsage_Zero_WhenEmpty()
    {
        Assert.Equal(0, _manager.GetTotalDiskUsage());
    }

    [Fact]
    public async Task EnsureModelAsync_DownloadsModel()
    {
        var desc = new ModelDescriptor("test-model", "Test", "http://fake/model", 100, ModelType.Translation);

        await _manager.EnsureModelAsync(desc, null, CancellationToken.None);

        var manifest = Path.Combine(_tempDir, "test-model", "manifest.json");
        Assert.True(File.Exists(manifest));
    }

    [Fact]
    public async Task EnsureModelAsync_SkipsIfAlreadyInstalled()
    {
        var desc = new ModelDescriptor("test-model", "Test", "http://fake/model", 100, ModelType.Translation);

        await _manager.EnsureModelAsync(desc, null, CancellationToken.None);
        await _manager.EnsureModelAsync(desc, null, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_tempDir, "test-model", "manifest.json")));
    }

    [Fact]
    public async Task EnsureModelAsync_ReportsProgress()
    {
        var desc = new ModelDescriptor("progress-test", "Test", "http://fake/model", 100, ModelType.Translation);
        var reports = new List<ModelDownloadProgress>();
        var progress = new Progress<ModelDownloadProgress>(r => reports.Add(r));

        await _manager.EnsureModelAsync(desc, progress, CancellationToken.None);

        await Task.Delay(100);
        Assert.NotEmpty(reports);
        Assert.All(reports, r => Assert.Equal("progress-test", r.ModelId));
    }

    [Fact]
    public async Task ListInstalled_ReturnsModels_AfterDownload()
    {
        var desc = new ModelDescriptor("list-test", "List Test", "http://fake/model", 100, ModelType.Translation);
        await _manager.EnsureModelAsync(desc, null, CancellationToken.None);

        var installed = _manager.ListInstalled();
        Assert.Single(installed);
        Assert.Equal("list-test", installed[0].Id);
    }

    [Fact]
    public async Task DeleteModelAsync_RemovesModel()
    {
        var desc = new ModelDescriptor("delete-test", "Del", "http://fake/model", 100, ModelType.Translation);
        await _manager.EnsureModelAsync(desc, null, CancellationToken.None);

        Assert.NotEmpty(_manager.ListInstalled());

        await _manager.DeleteModelAsync("delete-test", CancellationToken.None);

        Assert.Empty(_manager.ListInstalled());
    }

    [Fact]
    public async Task DeleteModelAsync_NoOp_WhenNotExists()
    {
        await _manager.DeleteModelAsync("nonexistent", CancellationToken.None);
    }

    [Fact]
    public async Task GetTotalDiskUsage_ReturnsNonZero_AfterDownload()
    {
        var desc = new ModelDescriptor("size-test", "Size", "http://fake/model", 100, ModelType.Translation);
        await _manager.EnsureModelAsync(desc, null, CancellationToken.None);

        Assert.True(_manager.GetTotalDiskUsage() > 0);
    }

    [Fact]
    public void GetModelDirectory_ReturnsCorrectPath()
    {
        var expected = Path.Combine(_tempDir, "test-id");
        Assert.Equal(expected, _manager.GetModelDirectory("test-id"));
    }

    [Fact]
    public void ListInstalled_IgnoresCorruptManifest()
    {
        var dir = Path.Combine(_tempDir, "corrupt-model");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), "not valid json {{{");

        Assert.Empty(_manager.ListInstalled());
    }

    [Fact]
    public void ListInstalled_IgnoresDirWithoutManifest()
    {
        var dir = Path.Combine(_tempDir, "no-manifest");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "somefile.dat"), "data");

        Assert.Empty(_manager.ListInstalled());
    }

    [Fact]
    public void ListInstalled_ReturnsEmpty_WhenStoragePathMissing()
    {
        var nonExistent = Path.Combine(_tempDir, "does_not_exist");
        var options = Options.Create(new CoreOptions { ModelStoragePath = nonExistent });
        var mgr = new ModelManager(options, new HttpClient(new FakeHttpMessageHandler()), Substitute.For<ILogger<ModelManager>>());

        Assert.Empty(mgr.ListInstalled());
    }

    [Fact]
    public void GetTotalDiskUsage_ReturnsZero_WhenStoragePathMissing()
    {
        var nonExistent = Path.Combine(_tempDir, "does_not_exist");
        var options = Options.Create(new CoreOptions { ModelStoragePath = nonExistent });
        var mgr = new ModelManager(options, new HttpClient(new FakeHttpMessageHandler()), Substitute.For<ILogger<ModelManager>>());

        Assert.Equal(0, mgr.GetTotalDiskUsage());
    }

    [Fact]
    public async Task EnsureModelAsync_ResumesDownload_WhenPartFileExists()
    {
        var desc = new ModelDescriptor("resume-test", "Resume", "http://fake/model", 200, ModelType.Translation);
        var modelDir = Path.Combine(_tempDir, "resume-test");
        Directory.CreateDirectory(modelDir);
        var partPath = Path.Combine(modelDir, "manifest.json.part");
        await File.WriteAllBytesAsync(partPath, new byte[50]);

        bool rangeRequested = false;
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.Headers.Range is not null)
                rangeRequested = true;
        });
        var options = Options.Create(new CoreOptions { ModelStoragePath = _tempDir });
        var mgr = new ModelManager(options, new HttpClient(handler), Substitute.For<ILogger<ModelManager>>());

        await mgr.EnsureModelAsync(desc, null, CancellationToken.None);

        Assert.True(rangeRequested);
        Assert.True(File.Exists(Path.Combine(modelDir, "manifest.json")));
        Assert.False(File.Exists(partPath));
    }

    [Fact]
    public void ListInstalled_ReturnsNull_SkipsNullManifest()
    {
        var dir = Path.Combine(_tempDir, "null-manifest");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), "null");

        Assert.Empty(_manager.ListInstalled());
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Action<HttpRequestMessage>? _inspector;

        public FakeHttpMessageHandler(Action<HttpRequestMessage>? inspector = null)
        {
            _inspector = inspector;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            _inspector?.Invoke(request);
            var content = new byte[100];
            Array.Fill(content, (byte)'x');
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            };
            return Task.FromResult(response);
        }
    }
}
