namespace LiveLingo.Desktop.Services.Configuration;

public interface ISettingsService
{
    SettingsModel Current { get; }
    SettingsModel CloneCurrent();
    void Replace(SettingsModel model);
    bool SettingsFileExists();
    Task LoadAsync(CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
    event Action? SettingsChanged;
}
