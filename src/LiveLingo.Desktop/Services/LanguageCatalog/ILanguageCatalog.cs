using LiveLingo.Core.Engines;

namespace LiveLingo.Desktop.Services.LanguageCatalog;

public interface ILanguageCatalog
{
    IReadOnlyList<LanguageInfo> All { get; }
}
