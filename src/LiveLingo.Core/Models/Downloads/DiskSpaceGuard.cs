namespace LiveLingo.Core.Models.Downloads;

/// <summary>
/// Refuses a download when the target volume cannot satisfy the requested byte budget.
/// </summary>
internal static class DiskSpaceGuard
{
    public static void EnsureAvailable(string path, long requiredBytes)
    {
        if (requiredBytes <= 0)
            return;

        var drive = new DriveInfo(Path.GetPathRoot(path) ?? path);
        if (drive.AvailableFreeSpace < requiredBytes)
            throw new InsufficientDiskSpaceException(requiredBytes, drive.AvailableFreeSpace);
    }
}
