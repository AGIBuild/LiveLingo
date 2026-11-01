using Avalonia.Platform.Storage;
using LiveLingo.Desktop.Web;

namespace LiveLingo.Desktop.Composer;

/// <summary>The system file pickers behind the composer's import and export.</summary>
internal sealed class StorageFileDialogs(IStorageProvider storage, ComposerTexts texts) : IFileDialogs
{
    public async Task<string?> PickRecordingAsync()
    {
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = texts.ImportTitle,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(texts.ImportFileType) { Patterns = ["*.wav"] }]
        });
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    public async Task<string?> PickExportPathAsync(ExportFormat format)
    {
        var (label, extension) = format switch
        {
            ExportFormat.Txt => ("TXT", "txt"),
            ExportFormat.Markdown => ("Markdown", "md"),
            ExportFormat.Srt => ("SRT", "srt"),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = texts.ExportTitle,
            SuggestedFileName = "livelingo." + extension,
            DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType(label) { Patterns = ["*." + extension] }]
        });
        return file?.Path.LocalPath;
    }
}
