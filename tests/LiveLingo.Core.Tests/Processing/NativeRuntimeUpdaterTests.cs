using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using LiveLingo.Core.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LiveLingo.Core.Tests.Processing;

public sealed class NativeRuntimeUpdaterTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"LiveLingo.NativeUpdater.{Guid.NewGuid():N}");

    public NativeRuntimeUpdaterTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task EnsureLatestLlamaServerAsync_resumes_partial_archive_download()
    {
        const string tagName = "b1234";
        var (assetName, archiveBytes, executableRelativePath) = await CreateArchiveAsync();

        var nativeDir = Path.Combine(_tempDir, "native", tagName);
        Directory.CreateDirectory(nativeDir);
        var archivePath = Path.Combine(nativeDir, assetName);

        var splitIndex = Math.Max(1, archiveBytes.Length / 3);
        await File.WriteAllBytesAsync(archivePath, archiveBytes[..splitIndex]);

        var rangeHeader = default(RangeHeaderValue);
        var requestedGetUrl = string.Empty;
        using var http = new HttpClient(new FakeRuntimeHandler(
            tagName,
            archiveBytes,
            h => rangeHeader = h,
            url => requestedGetUrl = url));
        var logger = new ListLogger<NativeRuntimeUpdater>();
        var updater = new NativeRuntimeUpdater(
            http,
            Options.Create(new CoreOptions { ModelStoragePath = _tempDir }),
            logger);

        var executablePath = await updater.EnsureLatestLlamaServerAsync(CancellationToken.None);

        Assert.True(executablePath is not null, string.Join(Environment.NewLine, logger.Messages));
        Assert.EndsWith($"/{assetName}", requestedGetUrl);
        Assert.Equal(splitIndex, rangeHeader?.Ranges.Single().From);
        Assert.False(File.Exists(archivePath));
        var expectedExecutablePath = Path.Combine(nativeDir, executableRelativePath);
        Assert.True(File.Exists(expectedExecutablePath));
        Assert.Equal(expectedExecutablePath, executablePath);
    }

    [Fact]
    public async Task EnsureLatestLlamaServerAsync_retries_after_transient_download_failure()
    {
        const string tagName = "b1234";
        var (assetName, archiveBytes, executableRelativePath) = await CreateArchiveAsync();

        var getAttempts = 0;
        using var http = new HttpClient(new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Head)
            {
                return new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    RequestMessage = request,
                    Headers =
                    {
                        Location = new Uri($"https://github.com/ggml-org/llama.cpp/releases/tag/{tagName}")
                    }
                };
            }

            getAttempts++;
            if (getAttempts == 1)
                throw new HttpRequestException("temporary failure");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(archiveBytes)
            };
        }));

        var logger = new ListLogger<NativeRuntimeUpdater>();
        var updater = new NativeRuntimeUpdater(
            http,
            Options.Create(new CoreOptions { ModelStoragePath = _tempDir }),
            logger);

        var executablePath = await updater.EnsureLatestLlamaServerAsync(CancellationToken.None);

        Assert.NotNull(executablePath);
        Assert.Equal(2, getAttempts);
        Assert.Contains(logger.Messages, m => m.Contains("Download attempt 1/3 failed", StringComparison.Ordinal));

        var expectedExecutablePath = Path.Combine(_tempDir, "native", tagName, executableRelativePath);
        Assert.Equal(expectedExecutablePath, executablePath);
        Assert.False(File.Exists(Path.Combine(_tempDir, "native", tagName, assetName)));
    }

    [Fact]
    public async Task EnsureLatestLlamaServerAsync_returns_null_when_release_tag_cannot_be_resolved()
    {
        using var http = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Head, request.Method);
            return new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                RequestMessage = request,
                Headers =
                {
                    Location = new Uri("https://github.com/ggml-org/llama.cpp/releases/latest/download")
                }
            };
        }));

        var logger = new ListLogger<NativeRuntimeUpdater>();
        var updater = new NativeRuntimeUpdater(
            http,
            Options.Create(new CoreOptions { ModelStoragePath = _tempDir }),
            logger);

        var executablePath = await updater.EnsureLatestLlamaServerAsync(CancellationToken.None);

        Assert.Null(executablePath);
        Assert.Contains(logger.Messages, m => m.Contains("Could not determine latest llama.cpp release tag", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task<(string AssetName, byte[] ArchiveBytes, string ExecutableRelativePath)> CreateArchiveAsync()
    {
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "llama-server.exe" : "llama-server";
        var executableRelativePath = Path.Combine("bundle", exeName);
        var stagingDir = Path.Combine(_tempDir, "staging");
        Directory.CreateDirectory(Path.Combine(stagingDir, "bundle"));
        await File.WriteAllTextAsync(Path.Combine(stagingDir, executableRelativePath), "server-binary");

        var (os, arch, ext) = GetPlatformAssetParts();
        var assetName = $"llama-b1234-bin-{os}-{arch}.{ext}";

        if (ext == "zip")
        {
            var zipPath = Path.Combine(_tempDir, "runtime.zip");
            ZipFile.CreateFromDirectory(stagingDir, zipPath);
            return (assetName, await File.ReadAllBytesAsync(zipPath), executableRelativePath);
        }

        var tarPath = Path.Combine(_tempDir, "runtime.tar");
        TarFile.CreateFromDirectory(stagingDir, tarPath, includeBaseDirectory: false);
        await using var tarStream = File.OpenRead(tarPath);
        await using var output = new MemoryStream();
        await using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            await tarStream.CopyToAsync(gzip);
        }

        return (assetName, output.ToArray(), executableRelativePath);
    }

    private static (string Os, string Arch, string Extension) GetPlatformAssetParts()
    {
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            _ => throw new NotSupportedException()
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ("win-cpu", arch, "zip");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return ("macos", arch, "tar.gz");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return ("ubuntu", arch, "tar.gz");

        throw new NotSupportedException();
    }

    private sealed class FakeRuntimeHandler(
        string tagName,
        byte[] fullArchive,
        Action<RangeHeaderValue?> captureRange,
        Action<string> captureGetUrl) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Head)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    RequestMessage = request,
                    Headers =
                    {
                        Location = new Uri($"https://github.com/ggml-org/llama.cpp/releases/tag/{tagName}")
                    }
                });
            }

            Assert.Equal(HttpMethod.Get, request.Method);
            captureGetUrl(request.RequestUri!.AbsoluteUri);

            captureRange(request.Headers.Range);

            if (request.Headers.Range?.Ranges.Single().From is long from)
            {
                var tail = fullArchive[(int)from..];
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(tail)
                };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, fullArchive.Length - 1, fullArchive.Length);
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(fullArchive)
            });
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add($"{logLevel}: {formatter(state, exception)}{(exception is null ? "" : $" :: {exception.GetType().Name}: {exception.Message}")}");
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
