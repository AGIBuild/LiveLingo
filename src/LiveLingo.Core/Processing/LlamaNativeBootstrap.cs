using LLama.Native;

namespace LiveLingo.Core.Processing;

/// <summary>
/// Registers extra native library search roots before LLamaSharp loads libllama / mtmd.
/// Use this to supply a newer llama.cpp build that stays API-compatible with the referenced LLamaSharp version
/// (same layout as the LLamaSharp.Backend.* NuGet: folder containing <c>LLamaSharpRuntimes/…</c>).
/// </summary>
public static class LlamaNativeBootstrap
{
    /// <summary>
    /// When set, this directory is registered first (highest priority among user-supplied paths).
    /// </summary>
    public const string SearchPathEnvironmentVariable = "LIVELINGO_LLAMA_NATIVE_PATH";

    /// <summary>
    /// Must run before any other LLamaSharp API that touches native code (e.g. before resolving <see cref="QwenModelHost"/>).
    /// </summary>
    /// <param name="settingsPath">Optional path from user settings (Advanced).</param>
    /// <param name="environmentPath">Optional path from <see cref="SearchPathEnvironmentVariable"/>.</param>
    public static void ApplySearchPathOverrides(string? settingsPath, string? environmentPath)
    {
        foreach (var dir in EnumerateExistingDirectories(environmentPath, settingsPath))
        {
            NativeLibraryConfig.LLama.WithSearchDirectory(dir);
            NativeLibraryConfig.Mtmd.WithSearchDirectory(dir);
        }
    }

    private static IEnumerable<string> EnumerateExistingDirectories(string? first, string? second)
    {
        foreach (var raw in new[] { first, second })
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var trimmed = raw.Trim();
            string full;
            try
            {
                full = Path.GetFullPath(trimmed);
            }
            catch (ArgumentException)
            {
                continue;
            }
            catch (NotSupportedException)
            {
                continue;
            }

            if (Directory.Exists(full))
                yield return full;
        }
    }
}
