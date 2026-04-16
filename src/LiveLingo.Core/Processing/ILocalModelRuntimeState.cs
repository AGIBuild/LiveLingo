using LiveLingo.Core.Models;

namespace LiveLingo.Core.Processing;

public interface ILocalModelRuntimeState
{
    ModelLoadState State { get; }
    ModelDescriptor? ActiveModelDescriptor { get; }
    event Action<ModelLoadState>? StateChanged;
}

public sealed class NullLocalModelRuntimeState : ILocalModelRuntimeState
{
    public ModelLoadState State => ModelLoadState.Unloaded;
    public ModelDescriptor? ActiveModelDescriptor => null;
    public event Action<ModelLoadState>? StateChanged
    {
        add { }
        remove { }
    }
}
