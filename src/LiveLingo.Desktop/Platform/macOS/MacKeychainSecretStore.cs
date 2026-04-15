using System.Diagnostics;
using System.Runtime.Versioning;

namespace LiveLingo.Desktop.Platform.macOS;

[SupportedOSPlatform("macos")]
internal sealed class MacKeychainSecretStore : ISecretStore
{
    private const string ServiceName = "LiveLingo";

    public async Task<string?> GetSecretAsync(string secretId, CancellationToken ct = default)
    {
        var result = await RunSecurityAsync(
            info =>
            {
                info.ArgumentList.Add("find-generic-password");
                info.ArgumentList.Add("-s");
                info.ArgumentList.Add(ServiceName);
                info.ArgumentList.Add("-a");
                info.ArgumentList.Add(secretId);
                info.ArgumentList.Add("-w");
            },
            ct);

        if (result.ExitCode == 0)
            return string.IsNullOrWhiteSpace(result.Stdout) ? null : result.Stdout.TrimEnd('\r', '\n');

        if (result.Stderr.Contains("could not be found", StringComparison.OrdinalIgnoreCase))
            return null;

        throw new InvalidOperationException($"Failed to read secret '{secretId}' from macOS Keychain: {result.Stderr}");
    }

    public async Task SetSecretAsync(string secretId, string secret, CancellationToken ct = default)
    {
        var result = await RunSecurityAsync(
            info =>
            {
                info.ArgumentList.Add("add-generic-password");
                info.ArgumentList.Add("-U");
                info.ArgumentList.Add("-s");
                info.ArgumentList.Add(ServiceName);
                info.ArgumentList.Add("-a");
                info.ArgumentList.Add(secretId);
                info.ArgumentList.Add("-w");
                info.ArgumentList.Add(secret);
            },
            ct);

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Failed to store secret '{secretId}' in macOS Keychain: {result.Stderr}");
    }

    public async Task DeleteSecretAsync(string secretId, CancellationToken ct = default)
    {
        var result = await RunSecurityAsync(
            info =>
            {
                info.ArgumentList.Add("delete-generic-password");
                info.ArgumentList.Add("-s");
                info.ArgumentList.Add(ServiceName);
                info.ArgumentList.Add("-a");
                info.ArgumentList.Add(secretId);
            },
            ct);

        if (result.ExitCode == 0 || result.Stderr.Contains("could not be found", StringComparison.OrdinalIgnoreCase))
            return;

        throw new InvalidOperationException($"Failed to delete secret '{secretId}' from macOS Keychain: {result.Stderr}");
    }

    private static async Task<ProcessResult> RunSecurityAsync(
        Action<ProcessStartInfo> configure,
        CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "security",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        configure(process.StartInfo);

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        return new ProcessResult(
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false));
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
}
