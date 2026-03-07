using System.Globalization;

namespace LiveLingo.App.Services.Localization;

public interface ILocalizationService
{
    CultureInfo CurrentCulture { get; }
    string T(string key);
    string T(string key, params object[] args);
    void SetCulture(string cultureName);
}
