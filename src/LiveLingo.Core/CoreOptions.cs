namespace LiveLingo.Core;

public class CoreOptions
{
    public string ModelStoragePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiveLingo", "models");

    public string DefaultTargetLanguage { get; set; } = "en";
}
