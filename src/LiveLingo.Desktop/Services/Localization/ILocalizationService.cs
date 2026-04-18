using System.Globalization;

namespace LiveLingo.Desktop.Services.Localization;

public interface ILocalizationService
{
    CultureInfo CurrentCulture { get; }
    string T(string key);
    string T(string key, params object[] args);

    /// <summary>
    /// Non-throwing, fallback-free lookup. Returns <c>true</c> only when the
    /// requested key resolved to a real translation in the current or fallback
    /// culture. Callers use this to supply their own display fallback instead of
    /// surfacing the raw dotted key that <see cref="T(string)"/> would return.
    /// </summary>
    bool TryT(string key, out string value);

    void SetCulture(string cultureName);
}
