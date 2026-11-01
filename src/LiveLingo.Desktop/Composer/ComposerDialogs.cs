using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace LiveLingo.Desktop.Composer;

/// <summary>The prototype's two dialogs: recent drafts and export.</summary>
internal static class ComposerDialogs
{
    public static Task ShowRecentAsync(Window owner, ComposerTexts texts, RecentDrafts recent, ComposerViewModel vm)
    {
        var body = new StackPanel { Spacing = 0 };
        var dialog = Create(texts.MoreRecent, body);

        if (recent.Items.Count == 0)
        {
            body.Children.Add(new TextBlock { Text = texts.RecentEmpty, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
        }

        foreach (var draft in recent.Items)
        {
            var restore = new Button { Classes = { "btn" }, Content = texts.RecentRestore, IsEnabled = vm.CanReplaceDraft };
            restore.Click += (_, _) =>
            {
                vm.SourceText = draft.Text;
                dialog.Close();
            };
            var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), Margin = new Thickness(0, 0) };
            row.Children.Add(new TextBlock { Text = ComposerTexts.RecentItem(draft), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(restore, 1);
            row.Children.Add(restore);
            body.Children.Add(new Border
            {
                Padding = new Thickness(0, 10),
                BorderThickness = new Thickness(0, 1, 0, 0),
                BorderBrush = (IBrush)owner.FindResource("Line")!,
                Child = row
            });
        }

        return dialog.ShowDialog(owner);
    }

    public static Task ShowExportAsync(Window owner, ComposerTexts texts, ComposerViewModel vm)
    {
        var body = new StackPanel { Spacing = 0 };
        var dialog = Create(texts.MoreExport, body);
        body.Children.Add(new TextBlock { Text = texts.ExportHint, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        foreach (var (format, label, extension) in new[]
                 {
                     (ExportFormat.Txt, "TXT", "txt"),
                     (ExportFormat.Markdown, "Markdown", "md"),
                     (ExportFormat.Srt, "SRT", "srt")
                 })
        {
            var button = new Button { Classes = { "btn" }, Content = label, IsEnabled = format != ExportFormat.Srt || vm.HasTimeline };
            button.Click += async (_, _) =>
            {
                dialog.Close();
                var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = texts.ExportTitle,
                    SuggestedFileName = "livelingo." + extension,
                    DefaultExtension = extension,
                    FileTypeChoices = [new FilePickerFileType(label) { Patterns = ["*." + extension] }]
                });
                if (file is not null)
                    await vm.ExportAsync(file.Path.LocalPath, format);
            };
            buttons.Children.Add(button);
        }

        body.Children.Add(buttons);
        return dialog.ShowDialog(owner);
    }

    /// <summary>A rounded panel with a title and a close button, like the prototype's dialog.</summary>
    internal static Window Create(string title, Control body)
    {
        var close = new Button { Classes = { "icon" }, Content = new Controls.Icon { Data = (Geometry)Application.Current!.FindResource("IcClose")! } };
        var header = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock { Text = title, Classes = { "heading" }, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        var content = new StackPanel();
        content.Children.Add(header);
        content.Children.Add(body);

        var panel = new Border
        {
            Margin = new Thickness(18),
            Padding = new Thickness(22),
            CornerRadius = new CornerRadius(18),
            BorderThickness = new Thickness(1),
            Child = content
        };
        panel.Bind(Border.BackgroundProperty, panel.GetResourceObservable("Panel"));
        panel.Bind(Border.BorderBrushProperty, panel.GetResourceObservable("Line"));
        panel.Bind(Border.BoxShadowProperty, panel.GetResourceObservable("TkShadow"));

        var window = new Window
        {
            Width = 596,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowDecorations = WindowDecorations.None,
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };
        close.Click += (_, _) => window.Close();
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape)
                window.Close();
        };
        return window;
    }
}
