using System.Formats.Tar;
using System.IO.Compression;
using LiveLingo.Core.Models;
using LiveLingo.Core.Models.Downloads;
using Microsoft.Extensions.Logging.Abstractions;
using SharpCompress.Common;
using SharpCompress.Writers;

namespace LiveLingo.Core.Tests.Models.Downloads;

public class ModelArchiveExtractorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "extractor-" + Guid.NewGuid().ToString("N"));
    private readonly ModelArchiveExtractor _extractor = new(NullLogger.Instance);

    public ModelArchiveExtractorTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static ModelDescriptor Descriptor(ModelArchiveType type, string archiveName) => new(
        "archive-model", "Archive", ModelType.Runtime, "MIT",
        [new ModelAsset(archiveName, "https://example.com/" + archiveName, 1, new string('0', 64))])
    {
        ArchiveType = type
    };

    private string Stage(string name)
    {
        var source = Path.Combine(_dir, "stage-" + name);
        Directory.CreateDirectory(Path.Combine(source, "bundle", "lib"));
        File.WriteAllText(Path.Combine(source, "bundle", "server"), "binary");
        File.WriteAllText(Path.Combine(source, "bundle", "lib", "a.txt"), "lib");
        return source;
    }

    private string TarGz(string source, string name)
    {
        var path = Path.Combine(_dir, name);
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        TarFile.CreateFromDirectory(source, gzip, includeBaseDirectory: false);
        return path;
    }

    [Fact]
    public void TarGz_FlattensTheTopLevelDirectory_AndRemovesTheArchive()
    {
        var archive = TarGz(Stage("a"), "bundle.tar.gz");
        var target = Path.Combine(_dir, "out-a");

        _extractor.Extract(Descriptor(ModelArchiveType.TarGz, "bundle.tar.gz"), archive, target);

        Assert.Equal("binary", File.ReadAllText(Path.Combine(target, "server")));
        Assert.Equal("lib", File.ReadAllText(Path.Combine(target, "lib", "a.txt")));
        Assert.False(File.Exists(archive));
        Assert.Empty(Directory.GetDirectories(target, ".extract*"));
    }

    [Fact]
    public void Zip_KeepsFilesAtTheRoot_WhenThereIsNoSingleTopLevelDirectory()
    {
        var source = Path.Combine(_dir, "stage-zip");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "llama-server.exe"), "exe");
        File.WriteAllText(Path.Combine(source, "ggml.dll"), "dll");
        var archive = Path.Combine(_dir, "bundle.zip");
        ZipFile.CreateFromDirectory(source, archive);
        var target = Path.Combine(_dir, "out-zip");

        _extractor.Extract(Descriptor(ModelArchiveType.Zip, "bundle.zip"), archive, target);

        Assert.True(File.Exists(Path.Combine(target, "llama-server.exe")));
        Assert.True(File.Exists(Path.Combine(target, "ggml.dll")));
        Assert.False(File.Exists(archive));
    }

    [Fact]
    public void TarBz2_IsExtractedThroughTheSameFlattening()
    {
        var source = Stage("bz2");
        var archive = Path.Combine(_dir, "bundle.tar.bz2");
        using (var file = File.Create(archive))
        using (var writer = WriterFactory.Open(file, ArchiveType.Tar, new WriterOptions(CompressionType.BZip2)))
        {
            writer.WriteAll(source, "*", SearchOption.AllDirectories);
        }
        var target = Path.Combine(_dir, "out-bz2");

        _extractor.Extract(Descriptor(ModelArchiveType.TarBz2, "bundle.tar.bz2"), archive, target);

        Assert.Equal("binary", File.ReadAllText(Path.Combine(target, "server")));
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void TarGz_PreservesTheExecutableBit()
    {
        var source = Stage("exec");
        File.SetUnixFileMode(Path.Combine(source, "bundle", "server"),
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var archive = TarGz(source, "exec.tar.gz");
        var target = Path.Combine(_dir, "out-exec");

        _extractor.Extract(Descriptor(ModelArchiveType.TarGz, "exec.tar.gz"), archive, target);

        Assert.True(File.GetUnixFileMode(Path.Combine(target, "server")).HasFlag(UnixFileMode.UserExecute));
    }

    [Fact]
    public void ReplacesAPreviousExtraction()
    {
        var archive = TarGz(Stage("again"), "again.tar.gz");
        var target = Path.Combine(_dir, "out-again");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "server"), "old");

        _extractor.Extract(Descriptor(ModelArchiveType.TarGz, "again.tar.gz"), archive, target);

        Assert.Equal("binary", File.ReadAllText(Path.Combine(target, "server")));
    }

    [Fact]
    public void MissingArchive_IsAnError()
    {
        Assert.Throws<FileNotFoundException>(() => _extractor.Extract(
            Descriptor(ModelArchiveType.Zip, "none.zip"), Path.Combine(_dir, "none.zip"), _dir));
    }

    [Fact]
    public void ADescriptorWithoutAnArchive_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => _extractor.Extract(
            Descriptor(ModelArchiveType.None, "x.bin"), Path.Combine(_dir, "x.bin"), _dir));
    }
}
