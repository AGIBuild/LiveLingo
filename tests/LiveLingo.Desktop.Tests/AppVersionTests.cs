using System.Reflection;
using System.Reflection.Emit;

namespace LiveLingo.Desktop.Tests;

public sealed class AppVersionTests
{
    private static Assembly AssemblyVersioned(string informational)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Versioned"), AssemblyBuilderAccess.Run);
        var constructor = typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!;
        assembly.SetCustomAttribute(new CustomAttributeBuilder(constructor, [informational]));
        return assembly;
    }

    [Theory]
    [InlineData("2.1.0+0123abc", "2.1.0")]
    [InlineData("2.1.0-rc.1+0123abc", "2.1.0-rc.1")]
    [InlineData("2.1.0", "2.1.0")]
    public void TheVersion_IsTheReleaseText_WithoutTheSourceRevision(string stamped, string shown) =>
        Assert.Equal(shown, AppVersion.Read(AssemblyVersioned(stamped)));

    [Fact]
    public void ABuildThatWasNotStamped_ShowsThatItIsNoRelease() =>
        Assert.Equal("0.0.0-dev", AppVersion.Current);
}
