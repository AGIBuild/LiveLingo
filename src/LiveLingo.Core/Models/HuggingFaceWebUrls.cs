using System.Diagnostics.CodeAnalysis;
using LiveLingo.HfGguf;

namespace LiveLingo.Core.Models;

/// <summary>
/// Builds Hugging Face Hub model card URLs from resolve download URLs.
/// </summary>
public static class HuggingFaceWebUrls
{
    /// <summary>
    /// Returns <c>https://huggingface.co/{org}/{repo}</c> when <paramref name="downloadUrl"/> is a HF <c>/resolve/</c> URL.
    /// </summary>
    public static bool TryGetModelCardUrl(string downloadUrl, [NotNullWhen(true)] out string? modelCardUrl)
    {
        modelCardUrl = null;
        if (!HuggingFaceResolveUrl.TryParse(downloadUrl, out var repoId, out _, out _))
            return false;
        if (string.IsNullOrWhiteSpace(repoId))
            return false;
        modelCardUrl = $"https://huggingface.co/{repoId.Trim().Trim('/')}";
        return true;
    }
}
