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
        Assert.DoesNotContain(entries, path => path.StartsWith("lib/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
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

    private static string PackSamplePackage()
    {
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

        if (Directory.Exists(nugetPackagePath))
        {
            Directory.Delete(nugetPackagePath, recursive: true);
        }

        RunDotNet([
            "pack", sourcePackProject,
            "-c", "Release",
            "-o", sourcePackOutput,
            $"/p:PackageVersion={version}",
            "/p:TreatWarningsAsErrors=false",
            "--verbosity", "quiet"
        ]);

        var sourcePackPackage = Directory.GetFiles(sourcePackOutput, "*.nupkg").Single();
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
