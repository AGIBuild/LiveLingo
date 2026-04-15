using System.Collections.Concurrent;

namespace LiveLingo.Desktop.Platform;

public sealed class InMemorySecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public Task<string?> GetSecretAsync(string secretId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_secrets.TryGetValue(secretId, out var secret) ? secret : null);
    }

    public Task SetSecretAsync(string secretId, string secret, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _secrets[secretId] = secret;
        return Task.CompletedTask;
    }

    public Task DeleteSecretAsync(string secretId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _secrets.TryRemove(secretId, out _);
        return Task.CompletedTask;
    }
}
