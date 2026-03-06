using LiveLingo.Core.Models;

namespace LiveLingo.Core.Tests.Models;

public class InsufficientDiskSpaceExceptionTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var ex = new InsufficientDiskSpaceException(1_000_000, 500_000);
        Assert.Equal(1_000_000, ex.RequiredBytes);
        Assert.Equal(500_000, ex.AvailableBytes);
        Assert.Contains("1000000", ex.Message);
        Assert.Contains("500000", ex.Message);
    }

    [Fact]
    public void IsException()
    {
        var ex = new InsufficientDiskSpaceException(100, 50);
        Assert.IsAssignableFrom<Exception>(ex);
    }
}
