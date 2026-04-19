using LiveLingo.Core;
using LiveLingo.Core.Models;
using LiveLingo.Core.Models.Installations;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiveLingo.Core.Tests.Models;

public class ObsoleteModelCleanerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ObsoleteModelCleaner _cleaner;

    public ObsoleteModelCleanerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"LiveLingoObsoleteTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _cleaner = new ObsoleteModelCleaner(
            new CoreOptions { ModelStoragePath = _tempDir },
            NullLogger.Instance);
    }

    [Fact]
    public async Task CleanAsync_StorageRootMissing_ReturnsZero()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), $"DoesNotExist_{Guid.NewGuid():N}");
        var cleaner = new ObsoleteModelCleaner(
            new CoreOptions { ModelStoragePath = missingRoot },
            NullLogger.Instance);

        var freed = await cleaner.CleanAsync(["whisper-base"], CancellationToken.None);

        Assert.Equal(0, freed);
    }

    [Fact]
    public async Task CleanAsync_ModelDirectoryMissing_ReturnsZeroAndIsNoOp()
    {
        var freed = await _cleaner.CleanAsync(["never-installed"], CancellationToken.None);

        Assert.Equal(0, freed);
        Assert.True(Directory.Exists(_tempDir), "Storage root must be left untouched");
    }

    [Fact]
    public async Task CleanAsync_ExistingDirectory_DeletesAndReturnsByteCount()
    {
        var modelDir = Path.Combine(_tempDir, "whisper-base");
        Directory.CreateDirectory(modelDir);
        var payloadPath = Path.Combine(modelDir, "ggml-base.bin");
        await File.WriteAllBytesAsync(payloadPath, new byte[1024]);

        var freed = await _cleaner.CleanAsync(["whisper-base"], CancellationToken.None);

        Assert.Equal(1024, freed);
        Assert.False(Directory.Exists(modelDir));
    }

    [Fact]
    public async Task CleanAsync_MultipleIds_AggregatesByteCounts()
    {
        await CreateModelDirectoryWithFile("model-a", "weights.bin", 512);
        await CreateModelDirectoryWithFile("model-b", "weights.bin", 1024);
        await CreateModelDirectoryWithFile("model-c", "weights.bin", 2048);

        var freed = await _cleaner.CleanAsync(["model-a", "model-b", "model-c"], CancellationToken.None);

        Assert.Equal(3584, freed);
        Assert.False(Directory.Exists(Path.Combine(_tempDir, "model-a")));
        Assert.False(Directory.Exists(Path.Combine(_tempDir, "model-b")));
        Assert.False(Directory.Exists(Path.Combine(_tempDir, "model-c")));
    }

    [Fact]
    public async Task CleanAsync_PartiallyExisting_OnlyDeletesPresent()
    {
        await CreateModelDirectoryWithFile("present-model", "weights.bin", 800);

        var freed = await _cleaner.CleanAsync(
            ["present-model", "missing-model"], CancellationToken.None);

        Assert.Equal(800, freed);
        Assert.False(Directory.Exists(Path.Combine(_tempDir, "present-model")));
    }

    [Fact]
    public async Task CleanAsync_SkipsBlankIds()
    {
        await CreateModelDirectoryWithFile("real-id", "weights.bin", 256);

        var freed = await _cleaner.CleanAsync(
            [string.Empty, "  ", "real-id"], CancellationToken.None);

        Assert.Equal(256, freed);
    }

    [Fact]
    public async Task CleanAsync_RecursesIntoSubdirectories()
    {
        var modelDir = Path.Combine(_tempDir, "nested-model");
        var subDir = Path.Combine(modelDir, "shards");
        Directory.CreateDirectory(subDir);
        await File.WriteAllBytesAsync(Path.Combine(modelDir, "manifest.json"), new byte[100]);
        await File.WriteAllBytesAsync(Path.Combine(subDir, "shard-0.bin"), new byte[200]);

        var freed = await _cleaner.CleanAsync(["nested-model"], CancellationToken.None);

        Assert.Equal(300, freed);
        Assert.False(Directory.Exists(modelDir));
    }

    private async Task CreateModelDirectoryWithFile(string id, string fileName, int sizeBytes)
    {
        var dir = Path.Combine(_tempDir, id);
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, fileName), new byte[sizeBytes]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }
}
