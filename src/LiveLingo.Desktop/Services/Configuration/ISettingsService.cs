namespace LiveLingo.Desktop.Services.Configuration;

public interface ISettingsService
{
    UserSettings Current { get; }
    bool SettingsFileExists();
    Task LoadAsync(CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
    void Update(Func<UserSettings, UserSettings> mutator);
    event Action<UserSettings>? SettingsChanged;
}
