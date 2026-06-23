#!/usr/bin/env dotnet
#:sdk Cake.Sdk@6.2.0
#:property IncludeAdditionalFiles=./build/*.cs

/*****************************
 * Setup
 *****************************/
Setup(
    static context => {
        InstallTool("dotnet:https://api.nuget.org/v3/index.json?package=GitVersion.Tool&version=6.7.0");
        InstallTool("dotnet:https://api.nuget.org/v3/index.json?package=DPI&version=2026.5.18.419");

        var assertedVersions = context.GitVersion(new GitVersionSettings
        {
            OutputType = GitVersionOutput.Json
        });

        var branchName = assertedVersions.BranchName;
        var isMainBranch = StringComparer.OrdinalIgnoreCase.Equals("main", branchName);

        var buildDate = DateTime.UtcNow;
        var runNumber = GitHubActions.IsRunningOnGitHubActions
                            ? GitHubActions.Environment.Workflow.RunNumber
                            : 0;

        var suffix = runNumber == 0
                       ? $"-{(short)((buildDate - buildDate.Date).TotalSeconds / 3)}"
                       : string.Empty;

        var version = FormattableString
                          .Invariant($"{buildDate:yyyy.M.d}.{runNumber}{suffix}");

        context.Information("Building version {0} (Branch: {1}, IsMain: {2})",
            version,
            branchName,
            isMainBranch);

        var artifactsPath = context
                            .MakeAbsolute(context.Directory("./artifacts"));

        var projectRoot = context
                            .MakeAbsolute(context.Directory("./src"));

        var projectPath = projectRoot.CombineWithFilePath("Devlead.SourcePack/Devlead.SourcePack.csproj");
        var sampleProjectPath = projectRoot.CombineWithFilePath("Devlead.SourcePack.Sample/Devlead.SourcePack.Sample.csproj");

        return new BuildData(
            version,
            isMainBranch,
            !context.IsRunningOnWindows(),
            BuildSystem.IsLocalBuild,
            projectRoot,
            projectPath,
            sampleProjectPath,
            new DotNetMSBuildSettings()
                .SetConfiguration("Release")
                .SetVersion(version)
                .WithProperty("Copyright", $"Mattias Karlsson © {DateTime.UtcNow.Year}")
                .WithProperty("Authors", "devlead")
                .WithProperty("Company", "devlead")
                .WithProperty("PackageLicenseExpression", "MIT")
                .WithProperty("PackageTags", "sourcepack;msbuild;nuget;source")
                .WithProperty("PackageDescription", "Opinionated MSBuild package for authoring source NuGet packages using standard SDK pack.")
                .WithProperty("RepositoryUrl", "https://github.com/devlead/Devlead.SourcePack.git")
                .WithProperty("ContinuousIntegrationBuild", GitHubActions.IsRunningOnGitHubActions ? "true" : "false")
                .WithProperty("EmbedUntrackedSources", "true"),
            artifactsPath,
            artifactsPath.Combine(version)
            );
    }
);

/*****************************
 * Tasks
 *****************************/
Task("Clean")
    .Does<BuildData>(
        static (context, data) => context.CleanDirectories(data.DirectoryPathsToClean)
    )
.Then("Restore")
    .Does<BuildData>(
        static (context, data) => {
            foreach (var project in context.GetFiles(data.ProjectRoot.FullPath + "/**/*.csproj")
                .Where(path => !path.FullPath.Contains("Devlead.SourcePack.Sample", StringComparison.OrdinalIgnoreCase)
                            && !path.FullPath.Contains("Devlead.SourcePack.Integration.Test", StringComparison.OrdinalIgnoreCase)))
            {
                context.DotNetRestore(
                    project.FullPath,
                    new DotNetRestoreSettings
                    {
                        MSBuildSettings = data.MSBuildSettings,
                        Verbosity = DotNetVerbosity.Minimal
                    }
                );
            }
        }
    )
