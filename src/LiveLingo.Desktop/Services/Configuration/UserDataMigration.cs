namespace LiveLingo.Desktop.Services.Configuration;

/// <summary>What happened to one entry of the data folder that earlier versions kept where the installer now lives.</summary>
/// <param name="Error">Why it could not be moved (it is in use, or the disk refused); null when it was moved.</param>
internal sealed record UserDataMove(string Name, Exception? Error);

/// <summary>
/// Moves the settings and the downloaded models from the folder earlier Windows versions used
/// (<c>%LocalAppData%\LiveLingo</c>, which Velopack empties when it installs and uninstalls) to the data folder
/// (<see cref="LiveLingo.Core.UserData"/>). A rename on one volume, so the models are not copied. An entry the new
/// folder already has is left alone, and one that cannot be moved is reported, not fatal: the application starts with
/// what it has and the old entry stays where it was.
/// </summary>
internal static class UserDataMigration
{
    private static readonly string[] Entries = ["settings.json", "preferences.json", "models"];

    public static IReadOnlyList<UserDataMove> Run(string legacy, string current)
    {
        var moves = new List<UserDataMove>();
        foreach (var name in Entries)
        {
            var from = Path.Combine(legacy, name);
            var to = Path.Combine(current, name);
            var isDirectory = Directory.Exists(from);
            if ((!isDirectory && !File.Exists(from)) || Directory.Exists(to) || File.Exists(to))
                continue;

            try
            {
                Directory.CreateDirectory(current);
                if (isDirectory)
                    Directory.Move(from, to);
                else
                    File.Move(from, to);
                moves.Add(new UserDataMove(name, null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                moves.Add(new UserDataMove(name, ex));
            }
        }

        return moves;
    }
}
