using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace LiveLingo.Desktop.Platform.Windows;

[SupportedOSPlatform("windows")]
internal sealed class WindowsCredentialSecretStore : ISecretStore
{
    private const string TargetPrefix = "LiveLingo/";

    public Task<string?> GetSecretAsync(string secretId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!NativeMethods.CredReadW(BuildTargetName(secretId), NativeMethods.CRED_TYPE_GENERIC, 0, out var credentialPtr))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == NativeMethods.ERROR_NOT_FOUND)
                return Task.FromResult<string?>(null);

            throw new Win32Exception(error, $"Failed to read credential '{secretId}'.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeMethods.CREDENTIAL>(credentialPtr);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                return Task.FromResult<string?>(null);

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            var secret = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
            return Task.FromResult<string?>(secret);
        }
        finally
        {
            NativeMethods.CredFree(credentialPtr);
        }
    }

    public Task SetSecretAsync(string secretId, string secret, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var bytes = Encoding.Unicode.GetBytes(secret);
        var secretPtr = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, secretPtr, bytes.Length);

            var credential = new NativeMethods.CREDENTIAL
            {
                Type = NativeMethods.CRED_TYPE_GENERIC,
                TargetName = BuildTargetName(secretId),
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = secretPtr,
                Persist = NativeMethods.CRED_PERSIST_LOCAL_MACHINE,
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                Comment = null,
                TargetAlias = null,
                UserName = null
            };

            if (!NativeMethods.CredWriteW(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to store credential '{secretId}'.");

            return Task.CompletedTask;
        }
        finally
        {
            Marshal.FreeCoTaskMem(secretPtr);
        }
    }

    public Task DeleteSecretAsync(string secretId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!NativeMethods.CredDeleteW(BuildTargetName(secretId), NativeMethods.CRED_TYPE_GENERIC, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == NativeMethods.ERROR_NOT_FOUND)
                return Task.CompletedTask;

            throw new Win32Exception(error, $"Failed to delete credential '{secretId}'.");
        }

        return Task.CompletedTask;
    }

    private static string BuildTargetName(string secretId) => $"{TargetPrefix}{secretId}";
}
