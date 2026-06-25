using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Devlead.SourcePack
{

/// <summary>
/// Resolves <see cref="SourcePackBundle"/> items into package files copied from dependency nupkgs.
/// </summary>
public sealed class ResolveSourcePackBundles : Task
{
    /// <summary>
    /// Gets or sets the bundle definitions declared in the project.
    /// </summary>
    [Required]
    public ITaskItem[] Bundles { get; set; }

    /// <summary>
    /// Gets or sets the active target framework moniker.
    /// </summary>
    [Required]
    public string TargetFramework { get; set; }

    /// <summary>
    /// Gets or sets the resolved bundle metadata items.
    /// </summary>
    [Output]
    public ITaskItem[] ResolvedBundles { get; set; }

    /// <summary>
    /// Gets or sets bundled source files with package paths.
    /// </summary>
    [Output]
    public ITaskItem[] BundledSources { get; set; }

    /// <summary>
    /// Gets or sets bundled build assets with package paths.
    /// </summary>
    [Output]
    public ITaskItem[] BundledBuildAssets { get; set; }

    /// <summary>
    /// Gets or sets the NuGet global packages folder.
    /// </summary>
    public string NuGetPackageRoot { get; set; }

    /// <summary>
    /// Gets or sets the semicolon-separated NuGet package folders from restore.
    /// </summary>
    public string NuGetPackageFolders { get; set; }

    /// <summary>
    /// Gets or sets the path to <c>project.assets.json</c> used to resolve package paths with central package management.
    /// </summary>
    public string ProjectAssetsFile { get; set; }

    /// <summary>
    /// Gets or sets package references used to resolve bundle versions.
    /// </summary>
    public ITaskItem[] PackageReferences { get; set; }

    /// <inheritdoc />
    public override bool Execute()
    {
        var resolved = new List<ITaskItem>();
        var sources = new List<ITaskItem>();
        var buildAssets = new List<ITaskItem>();
        var bundles = Bundles ?? new ITaskItem[0];

        foreach (var bundle in bundles)
        {
            var identity = bundle.ItemSpec;
            var pathProperty = bundle.GetMetadata("PackagePathProperty");
            if (string.IsNullOrWhiteSpace(pathProperty))
            {
                pathProperty = "Pkg" + identity.Replace(".", "_");
            }

            var pkgRoot = ResolvePackageRoot(identity, pathProperty);

            var resolvedItem = new TaskItem(identity);
            resolvedItem.SetMetadata("PkgRoot", pkgRoot);
            resolvedItem.SetMetadata("PackagePathPrefix", bundle.GetMetadata("PackagePathPrefix"));
            resolvedItem.SetMetadata(
                "IncludeBuildAssets",
                string.IsNullOrWhiteSpace(bundle.GetMetadata("IncludeBuildAssets"))
                    ? "true"
                    : bundle.GetMetadata("IncludeBuildAssets"));
            resolved.Add(resolvedItem);

            if (string.IsNullOrWhiteSpace(pkgRoot) || !Directory.Exists(pkgRoot))
            {
                continue;
            }

            var contentRoot = Path.Combine(pkgRoot, "contentFiles", "cs", TargetFramework);
            if (Directory.Exists(contentRoot))
            {
                var prefix = bundle.GetMetadata("PackagePathPrefix");
                var prefixSep = string.IsNullOrWhiteSpace(prefix)
                    ? string.Empty
                    : prefix.Replace('\\', '/').TrimEnd('/') + "/";
                var contentRootFull = Path.GetFullPath(contentRoot);
                var contentRootUri = new Uri(
                    contentRootFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                        ? contentRootFull
                        : contentRootFull + Path.DirectorySeparatorChar);

                foreach (var file in Directory.EnumerateFiles(contentRoot, "*.cs", SearchOption.AllDirectories))
                {
                    var fileFull = Path.GetFullPath(file);
                    var relative = Uri
                        .UnescapeDataString(contentRootUri.MakeRelativeUri(new Uri(fileFull)).ToString())
                        .Replace('\\', '/');
                    var sourceItem = new TaskItem(fileFull);
                    sourceItem.SetMetadata(
                        "PackagePath",
                        "contentFiles/cs/" + TargetFramework + "/" + prefixSep + relative);
                    sources.Add(sourceItem);
                }
            }

            var includeBuildAssets = resolvedItem.GetMetadata("IncludeBuildAssets");
            if (!string.Equals(includeBuildAssets, "false", StringComparison.OrdinalIgnoreCase))
            {
                var buildRoot = Path.Combine(pkgRoot, "build", TargetFramework);
                if (Directory.Exists(buildRoot))
                {
                    foreach (var file in Directory
                                 .EnumerateFiles(buildRoot, "*.props", SearchOption.TopDirectoryOnly)
                                 .Concat(Directory.EnumerateFiles(buildRoot, "*.targets", SearchOption.TopDirectoryOnly)))
                    {
                        var buildItem = new TaskItem(file);
                        buildItem.SetMetadata(
                            "PackagePath",
                            "build/" + TargetFramework + "/" + Path.GetFileName(file));
                        buildAssets.Add(buildItem);
                    }
                }
            }
        }

        ResolvedBundles = resolved.ToArray();
        BundledSources = sources.ToArray();
        BundledBuildAssets = buildAssets.ToArray();
        return true;
    }

    private string ResolvePackageRoot(string identity, string pathProperty)
    {
        var assetsRoot = ResolvePackageRootFromProjectAssets(identity);
        if (!string.IsNullOrWhiteSpace(assetsRoot))
        {
            return assetsRoot;
        }

        string propertyValue;
        if (TryGetEvaluatedProperty(pathProperty, out propertyValue) && !string.IsNullOrWhiteSpace(propertyValue))
        {
            return propertyValue;
        }

        var references = PackageReferences ?? new ITaskItem[0];
        foreach (var reference in references)
        {
            if (!string.Equals(reference.ItemSpec, identity, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var version = reference.GetMetadata("Version");
            if (string.IsNullOrWhiteSpace(version))
            {
                version = reference.GetMetadata("VersionOverride");
            }

            if (string.IsNullOrWhiteSpace(version))
            {
                break;
            }

            var packageRootBase = NuGetPackageRoot;
            if (string.IsNullOrWhiteSpace(packageRootBase))
            {
                packageRootBase = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".nuget",
                    "packages");
            }

            var packageRoot = Path.Combine(
                packageRootBase.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                identity.ToLowerInvariant(),
                version);

            if (Directory.Exists(packageRoot))
            {
                return packageRoot;
            }
        }

        return string.Empty;
    }

    private string ResolvePackageRootFromProjectAssets(string identity)
    {
        if (string.IsNullOrWhiteSpace(ProjectAssetsFile) || !File.Exists(ProjectAssetsFile))
        {
            return string.Empty;
        }

        try
        {
            using (var document = JsonDocument.Parse(File.ReadAllText(ProjectAssetsFile)))
            {
                if (!document.RootElement.TryGetProperty("libraries", out var libraries))
                {
                    return string.Empty;
                }

                var packageRootBase = ResolvePackageRootBase();
                foreach (var library in libraries.EnumerateObject())
                {
                    if (!library.Name.StartsWith(identity + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!library.Value.TryGetProperty("path", out var pathElement))
                    {
                        continue;
                    }

                    var relativePath = pathElement.GetString();
                    if (string.IsNullOrWhiteSpace(relativePath))
                    {
                        continue;
                    }

                    var fullPath = Path.Combine(
                        packageRootBase,
                        relativePath.Replace('/', Path.DirectorySeparatorChar));
                    if (Directory.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.LogWarning("Failed to read project assets for SourcePackBundle '{0}': {1}", identity, ex.Message);
        }

        return string.Empty;
    }

    private string ResolvePackageRootBase()
    {
        if (!string.IsNullOrWhiteSpace(NuGetPackageRoot))
        {
            return NuGetPackageRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        if (!string.IsNullOrWhiteSpace(NuGetPackageFolders))
        {
            var firstFolder = NuGetPackageFolders
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstFolder))
            {
                return firstFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages");
    }

    private bool TryGetEvaluatedProperty(string propertyName, out string value)
    {
        value = string.Empty;
        if (BuildEngine == null)
        {
            return false;
        }

        foreach (var method in BuildEngine.GetType().GetMethods())
        {
            if (!string.Equals(method.Name, "TryGetGlobalProperty", StringComparison.Ordinal))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 2 || parameters[0].ParameterType != typeof(string))
            {
                continue;
            }

            var args = new object[] { propertyName, null };
            if ((bool)method.Invoke(BuildEngine, args))
            {
                value = args[1] as string ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
            }

            return false;
        }

        return false;
    }
}
}
