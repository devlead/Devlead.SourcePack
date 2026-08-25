/*****************************
 * Records
 *****************************/
public record BuildData(
    string Version,
    bool IsMainBranch,
    bool ShouldNotPublish,
    bool IsLocalBuild,
    DirectoryPath ProjectRoot,
    FilePath ProjectPath,
    FilePath SampleProjectPath,
    FilePath SampleAdvancedProjectPath,
    DotNetMSBuildSettings MSBuildSettings,
    DirectoryPath ArtifactsPath,
    DirectoryPath OutputPath
    )
{
    private const string IntegrationTest = "integrationtest",
                         Output = "output";

    /// <summary>
    /// Path where NuGet packages are written during pack.
    /// </summary>
    public DirectoryPath NuGetOutputPath { get; } = OutputPath.Combine(Output).Combine("nuget");

    /// <summary>
    /// Path used for integration test working copies.
    /// </summary>
    public DirectoryPath IntegrationTestPath { get; } = OutputPath.Combine(IntegrationTest);

    public string? GitHubNuGetSource { get; } = System.Environment.GetEnvironmentVariable("GH_PACKAGES_NUGET_SOURCE");
    public string? GitHubNuGetApiKey { get; } = System.Environment.GetEnvironmentVariable("GITHUB_TOKEN");

    /// <summary>
    /// Whether GitHub Packages push should run.
    /// </summary>
    public bool ShouldPushGitHubPackages() => !ShouldNotPublish
                                            && !string.IsNullOrWhiteSpace(GitHubNuGetSource)
                                            && !string.IsNullOrWhiteSpace(GitHubNuGetApiKey);

    public string? NuGetSource { get; } = System.Environment.GetEnvironmentVariable("NUGET_SOURCE");
    public string? NuGetApiKey { get; } = System.Environment.GetEnvironmentVariable("NUGET_APIKEY");

    /// <summary>
    /// Whether nuget.org push should run.
    /// </summary>
    public bool ShouldPushNuGetPackages() => IsMainBranch
                                             && !ShouldNotPublish
                                             && !string.IsNullOrWhiteSpace(NuGetSource)
                                             && !string.IsNullOrWhiteSpace(NuGetApiKey);

    public ICollection<DirectoryPath> DirectoryPathsToClean { get; } =
    [
        ArtifactsPath,
        OutputPath,
        OutputPath.Combine(IntegrationTest)
    ];

    /// <summary>
    /// Integration tests run on every build.
    /// </summary>
    public bool ShouldRunIntegrationTests() => true;

    /// <summary>
    /// Whether a packed nupkg should be published (excludes local sample packages).
    /// </summary>
    /// <param name="packagePath">Path to the nupkg file.</param>
    /// <returns><c>true</c> when the package should be pushed or attached to a release.</returns>
    public bool IsPublishablePackage(FilePath packagePath)
    {
        var fileName = packagePath.GetFilename().FullPath;
        return !fileName.StartsWith("Devlead.SourcePack.Sample.", StringComparison.OrdinalIgnoreCase)
            && !fileName.StartsWith("Devlead.SourcePack.Bundle.Sample.", StringComparison.OrdinalIgnoreCase);
    }
}

internal record ExtensionHelper(Func<string, CakeTaskBuilder> TaskCreate, Func<CakeReport> Run);
