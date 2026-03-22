namespace LiveLingo.HfGguf;

/// <summary>
/// Parses Hugging Face model repo ids ({owner}/{name}).
/// </summary>
public static class HfRepoId
{
    public static (string Namespace, string Name) Split(string repoId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoId);
        var trimmed = repoId.Trim();
        var slash = trimmed.IndexOf('/');
        if (slash <= 0 || slash == trimmed.Length - 1)
            throw new ArgumentException("repoId must be '{namespace}/{repo}'.", nameof(repoId));

        return (trimmed[..slash], trimmed[(slash + 1)..]);
    }
}
