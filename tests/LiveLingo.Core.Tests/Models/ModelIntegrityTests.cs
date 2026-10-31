using System.Net;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LiveLingo.Core.Tests.Models;

public class ModelIntegrityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "integrity-" + Guid.NewGuid().ToString("N"));

    private sealed class ConstantHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Enumerable.Repeat((byte)'x', 100).ToArray())
            });
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private ModelManager Manager() => new(
        Options.Create(new CoreOptions { ModelStoragePath = _dir }),
        new HttpClient(new ConstantHandler()),
        NullLogger<ModelManager>.Instance);

    private static ModelDescriptor Descriptor(string sha256) => new(
        "integrity", "Integrity", ModelType.Translation, "test",
        [new ModelAsset("weights.bin", "https://example.com/weights.bin", 100, sha256)]);

    [Fact]
    public async Task MatchingHash_InstallsTheModel()
    {
        await Manager().EnsureModelAsync(Descriptor(TestModel.FakeContentSha256));

        Assert.True(File.Exists(Path.Combine(_dir, "integrity", "weights.bin")));
        Assert.True(File.Exists(Path.Combine(_dir, "integrity", "manifest.json")));
    }

    [Fact]
    public async Task HashComparison_IgnoresCase()
    {
        await Manager().EnsureModelAsync(Descriptor(TestModel.FakeContentSha256.ToUpperInvariant()));

        Assert.True(File.Exists(Path.Combine(_dir, "integrity", "manifest.json")));
    }

    [Fact]
    public async Task MismatchingHash_FailsExplicitly_AndLeavesNothingInstalled()
    {
        var wrong = new string('0', 64);

        var ex = await Assert.ThrowsAsync<ModelIntegrityException>(() => Manager().EnsureModelAsync(Descriptor(wrong)));

        Assert.Equal("integrity", ex.ModelId);
        Assert.Equal("weights.bin", ex.RelativePath);
        Assert.Equal(wrong, ex.Expected);
        Assert.Equal(TestModel.FakeContentSha256, ex.Actual);
        Assert.False(File.Exists(Path.Combine(_dir, "integrity", "weights.bin")));
        Assert.False(File.Exists(Path.Combine(_dir, "integrity", "manifest.json")));
    }
}
