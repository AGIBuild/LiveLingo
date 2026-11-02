namespace LiveLingo.Core.Models;

/// <summary>A downloaded file does not match the SHA256 pinned in the model catalog.</summary>
public sealed class ModelIntegrityException(string modelId, string relativePath, string expected, string actual)
    : Exception($"Model '{modelId}' asset '{relativePath}' failed integrity verification: expected SHA256 {expected}, got {actual}.")
{
    public string ModelId { get; } = modelId;
    public string RelativePath { get; } = relativePath;
    public string Expected { get; } = expected;
    public string Actual { get; } = actual;
}
