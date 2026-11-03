using System.Reflection;

namespace LiveLingo.Desktop;

/// <summary>
/// The version this build shows. The release build stamps it from the release tag (<c>--Version</c>), so it is the same
/// text the installer and the update feed carry; a build made any other way says so.
/// </summary>
public static class AppVersion
{
    public static string Current { get; } = Read(typeof(AppVersion).Assembly);

    /// <summary>The informational version without the build metadata (the source revision) that follows a "+".</summary>
    internal static string Read(Assembly assembly)
    {
        var text = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var plus = text.IndexOf('+');
        return plus < 0 ? text : text[..plus];
    }
}
