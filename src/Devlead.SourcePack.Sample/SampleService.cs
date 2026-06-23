namespace Devlead.Sample;

/// <summary>
/// Sample service shipped as source in the Devlead.SourcePack.Sample package.
/// </summary>
public sealed class SampleService
{
    /// <summary>
    /// Gets a greeting message for validation tests.
    /// </summary>
    /// <returns>A fixed greeting string.</returns>
    public string GetGreeting() => "source-pack-ok";
}
