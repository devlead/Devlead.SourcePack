using Devlead.Advanced;
using Devlead.Console.Extensions;
using Xunit;

namespace Devlead.SourcePack.Integration.Test;

/// <summary>
/// Integration tests that consume a packed Devlead.SourcePack.Sample.Advanced NuGet package.
/// </summary>
public sealed class AdvancedConsumerTests
{
    /// <summary>
    /// Verifies the advanced sample marker is available from the packed package.
    /// </summary>
    [Fact]
    public void Advanced_marker_is_available()
    {
        Assert.Equal("Devlead.SourcePack.Sample.Advanced", AdvancedMarker.Name);
    }

    /// <summary>
    /// Verifies bundled Devlead.Console sources compile without a direct Console PackageReference.
    /// </summary>
    [Fact]
    public void Bundled_console_types_are_available()
    {
        Assert.Equal(nameof(AppServiceConfig), typeof(AppServiceConfig).Name);
    }
}
