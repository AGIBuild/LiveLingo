using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(LiveLingo.Desktop.Tests.TestAppBuilder))]

namespace LiveLingo.Desktop.Tests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<TestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public class TestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://LiveLingo.Desktop.Tests"))
        {
            Source = new Uri("avares://LiveLingo.Desktop/Styles/AppTheme.axaml")
        });
    }
}
