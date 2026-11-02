using System.Security.Cryptography;
using LiveLingo.Core.Models;

namespace LiveLingo.Core.Tests.Models;

/// <summary>Builds descriptors for tests that download the 100 'x' bytes served by the fake HTTP handlers.</summary>
internal static class TestModel
{
    public static readonly string FakeContentSha256 = Convert.ToHexStringLower(SHA256.HashData(Content(100)));

    public static byte[] Content(int length) => Enumerable.Repeat((byte)'x', length).ToArray();

    public static ModelDescriptor Create(string id, string displayName, string url, long size, ModelType type) =>
        new(id, displayName, type, "test", [Asset(FileName(url), url, size)]);

    public static ModelAsset Asset(string relativePath, string url, long size) =>
        new(relativePath, url, size, FakeContentSha256);

    private static string FileName(string url)
    {
        var name = Path.GetFileName(new Uri(url).AbsolutePath);
        return string.IsNullOrEmpty(name) ? "model.bin" : name;
    }
}
