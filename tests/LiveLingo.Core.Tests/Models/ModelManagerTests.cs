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
    private readonly ILogger<ModelManager> _logger;

    public ModelManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"LiveLingoTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var options = Options.Create(new CoreOptions { ModelStoragePath = _tempDir });
        var handler = new FakeHttpMessageHandler();
        var http = new HttpClient(handler);
        _logger = Substitute.For<ILogger<ModelManager>>();

        _manager = new ModelManager(options, http, _logger);
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
        var desc = new ModelDescriptor("resume-test", "Resume", "http://fake/model.bin", 200, ModelType.Translation);
        var modelDir = Path.Combine(_tempDir, "resume-test");
        Directory.CreateDirectory(modelDir);
        var partPath = Path.Combine(modelDir, "model.bin.part");
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
        Assert.True(File.Exists(Path.Combine(modelDir, "model.bin")));
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

    [Fact]
    public async Task EnsureModelAsync_SkipsDownload_LogsDebug_WhenAlreadyInstalled()
    {
        var desc = new ModelDescriptor("log-test", "Test", "http://fake/model", 100, ModelType.Translation);

        await _manager.EnsureModelAsync(desc, null, CancellationToken.None);
        await _manager.EnsureModelAsync(desc, null, CancellationToken.None);

        _logger.Received().Log(
            LogLevel.Debug,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task EnsureModelAsync_LogsDebug_WhenAlreadyInstalled_WithModelId()
    {
        var desc = new ModelDescriptor("dbg-log", "Test", "http://fake/model", 100, ModelType.Translation);
        await _manager.EnsureModelAsync(desc, null, CancellationToken.None);
        _logger.ClearReceivedCalls();

        await _manager.EnsureModelAsync(desc, null, CancellationToken.None);

        _logger.Received().Log(
            LogLevel.Debug,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("dbg-log")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task EnsureModelAsync_LogsInformation_AfterDownload()
    {
        var desc = new ModelDescriptor("info-log", "Test", "http://fake/model", 100, ModelType.Translation);
        await _manager.EnsureModelAsync(desc, null, CancellationToken.None);

        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task DeleteModelAsync_LogsInformation()
    {
        var desc = new ModelDescriptor("del-log", "Del", "http://fake/model", 100, ModelType.Translation);
        await _manager.EnsureModelAsync(desc, null, CancellationToken.None);
        _logger.ClearReceivedCalls();

        await _manager.DeleteModelAsync("del-log", CancellationToken.None);

        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public void ListInstalled_LogsWarning_ForCorruptManifest()
    {
        var dir = Path.Combine(_tempDir, "warn-test");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), "not valid json");

        _manager.ListInstalled();

        _logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task EnsureModelAsync_ProgressReports_AccumulateBytes()
    {
        var desc = new ModelDescriptor("progress-accum", "Test", "http://fake/model", 200, ModelType.Translation);
        var reports = new List<ModelDownloadProgress>();
        var syncProgress = new SyncProgress<ModelDownloadProgress>(r => reports.Add(r));

        await _manager.EnsureModelAsync(desc, syncProgress, CancellationToken.None);

        Assert.NotEmpty(reports);
        Assert.True(reports[^1].BytesDownloaded > 0);
        Assert.Equal("progress-accum", reports[^1].ModelId);
    }

    [Fact]
    public async Task EnsureModelAsync_NewDownload_UsesCreateMode()
    {
        var desc = new ModelDescriptor("create-mode", "Test", "http://fake/model", 100, ModelType.Translation);
        await _manager.EnsureModelAsync(desc, null, CancellationToken.None);

        var manifestPath = Path.Combine(_tempDir, "create-mode", "manifest.json");
        Assert.True(File.Exists(manifestPath));

        var partPath = manifestPath + ".part";
        Assert.False(File.Exists(partPath));
    }

    [Fact]
    public async Task EnsureModelAsync_WritesManifestContent()
    {
        var desc = new ModelDescriptor("manifest-write", "MW Test", "http://fake/model", 100, ModelType.Translation);
        await _manager.EnsureModelAsync(desc, null, CancellationToken.None);

        var manifestPath = Path.Combine(_tempDir, "manifest-write", "manifest.json");
        var content = File.ReadAllText(manifestPath);
        Assert.Contains("manifest-write", content);
        Assert.Contains("MW Test", content);
    }

    [Fact]
    public void ListInstalled_ReturnsCorrectFieldValues()
    {
        var dir = Path.Combine(_tempDir, "fields-test");
        Directory.CreateDirectory(dir);
        var manifest = new ModelManifest("fields-test", "Fields Display", 12345,
            ModelType.PostProcessing, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.WriteAllText(Path.Combine(dir, "manifest.json"), manifest.ToJson());

        var installed = _manager.ListInstalled();
        Assert.Single(installed);
        var m = installed[0];
        Assert.Equal("fields-test", m.Id);
        Assert.Equal("Fields Display", m.DisplayName);
        Assert.Equal(12345, m.SizeBytes);
        Assert.Equal(ModelType.PostProcessing, m.Type);
    }

    [Fact]
    public void GetTotalDiskUsage_SumsAllFiles()
    {
        var dir1 = Path.Combine(_tempDir, "size-a");
        var dir2 = Path.Combine(_tempDir, "size-b");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);
        File.WriteAllBytes(Path.Combine(dir1, "data.bin"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(dir2, "data.bin"), new byte[2000]);

        var usage = _manager.GetTotalDiskUsage();
        Assert.True(usage >= 3000);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public SyncProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }

    [Fact]
    public async Task MigrateStoragePathAsync_MovesModelDirectories()
    {
        var modelDir = Path.Combine(_tempDir, "test-model");
        Directory.CreateDirectory(modelDir);
        await File.WriteAllTextAsync(Path.Combine(modelDir, "model.bin"), "data");
        await File.WriteAllTextAsync(Path.Combine(modelDir, "manifest.json"), "{}");

        var newPath = Path.Combine(Path.GetTempPath(), $"LiveLingoMigrate_{Guid.NewGuid():N}");
        try
        {
            await _manager.MigrateStoragePathAsync(newPath);

            Assert.True(Directory.Exists(Path.Combine(newPath, "test-model")));
            Assert.True(File.Exists(Path.Combine(newPath, "test-model", "model.bin")));
            Assert.False(Directory.Exists(modelDir));
        }
        finally
        {
            if (Directory.Exists(newPath)) Directory.Delete(newPath, true);
        }
    }

    [Fact]
    public async Task MigrateStoragePathAsync_NoOp_WhenSamePath()
    {
        var modelDir = Path.Combine(_tempDir, "keep-model");
        Directory.CreateDirectory(modelDir);
        await File.WriteAllTextAsync(Path.Combine(modelDir, "model.bin"), "data");

        await _manager.MigrateStoragePathAsync(_tempDir);

        Assert.True(Directory.Exists(modelDir));
    }

    [Fact]
    public async Task MigrateStoragePathAsync_HandlesEmptySource()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), $"LiveLingoEmpty_{Guid.NewGuid():N}");
        var newPath = Path.Combine(Path.GetTempPath(), $"LiveLingoNew_{Guid.NewGuid():N}");
        var options = Options.Create(new CoreOptions { ModelStoragePath = emptyDir });
        var mgr = new ModelManager(options, new HttpClient(new FakeHttpMessageHandler()), _logger);

        await mgr.MigrateStoragePathAsync(newPath);

        Assert.False(Directory.Exists(emptyDir));
    }

    [Fact]
    public async Task EnsureModelAsync_WritesFileContent()
    {
        var desc = new ModelDescriptor("content-test", "Test", "http://fake/model.bin", 100, ModelType.Translation);
        await _manager.EnsureModelAsync(desc, null, CancellationToken.None);

        var modelFile = Path.Combine(_tempDir, "content-test", "model.bin");
        Assert.True(File.Exists(modelFile));
        var content = await File.ReadAllBytesAsync(modelFile);
        Assert.True(content.Length > 0, "Downloaded file should not be empty");
    }

    [Fact]
    public async Task EnsureModelAsync_SkipsDownload_WhenAlreadyInstalled_NoHttpCall()
    {
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(_ => callCount++);
        var options = Options.Create(new CoreOptions { ModelStoragePath = _tempDir });
        var mgr = new ModelManager(options, new HttpClient(handler), _logger);
        var desc = new ModelDescriptor("skip-test", "Test", "http://fake/model", 100, ModelType.Translation);

        await mgr.EnsureModelAsync(desc, null, CancellationToken.None);
        var firstCount = callCount;

        await mgr.EnsureModelAsync(desc, null, CancellationToken.None);
        Assert.Equal(firstCount, callCount);
    }

    [Theory]
    [InlineData("http://example.com/weights.gguf", "weights.gguf")]
    [InlineData("http://example.com/path/model.bin", "model.bin")]
    [InlineData("http://example.com/", "model.bin")]
    public async Task EnsureModelAsync_ExtractsCorrectFileName(string url, string expectedFile)
    {
        var desc = new ModelDescriptor("fname-test", "Test", url, 100, ModelType.Translation);
        await _manager.EnsureModelAsync(desc, null, CancellationToken.None);

        var modelDir = Path.Combine(_tempDir, "fname-test");
        Assert.True(File.Exists(Path.Combine(modelDir, expectedFile)),
            $"Expected file {expectedFile} in model directory");

        Directory.Delete(modelDir, true);
    }

    [Fact]
    public async Task MigrateStoragePathAsync_LogsInfoOnSuccess()
    {
        var modelDir = Path.Combine(_tempDir, "log-migrate");
        Directory.CreateDirectory(modelDir);
        await File.WriteAllTextAsync(Path.Combine(modelDir, "data.bin"), "x");
        _logger.ClearReceivedCalls();

        var newPath = Path.Combine(Path.GetTempPath(), $"LiveLingoLogMigrate_{Guid.NewGuid():N}");
        try
        {
            await _manager.MigrateStoragePathAsync(newPath);

            _logger.Received().Log(
                LogLevel.Information,
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>());
        }
        finally
        {
            if (Directory.Exists(newPath)) Directory.Delete(newPath, true);
        }
    }

    [Fact]
    public async Task MigrateStoragePathAsync_LogsInfoWhenSourceMissing()
    {
        var nonExistent = Path.Combine(_tempDir, "does_not_exist_src");
        var newPath = Path.Combine(Path.GetTempPath(), $"LiveLingoMissing_{Guid.NewGuid():N}");
        var options = Options.Create(new CoreOptions { ModelStoragePath = nonExistent });
        var logger = Substitute.For<ILogger<ModelManager>>();
        var mgr = new ModelManager(options, new HttpClient(new FakeHttpMessageHandler()), logger);

        await mgr.MigrateStoragePathAsync(newPath);

        logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task MigrateStoragePathAsync_PreservesNestedDirectories()
    {
        var modelDir = Path.Combine(_tempDir, "nested-model");
        var subDir = Path.Combine(modelDir, "subdir");
        Directory.CreateDirectory(subDir);
        await File.WriteAllTextAsync(Path.Combine(modelDir, "root.bin"), "root");
        await File.WriteAllTextAsync(Path.Combine(subDir, "nested.bin"), "nested");

        var newPath = Path.Combine(Path.GetTempPath(), $"LiveLingoNested_{Guid.NewGuid():N}");
        try
        {
            await _manager.MigrateStoragePathAsync(newPath);

            Assert.True(File.Exists(Path.Combine(newPath, "nested-model", "root.bin")));
            Assert.True(File.Exists(Path.Combine(newPath, "nested-model", "subdir", "nested.bin")));
            Assert.False(Directory.Exists(modelDir));
        }
        finally
        {
            if (Directory.Exists(newPath)) Directory.Delete(newPath, true);
        }
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
