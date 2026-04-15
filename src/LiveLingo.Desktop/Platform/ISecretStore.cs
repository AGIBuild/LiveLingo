namespace LiveLingo.Desktop.Platform;

public interface ISecretStore
{
    Task<string?> GetSecretAsync(string secretId, CancellationToken ct = default);
    Task SetSecretAsync(string secretId, string secret, CancellationToken ct = default);
    Task DeleteSecretAsync(string secretId, CancellationToken ct = default);
}
