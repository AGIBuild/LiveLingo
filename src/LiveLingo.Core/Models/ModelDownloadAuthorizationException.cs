namespace LiveLingo.Core.Models;

/// <summary>
/// Hugging Face (or a configured mirror) rejected the download with 401/403.
/// User may need a read token under Settings → Advanced.
/// </summary>
public sealed class ModelDownloadAuthorizationException : Exception
{
    public ModelDownloadAuthorizationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
