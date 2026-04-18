using LiveLingo.Core.Models;
using LiveLingo.Core.Models.Downloads;

namespace LiveLingo.Core.Tests.Models.Downloads;

public sealed class DiskSpaceGuardTests
{
    [Fact]
    public void EnsureAvailable_AllowsZeroOrNegativeRequirement()
    {
        var dir = Directory.GetCurrentDirectory();

        DiskSpaceGuard.EnsureAvailable(dir, 0);
        DiskSpaceGuard.EnsureAvailable(dir, -100);
    }

    [Fact]
    public void EnsureAvailable_PassesWhenWithinQuota()
    {
        var dir = Directory.GetCurrentDirectory();

        DiskSpaceGuard.EnsureAvailable(dir, 1024);
    }

    [Fact]
    public void EnsureAvailable_ThrowsInsufficientDiskSpaceWhenAskingForExabyte()
    {
        var dir = Directory.GetCurrentDirectory();

        var ex = Assert.Throws<InsufficientDiskSpaceException>(() =>
            DiskSpaceGuard.EnsureAvailable(dir, long.MaxValue));

        Assert.Equal(long.MaxValue, ex.RequiredBytes);
        Assert.True(ex.AvailableBytes >= 0);
    }
}
