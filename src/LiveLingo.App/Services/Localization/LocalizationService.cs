using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LiveLingo.App.Services.Localization;

public class LocalizationService : ILocalizationService
{
    private const string FallbackCulture = "en-US";
    private const string ResourcePrefix = "LiveLingo.App.Resources.i18n.";

    private readonly ILogger<LocalizationService>? _logger;
    private readonly Dictionary<string, Dictionary<string, string>> _resources = new(StringComparer.OrdinalIgnoreCase);

    public CultureInfo CurrentCulture { get; private set; } = CultureInfo.GetCultureInfo(FallbackCulture);

    public LocalizationService(ILogger<LocalizationService>? logger = null)
    {
        _logger = logger;
        LoadEmbeddedResources();
    }

    public LocalizationService(
        Dictionary<string, Dictionary<string, string>> resources,
        ILogger<LocalizationService>? logger = null)
    {
        _logger = logger;
        foreach (var (culture, dict) in resources)
            _resources[culture] = dict;
    }

    public string T(string key)
    {
        if (_resources.TryGetValue(CurrentCulture.Name, out var active) && active.TryGetValue(key, out var value))
            return value;

        if (!string.Equals(CurrentCulture.Name, FallbackCulture, StringComparison.OrdinalIgnoreCase) &&
            _resources.TryGetValue(FallbackCulture, out var fallback) && fallback.TryGetValue(key, out var fallbackValue))
            return fallbackValue;

        _logger?.LogWarning("Missing localization key: {Key} for culture {Culture}", key, CurrentCulture.Name);
        return key;
    }

    public string T(string key, params object[] args)
    {
        var template = T(key);
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    public void SetCulture(string cultureName)
    {
        CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
    }

    private void LoadEmbeddedResources()
    {
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase) ||
                !resourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;

            var cultureName = resourceName[ResourcePrefix.Length..^".json".Length];

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null) continue;

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict is not null)
                _resources[cultureName] = dict;
        }
    }
}
