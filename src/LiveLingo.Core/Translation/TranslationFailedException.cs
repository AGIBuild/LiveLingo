namespace LiveLingo.Core.Translation;

public sealed class TranslationFailedException : InvalidOperationException
{
    public TranslationFailedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
