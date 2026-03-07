using System.Text.Json;

namespace LiveLingo.Desktop.Services.Configuration;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private UserSettings _current = new();

    public UserSettings Current => _current;
    public event Action<UserSettings>? SettingsChanged;

    public JsonSettingsService(string? filePath = null)
    {
        _filePath = filePath ?? GetDefaultPath();
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_filePath))
            {
                _current = new UserSettings();
                return;
            }

            var json = await File.ReadAllTextAsync(_filePath, ct);
            _current = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? new UserSettings();
        }
        catch (JsonException)
        {
            _current = new UserSettings();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_current, JsonOptions);
            var tempPath = $"{_filePath}.tmp";
            await File.WriteAllTextAsync(tempPath, json, ct);
            File.Move(tempPath, _filePath, true);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Update(Func<UserSettings, UserSettings> mutator)
    {
        _current = mutator(_current);
        SettingsChanged?.Invoke(_current);
        _ = SaveAsync(CancellationToken.None);
    }

    public bool SettingsFileExists() => File.Exists(_filePath);

    private static string GetDefaultPath()
    {
        var folder = OperatingSystem.IsMacOS()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "LiveLingo")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LiveLingo");

        return Path.Combine(folder, "settings.json");
    }
}
