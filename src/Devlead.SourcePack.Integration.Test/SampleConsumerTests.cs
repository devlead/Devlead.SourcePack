using Devlead.Sample;
using Xunit;

namespace Devlead.SourcePack.Integration.Test;

/// <summary>
/// Integration tests that consume a packed Devlead.SourcePack.Sample NuGet package.
/// </summary>
public sealed class SampleConsumerTests
{
    /// <summary>
    /// Verifies that source from the packed sample compiles and runs in the consumer.
    /// </summary>
    [Fact]
    public void Sample_service_returns_expected_greeting()
    {
        var service = new SampleService();
        Assert.Equal("source-pack-ok", service.GetGreeting());
    }

    /// <summary>
    /// Verifies that sample targets define the expected constant.
    /// </summary>
    [Fact]
    public void Sample_targets_define_expected_constant()
    {
#if SourcePackSample
        Assert.True(true);
#else
        Assert.Fail("SourcePackSample constant was not defined by packed targets.");
#endif
    }
}
