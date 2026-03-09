namespace LiveLingo.Core.Models;

public sealed class ModelNotReadyException : InvalidOperationException
{
    public ModelType ModelType { get; }
    public string ModelId { get; }
    public string RecoveryHint { get; }

    public ModelNotReadyException(
        ModelType modelType,
        string modelId,
        string message,
        string recoveryHint)
        : base(message)
    {
        ModelType = modelType;
        ModelId = modelId;
        RecoveryHint = recoveryHint;
    }
}
