using System.Diagnostics.CodeAnalysis;

namespace LiveLingo.HfGguf;

/// <summary>
/// Parses Hugging Face "resolve" download URLs into repo id, revision, and file path.
/// </summary>
public static class HuggingFaceResolveUrl
{
    /// <summary>
    /// Expects <c>https://{host}/{namespace}/{repo}/resolve/{revision}/{filePath}</c> (file path may contain slashes).
    /// </summary>
    public static bool TryParse(
        string absoluteUrl,
        [NotNullWhen(true)] out string? repoId,
        out string revision,
        [NotNullWhen(true)] out string? filePath)
    {
        repoId = null;
        revision = "";
        filePath = null;
        if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out var uri))
            return false;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var resolveIdx = Array.FindIndex(segments, s => s.Equals("resolve", StringComparison.OrdinalIgnoreCase));
        if (resolveIdx < 2 || resolveIdx + 1 >= segments.Length)
            return false;

        repoId = string.Join('/', segments[..resolveIdx]);
        revision = segments[resolveIdx + 1];
        filePath = string.Join('/', segments[(resolveIdx + 2)..]);
        return !string.IsNullOrEmpty(repoId)
               && !string.IsNullOrEmpty(revision)
               && !string.IsNullOrEmpty(filePath);
    }
}
