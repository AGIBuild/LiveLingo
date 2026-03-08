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
    private SettingsModel _current = SettingsModel.CreateDefault();

    public SettingsModel Current => _current;
    public event Action? SettingsChanged;

    public JsonSettingsService(string? filePath = null)
    {
        _filePath = filePath ?? GetDefaultPath();
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var shouldPersistDefaults = false;
            if (!File.Exists(_filePath))
            {
                _current = SettingsModel.CreateDefault();
                shouldPersistDefaults = true;
            }
            else
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_filePath, ct).ConfigureAwait(false);
                    using var document = JsonDocument.Parse(json);
                    if (!document.RootElement.TryGetProperty("schemaVersion", out var schemaNode) ||
                        !schemaNode.TryGetInt32(out var schemaVersion) ||
                        schemaVersion != SettingsModel.CurrentSchemaVersion)
                    {
                        _current = SettingsModel.CreateDefault();
                        shouldPersistDefaults = true;
                    }
                    else
                    {
                        var loaded = JsonSerializer.Deserialize<SettingsModel>(json, JsonOptions);
                        if (loaded is null)
                        {
                            _current = SettingsModel.CreateDefault();
                            shouldPersistDefaults = true;
                        }
                        else
                        {
                            _current = loaded;
                        }
                    }
                }
                catch (JsonException)
                {
                    _current = SettingsModel.CreateDefault();
                    shouldPersistDefaults = true;
                }
            }

            if (shouldPersistDefaults)
                SaveCurrentUnsafe();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            SaveCurrentUnsafe();
        }
        finally
        {
            _lock.Release();
        }
    }

    public SettingsModel CloneCurrent()
    {
        _lock.Wait();
        try
        {
            return _current.DeepClone();
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Replace(SettingsModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        _lock.Wait();
        try
        {
            var previous = _current;
            var replacement = model.DeepClone();
            _current = replacement;

            try
            {
                SaveCurrentUnsafe();
            }
            catch
            {
                _current = previous;
                throw;
            }

            SettingsChanged?.Invoke();
        }
        finally
        {
            _lock.Release();
        }
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

    private void SaveCurrentUnsafe()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(_current, JsonOptions);
        var tempPath = $"{_filePath}.tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, true);
    }
}
