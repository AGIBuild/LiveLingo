namespace LiveLingo.Core;

/// <summary>
/// Where the user's data (settings, models, logs) lives: one place for the whole application. It is never inside a
/// folder an installer owns. Velopack empties <c>%LocalAppData%\LiveLingo</c> when an installer runs over it and when
/// the application is uninstalled, so on Windows the data sits under the publisher's folder next to it and survives
/// both, like the data under Application Support on macOS survives deleting the app.
/// </summary>
public static class UserData
{
    public static string Directory { get; } = Resolve(
        OperatingSystem.IsMacOS(),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>The folder earlier versions kept the data in on Windows; the installer owns it now.</summary>
    public static string LegacyWindowsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiveLingo");

    internal static string Resolve(bool macOS, string home, string localAppData) => macOS
        ? Path.Combine(home, "Library", "Application Support", "LiveLingo")
        : Path.Combine(localAppData, "AGIBuild", "LiveLingo");
}
