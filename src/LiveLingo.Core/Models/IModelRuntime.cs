namespace LiveLingo.Core.Models;

public interface IModelRuntime
{
    ModelRuntimeKind RuntimeKind { get; }

    Task<ModelRuntimeSession> AcquireSessionAsync(
        ModelProfile profile,
        ModelTaskType taskType,
        CancellationToken ct = default);
}
