namespace LiveLingo.HfGguf;

/// <summary>
/// Picks a recommended .gguf path for CPU / limited RAM from a file list.
/// </summary>
public static class QuantChooser
{
    private static readonly string[] DefaultOrder =
        ["Q4_K_M", "Q4_K_S", "Q3_K_M", "Q3_K_S", "Q2_K"];

    private static readonly string[] SaferOrder =
        ["Q4_K_S", "Q3_K_M", "Q3_K_S", "Q2_K", "Q4_K_M"];

    public static string? Choose(
        IReadOnlyList<string> ggufPaths,
        bool preferSaferMemory,
        int contextLength,
        string? forcedQuant = null)
    {
        if (ggufPaths.Count == 0)
            return null;

        var safer = preferSaferMemory || contextLength >= 8192;
        if (!string.IsNullOrWhiteSpace(forcedQuant))
        {
            var q = forcedQuant.Trim();
            foreach (var path in ggufPaths.OrderBy(p => p, StringComparer.Ordinal))
            {
                if (Path.GetFileName(path).Contains(q, StringComparison.OrdinalIgnoreCase))
                    return path;
            }

            return null;
        }

        var order = safer ? SaferOrder : DefaultOrder;
        foreach (var token in order)
        {
            foreach (var path in ggufPaths)
            {
                if (Path.GetFileName(path).Contains(token, StringComparison.OrdinalIgnoreCase))
                    return path;
            }
        }

        return ggufPaths.OrderBy(p => p, StringComparer.Ordinal).FirstOrDefault();
    }
}