.Then("DPI")
    .Does<BuildData>(
        static (context, data) => Command(
                ["dpi", "dpi.exe"],
                new ProcessArgumentBuilder()
                    .Append("nuget")
                    .Append("--silent")
                    .AppendSwitchQuoted("--output", "table")
                    .Append(
                        (
                            !string.IsNullOrWhiteSpace(context.EnvironmentVariable("NuGetReportSettings_SharedKey"))
                            &&
                            !string.IsNullOrWhiteSpace(context.EnvironmentVariable("NuGetReportSettings_WorkspaceId"))
                        )
                            ? "report"
                            : "analyze"
                        )
                    .AppendSwitchQuoted("--buildversion", data.Version)
            )
    )
.Then("Build")
    .DoesForEach<BuildData, FilePath>(
        static (data, context) => context.GetFiles(data.ProjectRoot.FullPath + "/**/*.csproj")
                                    .Where(path => !path.FullPath.Contains("Devlead.SourcePack.Sample", StringComparison.OrdinalIgnoreCase)
                                                && !path.FullPath.Contains("Devlead.SourcePack.Integration.Test", StringComparison.OrdinalIgnoreCase))
                                    .OrderBy(path => path.FullPath.EndsWith("Devlead.SourcePack.csproj", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                                    .ThenBy(path => path.FullPath, StringComparer.OrdinalIgnoreCase),
        static (data, item, context) => context.DotNetBuild(
            item.FullPath,
            new DotNetBuildSettings
            {
                NoRestore = true,
                MSBuildSettings = data.MSBuildSettings
            }
        )
    )
.Then("Test")
    .Does<BuildData>(
        static (context, data) => context.DotNetTest(
            data.ProjectRoot.CombineWithFilePath("Devlead.SourcePack.Tests/Devlead.SourcePack.Tests.csproj").FullPath,
            new DotNetTestSettings
            {
                NoBuild = true,
                NoRestore = true,
                MSBuildSettings = data.MSBuildSettings
            }
        )
    )
.Then("Pack")
    .Does<BuildData>(
        static (context, data) => context.DotNetPack(
            data.ProjectPath.FullPath,
            new DotNetPackSettings
            {
                NoBuild = true,
                NoRestore = true,
                OutputDirectory = data.NuGetOutputPath,
                MSBuildSettings = data.MSBuildSettings
            }
        )
    )
.Then("Pack-Sample")
    .Does<BuildData>(
        static (context, data) => {
            context.DotNetRestore(
                data.SampleProjectPath.FullPath,
                new DotNetRestoreSettings
                {
                    MSBuildSettings = data.MSBuildSettings
                        .WithProperty("RestoreAdditionalProjectSources", data.NuGetOutputPath.FullPath)
                        .WithProperty("DevleadSourcePackVersion", data.Version)
                }
            );

            context.DotNetPack(
                data.SampleProjectPath.FullPath,
                new DotNetPackSettings
                {
                    NoRestore = true,
                    OutputDirectory = data.NuGetOutputPath,
                    ArgumentCustomization = args => args.Append("/m:1"),
                    MSBuildSettings = new DotNetMSBuildSettings()
                        .SetConfiguration("Release")
                        .WithProperty("DevleadSourcePackVersion", data.Version)
                        .WithProperty("PackageVersion", data.Version)
                        .WithProperty("TreatWarningsAsErrors", "false")
                }
            );
        }
    )
.Then("Upload-Artifacts")
    .WithCriteria(BuildSystem.IsRunningOnGitHubActions, nameof(BuildSystem.IsRunningOnGitHubActions))
    .Does<BuildData>(
        static (context, data) => GitHubActions
            .Commands
            .UploadArtifact(data.ArtifactsPath, $"Artifact_{GitHubActions.Environment.Runner.ImageOS ?? GitHubActions.Environment.Runner.OS}_{context.Environment.Runtime.BuiltFramework.Identifier}_{context.Environment.Runtime.BuiltFramework.Version}")
    )
.Then("Integration-Test")
    .WithCriteria<BuildData>((context, data) => data.ShouldRunIntegrationTests())
    .DoesForEach<BuildData, string>(
        static (data, context) => ["net9.0", "net10.0"],
        static (data, targetFramework, context) => {
            context.Information("Running integration tests for {0}", targetFramework);
            DirectoryPath sourceProjectPath = data.ProjectRoot.Combine("Devlead.SourcePack.Integration.Test");
            DirectoryPath targetProjectPath = data.IntegrationTestPath.Combine($"Devlead.SourcePack.Integration.Test.{targetFramework}");
            FilePath nuGetConfigPath = targetProjectPath.CombineWithFilePath("nuget.config");
            FilePath centralPackageManagementPath = data.ProjectRoot.CombineWithFilePath("Directory.Packages.props");

            context.CopyDirectory(sourceProjectPath, targetProjectPath);
            context.CopyFile(centralPackageManagementPath, data.IntegrationTestPath.CombineWithFilePath("Directory.Packages.props"));
            context.CleanDirectories(
                [
                    targetProjectPath.Combine("bin").FullPath,
                    targetProjectPath.Combine("obj").FullPath
                ]
            );

            using (var stream = context.FileSystem.GetFile(nuGetConfigPath).OpenWrite())
            {
                ReadOnlySpan<byte> content = System.Text.Encoding.UTF8.GetBytes(
                    $"""
                    <configuration>
                        <packageSources>
                            <clear />
                            <add key="artifacts" value="{data.NuGetOutputPath.FullPath}" />
                            <add key="nuget" value="https://api.nuget.org/v3/index.json" />
                        </packageSources>
                        <packageSourceMapping>
                            <packageSource key="artifacts">
                                <package pattern="Devlead.*" />
                            </packageSource>
                            <packageSource key="nuget">
                                <package pattern="*" />
                            </packageSource>
                        </packageSourceMapping>
                    </configuration>
                    """
                );

                stream.Write(content);
            }

            context.DotNetTest(
                targetProjectPath.FullPath,
                new DotNetTestSettings
                {
                    Configuration = "IntegrationTest",
                    MSBuildSettings = new DotNetMSBuildSettings()
                        .SetConfiguration("IntegrationTest")
                        .WithProperty("SamplePackageVersion", data.Version)
                        .WithProperty("TargetFramework", targetFramework)
                        .WithProperty("RestoreAdditionalProjectSources", data.NuGetOutputPath.FullPath)
                }
            );
        }
    )
.Default()
.Then("Push-GitHub-Packages")
    .WithCriteria<BuildData>((context, data) => data.ShouldPushGitHubPackages())
    .DoesForEach<BuildData, FilePath>(
        static (data, context)
            => context.GetFiles(data.NuGetOutputPath.FullPath + "/*.nupkg")
                      .Where(path => data.IsPublishablePackage(path)),
        static (data, item, context)
            => context.DotNetNuGetPush(
                item.FullPath,
                new DotNetNuGetPushSettings
                {
                    Source = data.GitHubNuGetSource,
                    ApiKey = data.GitHubNuGetApiKey
                }
            )
    )
.Then("Push-NuGet-Packages")
    .WithCriteria<BuildData>((context, data) => data.ShouldPushNuGetPackages())
    .DoesForEach<BuildData, FilePath>(
        static (data, context)
            => context.GetFiles(data.NuGetOutputPath.FullPath + "/*.nupkg")
                      .Where(path => data.IsPublishablePackage(path)),
        static (data, item, context)
            => context.DotNetNuGetPush(
                item.FullPath,
                new DotNetNuGetPushSettings
                {
                    Source = data.NuGetSource,
                    ApiKey = data.NuGetApiKey
                }
            )
    )
.Then("Create-GitHub-Release")
    .WithCriteria<BuildData>((context, data) => data.ShouldPushNuGetPackages())
    .Does<BuildData>(
        static (context, data) => context
            .Command(
                new CommandSettings
                {
                    ToolName = "GitHub CLI",
                    ToolExecutableNames = ["gh.exe", "gh"],
                    EnvironmentVariables = { { "GH_TOKEN", data.GitHubNuGetApiKey } }
                },
                new ProcessArgumentBuilder()
                    .Append("release")
                    .Append("create")
                    .Append(data.Version)
                    .AppendSwitchQuoted("--title", data.Version)
                    .Append("--generate-notes")
                    .Append(string.Join(
                        ' ',
                        context
                            .GetFiles(data.NuGetOutputPath.FullPath + "/*.nupkg")
                            .Where(path => data.IsPublishablePackage(path))
                            .Select(path => path.FullPath.Quote())
                        ))
            )
    )
.Then("GitHub-Actions")
.Run();
