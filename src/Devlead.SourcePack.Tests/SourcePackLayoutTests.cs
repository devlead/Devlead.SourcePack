using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;
using Xunit;

namespace Devlead.SourcePack.Tests;

/// <summary>
/// Validates that Devlead.SourcePack produces expected NuGet package layouts.
/// </summary>
public sealed class SourcePackLayoutTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string ArtifactsRoot = Path.Combine(RepositoryRoot, "artifacts", "test-output");

    /// <summary>
    /// Packing the sample project produces contentFiles sources and no lib folder.
    /// </summary>
    [Fact]
    public void Pack_sample_produces_content_files_without_lib()
    {
        var output = PackSamplePackage();
        var entries = ReadZipEntries(output);

        Assert.Contains(entries, path => path.Equals("contentFiles/cs/net8.0/Devlead/Sample/SampleService.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, path => path.Equals("contentFiles/cs/net9.0/Devlead/Sample/SampleService.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, path => path.Equals("contentFiles/cs/net10.0/Devlead/Sample/SampleService.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, path => path.Equals("build/net8.0/Devlead.SourcePack.Sample.props", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, path => path.Equals("build/net10.0/Devlead.SourcePack.Sample.targets", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, path => path.Equals("README.md", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, path => path.Contains("/obj/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, path => path.StartsWith("lib/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Packed dependencies flow build and analyzer assets to consumers (no exclude attribute).
    /// </summary>
    [Fact]
    public void Pack_sample_nuspec_dependencies_flow_build_assets()
    {
        var output = PackSamplePackage();
        var nuspecPath = ExtractNuspec(output);
        var document = XDocument.Load(nuspecPath);
        XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;

        var excludedDependencies = document
            .Descendants(ns + "dependency")
            .Where(dependency => dependency.Attribute("exclude") != null)
            .Select(dependency => dependency.Attribute("id")!.Value)
            .ToList();

        Assert.Empty(excludedDependencies);
    }

    /// <summary>
    /// Packed sample nuspec contains per-target-framework dependency groups.
    /// </summary>
    [Fact]
    public void Pack_sample_nuspec_contains_tfm_dependencies()
    {
        var output = PackSamplePackage();
        var nuspecPath = ExtractNuspec(output);
        var document = XDocument.Load(nuspecPath);
        XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;

        var dependencyGroups = document
            .Descendants(ns + "group")
            .Where(group => group.Attribute("targetFramework") != null)
            .Select(group => group.Attribute("targetFramework")!.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("net8.0", dependencyGroups);
        Assert.Contains("net9.0", dependencyGroups);
        Assert.Contains("net10.0", dependencyGroups);
    }

    /// <summary>
    /// Packing the bundle sample re-exports dependency content and promotes direct dependencies.
    /// </summary>
    [Fact]
    public void Pack_bundle_sample_includes_content_and_promotes_direct_dependencies()
    {
        var output = PackBundleSamplePackage();
        var entries = ReadZipEntries(output);

        Assert.Contains(entries, path => path.Equals("contentFiles/cs/net8.0/Devlead/Bundle/BundleMarker.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, path => path.Equals("contentFiles/cs/net8.0/Devlead/Bundled/Devlead/Sample/SampleService.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, path => path.Equals("contentFiles/cs/net10.0/Devlead/Bundled/Devlead/Sample/SampleService.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, path => path.Equals("build/net8.0/Devlead.SourcePack.Sample.props", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, path => path.Equals("build/net10.0/Devlead.SourcePack.Sample.targets", StringComparison.OrdinalIgnoreCase));

        var nuspecPath = ExtractNuspec(output);
        var document = XDocument.Load(nuspecPath);
        XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;

        var dependencyIds = document
            .Descendants(ns + "dependency")
            .Select(dependency => dependency.Attribute("id")!.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("Microsoft.Extensions.Logging.Abstractions", dependencyIds);
        Assert.DoesNotContain("Devlead.SourcePack.Sample", dependencyIds);

        var dependencyGroups = document
            .Descendants(ns + "group")
            .Where(group => group.Attribute("targetFramework") != null)
            .Select(group => group.Attribute("targetFramework")!.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("net8.0", dependencyGroups);
        Assert.Contains("net9.0", dependencyGroups);
        Assert.Contains("net10.0", dependencyGroups);
    }

    private static string PackBundleSamplePackage()
    {
        if (Directory.Exists(ArtifactsRoot))
        {
            Directory.Delete(ArtifactsRoot, recursive: true);
        }

        Directory.CreateDirectory(ArtifactsRoot);
        var feed = Path.Combine(ArtifactsRoot, "bundle-feed");
        var sourcePackOutput = Path.Combine(ArtifactsRoot, "sourcepack");
        var sampleOutput = Path.Combine(ArtifactsRoot, "sample");
        var bundleOutput = Path.Combine(ArtifactsRoot, "bundle-sample");

        Directory.CreateDirectory(feed);
        Directory.CreateDirectory(sourcePackOutput);
        Directory.CreateDirectory(sampleOutput);
        Directory.CreateDirectory(bundleOutput);

        var version = "0.0.0-bundle-test";
        var sourcePackProject = Path.Combine(RepositoryRoot, "src", "Devlead.SourcePack", "Devlead.SourcePack.csproj");
        var tasksProject = Path.Combine(RepositoryRoot, "src", "Devlead.SourcePack.Tasks", "Devlead.SourcePack.Tasks.csproj");
        var sampleProject = Path.Combine(RepositoryRoot, "src", "Devlead.SourcePack.Sample", "Devlead.SourcePack.Sample.csproj");
        var bundleProject = Path.Combine(RepositoryRoot, "src", "Devlead.SourcePack.Bundle.Sample", "Devlead.SourcePack.Bundle.Sample.csproj");
        var nugetPackagePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages");
        var configuredPackagesPath = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        var packageCacheRoots = new List<string> { nugetPackagePath };
        if (!string.IsNullOrWhiteSpace(configuredPackagesPath))
        {
            packageCacheRoots.Add(configuredPackagesPath);
        }

        foreach (var cacheRoot in packageCacheRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var packageId in new[] { "devlead.sourcepack", "devlead.sourcepack.sample", "devlead.sourcepack.bundle.sample" })
            {
                var packagePath = Path.Combine(cacheRoot, packageId, version);
                if (Directory.Exists(packagePath))
                {
                    Directory.Delete(packagePath, recursive: true);
                }
            }
        }

        RunDotNet([
            "build", tasksProject,
            "-c", "Release",
            "/p:TreatWarningsAsErrors=false",
            "--verbosity", "quiet"
        ]);

        RunDotNet([
            "pack", sourcePackProject,
            "-c", "Release",
            "-o", sourcePackOutput,
            $"/p:PackageVersion={version}",
            "/p:TreatWarningsAsErrors=false",
            "--verbosity", "quiet"
        ]);

        foreach (var package in Directory.GetFiles(sourcePackOutput, "Devlead.SourcePack.*.nupkg"))
        {
            File.Copy(package, Path.Combine(feed, Path.GetFileName(package)), overwrite: true);
        }

        var nugetConfig = Path.Combine(ArtifactsRoot, "bundle-nuget.config");
        File.WriteAllText(nugetConfig,
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="{feed}" />
                <add key="nuget" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        RunDotNet([
            "pack", sampleProject,
            "-c", "Release",
            "--configfile", nugetConfig,
            $"/p:DevleadSourcePackVersion={version}",
            "-o", sampleOutput,
            $"/p:PackageVersion={version}",
            "/p:TreatWarningsAsErrors=false",
            "--",
            "/m:1",
            "--verbosity", "quiet"
        ]);

        foreach (var package in Directory.GetFiles(sampleOutput, "*.nupkg"))
        {
            File.Copy(package, Path.Combine(feed, Path.GetFileName(package)), overwrite: true);
        }

        RunDotNet([
            "pack", bundleProject,
            "-c", "Release",
            "--configfile", nugetConfig,
            $"/p:DevleadSourcePackVersion={version}",
            $"/p:DevleadSourcePackSampleVersion={version}",
            "-o", bundleOutput,
            $"/p:PackageVersion={version}",
            "/p:TreatWarningsAsErrors=false",
            "--",
            "/m:1",
            "--verbosity", "quiet"
        ]);

        return Directory.GetFiles(bundleOutput, "Devlead.SourcePack.Bundle.Sample.*.nupkg").Single();
    }

    private static string PackSamplePackage()
    {
        if (Directory.Exists(ArtifactsRoot))
        {
            Directory.Delete(ArtifactsRoot, recursive: true);
        }

        Directory.CreateDirectory(ArtifactsRoot);
        var feed = Path.Combine(ArtifactsRoot, "feed");
        var sourcePackOutput = Path.Combine(ArtifactsRoot, "sourcepack");
        var sampleOutput = Path.Combine(ArtifactsRoot, "sample");

        Directory.CreateDirectory(feed);
        Directory.CreateDirectory(sourcePackOutput);
        Directory.CreateDirectory(sampleOutput);

        var version = "0.0.0-test";
        var sourcePackProject = Path.Combine(RepositoryRoot, "src", "Devlead.SourcePack", "Devlead.SourcePack.csproj");
        var sampleProject = Path.Combine(RepositoryRoot, "src", "Devlead.SourcePack.Sample", "Devlead.SourcePack.Sample.csproj");
        var nugetPackagePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages",
            "devlead.sourcepack",
            version);
        var configuredPackagesPath = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        var packagePaths = new List<string> { nugetPackagePath };
        if (!string.IsNullOrWhiteSpace(configuredPackagesPath))
        {
            packagePaths.Add(Path.Combine(configuredPackagesPath, "devlead.sourcepack", version));
        }

        foreach (var packagePath in packagePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(packagePath))
            {
                Directory.Delete(packagePath, recursive: true);
            }
        }

        RunDotNet([
            "pack", sourcePackProject,
            "-c", "Release",
            "-o", sourcePackOutput,
            $"/p:PackageVersion={version}",
            "/p:TreatWarningsAsErrors=false",
            "--verbosity", "quiet"
        ]);

        var sourcePackPackage = Directory.GetFiles(sourcePackOutput, "Devlead.SourcePack.*.nupkg").Single();
        File.Copy(sourcePackPackage, Path.Combine(feed, Path.GetFileName(sourcePackPackage)), overwrite: true);

        var nugetConfig = Path.Combine(ArtifactsRoot, "nuget.config");
        File.WriteAllText(nugetConfig,
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="{feed}" />
                <add key="nuget" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        RunDotNet([
            "pack", sampleProject,
            "-c", "Release",
            "--configfile", nugetConfig,
            $"/p:DevleadSourcePackVersion={version}",
            "-o", sampleOutput,
            $"/p:PackageVersion={version}",
            "/p:TreatWarningsAsErrors=false",
            "--",
            "/m:1",
            "--verbosity", "quiet"
        ]);

        return Directory.GetFiles(sampleOutput, "Devlead.SourcePack.Sample.*.nupkg").Single();
    }

    private static IReadOnlyList<string> ReadZipEntries(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        return archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToList();
    }

    private static string ExtractNuspec(string packagePath)
    {
        var extractPath = Path.Combine(ArtifactsRoot, "nuspec");
        if (Directory.Exists(extractPath))
        {
            Directory.Delete(extractPath, recursive: true);
        }

        Directory.CreateDirectory(extractPath);
        ZipFile.ExtractToDirectory(packagePath, extractPath);
        return Directory.GetFiles(extractPath, "*.nuspec", SearchOption.AllDirectories).Single();
    }

    private static void RunDotNet(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start dotnet.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"dotnet {string.Join(' ', arguments)} failed with exit code {process.ExitCode}.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "cake.cs")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
