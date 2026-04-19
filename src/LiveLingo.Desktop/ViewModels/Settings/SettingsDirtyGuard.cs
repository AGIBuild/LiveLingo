namespace LiveLingo.Desktop.ViewModels.Settings;

internal sealed class SettingsDirtyGuard : ISettingsDirtyGuard
{
    public bool IsLoading { get; private set; }

    public void RunLoading(Action action)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));

        IsLoading = true;
        try
        {
            action();
        }
        finally
        {
            IsLoading = false;
        }
    }
}
