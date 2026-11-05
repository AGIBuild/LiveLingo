using System.Text.Json;
using LiveLingo.Core;

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
                    var schemaVersion = document.RootElement.TryGetProperty("schemaVersion", out var schemaNode) &&
                                        schemaNode.ValueKind == JsonValueKind.Number &&
                                        schemaNode.TryGetInt32(out var found)
                        ? found
                        : (int?)null;
                    if (schemaVersion != SettingsModel.CurrentSchemaVersion)
                    {
                        _current = schemaVersion == SettingsMigration.LegacySchemaVersion
                            ? SettingsMigration.FromLegacy(document.RootElement)
                            : SettingsModel.CreateDefault();
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

            // The single cloud endpoint of earlier versions becomes the first of the endpoint list.
            if (_current.Cloud.MigrateLegacy())
                shouldPersistDefaults = true;

            if (shouldPersistDefaults)
                SaveCurrentUnsafe();

            // Every settings file written before 2.0.1 holds the placeholder address that was the default then, so the
            // installed app could never find an update. An address the user typed, or an empty one (updates off), is kept.
            if (string.Equals(_current.Update.UpdateUrl, UpdateSettings.PlaceholderUpdateUrl, StringComparison.OrdinalIgnoreCase))
            {
                _current.Update.UpdateUrl = UpdateSettings.DefaultUpdateUrl;
                SaveCurrentUnsafe();
            }
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

    private static string GetDefaultPath() => Path.Combine(UserData.Directory, "settings.json");

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
