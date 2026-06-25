using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        string propertyValue;
        if (TryGetGlobalProperty(pathProperty, out propertyValue) && !string.IsNullOrWhiteSpace(propertyValue))
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

    private bool TryGetGlobalProperty(string propertyName, out string value)
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
                return true;
            }

            return false;
        }

        return false;
    }
}
}
