namespace LiveLingo.HfGguf;

public interface IHfFileLister
{
    Task<IReadOnlyList<string>> ListGgufPathsAsync(
        string repoId,
        string revision,
        string? bearerToken,
        CancellationToken cancellationToken = default);
}
