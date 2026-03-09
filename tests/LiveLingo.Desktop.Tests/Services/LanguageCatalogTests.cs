using LiveLingo.Desktop.Services.LanguageCatalog;

namespace LiveLingo.Desktop.Tests.Services;

public class LanguageCatalogTests
{
    [Fact]
    public void DefaultCatalog_HasExpectedOrderAndCodes()
    {
        var catalog = new LanguageCatalog();

        var codes = catalog.All.Select(l => l.Code).ToArray();
        Assert.Equal(10, codes.Length);
        Assert.Equal(["zh", "en", "ja", "ko", "fr", "de", "es", "ru", "ar", "pt"], codes);
    }
}
