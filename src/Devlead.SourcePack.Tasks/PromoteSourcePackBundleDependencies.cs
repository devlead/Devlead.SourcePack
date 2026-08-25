using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Devlead.SourcePack
{

/// <summary>
/// Promotes direct NuGet dependencies of <c>SourcePackBundle</c> packages into the packing project's assets/dgspec.
/// </summary>
public sealed class PromoteSourcePackBundleDependencies : Task
{
    /// <summary>
    /// Gets or sets the bundle definitions declared in the project.
    /// </summary>
    [Required]
    public ITaskItem[] Bundles { get; set; }

    /// <summary>
    /// Gets or sets the path to <c>project.assets.json</c>.
    /// </summary>
    [Required]
    public string ProjectAssetsFile { get; set; }

    /// <summary>
    /// Gets or sets the path to the matching <c>*.nuget.dgspec.json</c> file.
    /// </summary>
    public string DgspecFile { get; set; }

    /// <summary>
    /// Gets or sets the NuGet global packages folder.
    /// </summary>
    public string NuGetPackageRoot { get; set; }

    /// <summary>
    /// Gets or sets the semicolon-separated NuGet package folders from restore.
    /// </summary>
    public string NuGetPackageFolders { get; set; }

    /// <summary>
    /// Gets or sets package references to add for promoted direct dependencies.
    /// </summary>
    [Output]
    public ITaskItem[] PromotedPackageReferences { get; set; }

    /// <inheritdoc />
    public override bool Execute()
    {
        PromotedPackageReferences = Array.Empty<ITaskItem>();

        if (!File.Exists(ProjectAssetsFile))
        {
            Log.LogWarning(
                "SourcePackBundle PromoteDependencies: project.assets.json was not found at '{0}'.",
                ProjectAssetsFile);
            return true;
        }

        var allBundles = Bundles ?? Array.Empty<ITaskItem>();
        if (allBundles.Length == 0)
        {
            return true;
        }

        var bundledIds = new HashSet<string>(
            allBundles.Select(bundle => bundle.ItemSpec),
            StringComparer.OrdinalIgnoreCase);

        var promoteBundles = allBundles
            .Where(bundle => !string.Equals(
                bundle.GetMetadata("PromoteDependencies"),
                "false",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var assetsRoot = JsonNode.Parse(File.ReadAllText(ProjectAssetsFile))?.AsObject();
        if (assetsRoot == null)
        {
            return true;
        }

        var packageFolders = ResolvePackageFolders(assetsRoot);
        var promotedByFramework = promoteBundles.Length == 0
            ? new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            : CollectPromotedDependencies(promoteBundles, bundledIds, assetsRoot, packageFolders);

        PromotedPackageReferences = BuildPromotedPackageReferenceItems(promotedByFramework, bundledIds);

        var assetsChanged = ApplyPromotions(
            assetsRoot["project"]?["frameworks"]?.AsObject(),
            promotedByFramework,
            bundledIds);
        if (assetsChanged)
        {
            WriteJson(ProjectAssetsFile, assetsRoot);
        }

        if (!string.IsNullOrWhiteSpace(DgspecFile) && File.Exists(DgspecFile))
        {
            var dgspecRoot = JsonNode.Parse(File.ReadAllText(DgspecFile))?.AsObject();
            if (dgspecRoot != null)
            {
                var dgspecChanged = false;
                var projects = dgspecRoot["projects"]?.AsObject();
                if (projects != null)
                {
                    foreach (var project in projects)
                    {
                        dgspecChanged |= ApplyPromotions(
                            project.Value?["frameworks"]?.AsObject(),
                            promotedByFramework,
                            bundledIds);
                    }
                }

                if (dgspecChanged)
                {
                    WriteJson(DgspecFile, dgspecRoot);
                }
            }
        }

        return true;
    }

    private static ITaskItem[] BuildPromotedPackageReferenceItems(
        Dictionary<string, Dictionary<string, string>> promotedByFramework,
        HashSet<string> bundledIds)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var framework in promotedByFramework.Values)
        {
            foreach (var dep in framework)
            {
                if (bundledIds.Contains(dep.Key))
                {
                    continue;
                }

                if (!merged.TryGetValue(dep.Key, out var existingVersion)
                    || IsHigherVersion(dep.Value, existingVersion))
                {
                    merged[dep.Key] = dep.Value;
                }
            }
        }

        return merged
            .Select(pair =>
            {
                var item = new TaskItem(pair.Key);
                item.SetMetadata("Version", StripAssetsVersionRange(pair.Value));
                return (ITaskItem)item;
            })
            .ToArray();
    }

    private static string StripAssetsVersionRange(string version)
    {
        var trimmed = version.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal)
            || trimmed.StartsWith("(", StringComparison.Ordinal))
        {
            trimmed = trimmed.TrimStart('[', '(');
            var commaIndex = trimmed.IndexOf(',');
            if (commaIndex >= 0)
            {
                trimmed = trimmed.Substring(0, commaIndex);
            }
        }

        return trimmed.Trim();
    }

    private Dictionary<string, Dictionary<string, string>> CollectPromotedDependencies(
        ITaskItem[] bundles,
        HashSet<string> bundledIds,
        JsonObject assetsRoot,
        IReadOnlyList<string> packageFolders)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var libraries = assetsRoot["libraries"]?.AsObject();
        var projectFrameworks = assetsRoot["project"]?["frameworks"]?.AsObject();
        if (libraries == null || projectFrameworks == null)
        {
            return result;
        }

        foreach (var framework in projectFrameworks)
        {
            var frameworkKey = framework.Key;
            var shortTfm = NormalizeTfm(frameworkKey);
            if (!result.TryGetValue(frameworkKey, out var frameworkDeps))
            {
                frameworkDeps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                result[frameworkKey] = frameworkDeps;
            }

            foreach (var bundle in bundles)
            {
                var identity = bundle.ItemSpec;
                if (!TryFindLibrary(libraries, identity, out var libraryKey, out var libraryNode))
                {
                    Log.LogWarning(
                        "SourcePackBundle PromoteDependencies: package '{0}' was not found in project.assets.json.",
                        identity);
                    continue;
                }

                var packageRoot = ResolvePackageRoot(libraryNode, packageFolders);
                var directDeps = ReadDirectDependencies(packageRoot, identity, libraryKey, shortTfm, assetsRoot);
                foreach (var dep in directDeps)
                {
                    if (bundledIds.Contains(dep.Key))
                    {
                        continue;
                    }

                    if (!frameworkDeps.TryGetValue(dep.Key, out var existingVersion)
                        || IsHigherVersion(dep.Value, existingVersion))
                    {
                        frameworkDeps[dep.Key] = dep.Value;
                    }
                }
            }
        }

        return result;
    }

    private static Dictionary<string, string> ReadDirectDependencies(
        string packageRoot,
        string identity,
        string libraryKey,
        string shortTfm,
        JsonObject assetsRoot)
    {
        var fromNuspec = ReadDirectDependenciesFromNuspec(packageRoot, shortTfm);
        if (fromNuspec.Count > 0)
        {
            return fromNuspec;
        }

        return ReadDirectDependenciesFromAssets(assetsRoot, libraryKey, shortTfm);
    }

    private static Dictionary<string, string> ReadDirectDependenciesFromNuspec(string packageRoot, string shortTfm)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(packageRoot) || !Directory.Exists(packageRoot))
        {
            return result;
        }

        var nuspecPath = Directory.EnumerateFiles(packageRoot, "*.nuspec", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
        if (nuspecPath == null)
        {
            return result;
        }

        XDocument document;
        try
        {
            document = XDocument.Load(nuspecPath);
        }
        catch
        {
            return result;
        }

        XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;
        var groups = document.Descendants(ns + "group").ToList();
        if (groups.Count == 0)
        {
            foreach (var dependency in document.Descendants(ns + "dependency"))
            {
                AddDependencyElement(result, dependency);
            }

            return result;
        }

        var matchingGroup = groups.FirstOrDefault(group =>
            string.Equals(
                NormalizeTfm(group.Attribute("targetFramework")?.Value),
                shortTfm,
                StringComparison.OrdinalIgnoreCase));

        if (matchingGroup == null)
        {
            matchingGroup = groups.FirstOrDefault(group =>
                string.IsNullOrWhiteSpace(group.Attribute("targetFramework")?.Value));
        }

        if (matchingGroup == null)
        {
            return result;
        }

        foreach (var dependency in matchingGroup.Elements(ns + "dependency"))
        {
            AddDependencyElement(result, dependency);
        }

        return result;
    }

    private static void AddDependencyElement(Dictionary<string, string> result, XElement dependency)
    {
        var id = dependency.Attribute("id")?.Value;
        var version = dependency.Attribute("version")?.Value;
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
        {
            return;
        }

        result[id] = ToAssetsVersion(version);
    }

    private static string ToAssetsVersion(string version)
    {
        var trimmed = version.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal)
            || trimmed.StartsWith("(", StringComparison.Ordinal)
            || trimmed.IndexOf(',') >= 0)
        {
            return trimmed;
        }

        return "[" + trimmed + ", )";
    }

    private static Dictionary<string, string> ReadDirectDependenciesFromAssets(
        JsonObject assetsRoot,
        string libraryKey,
        string shortTfm)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var targets = assetsRoot["targets"]?.AsObject();
        if (targets == null)
        {
            return result;
        }

        foreach (var target in targets)
        {
            if (!string.Equals(NormalizeTfm(target.Key), shortTfm, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var packageNode = target.Value?[libraryKey]?.AsObject();
            var dependencies = packageNode?["dependencies"]?.AsObject();
            if (dependencies == null)
            {
                continue;
            }

            foreach (var dependency in dependencies)
            {
                var version = dependency.Value?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(version))
                {
                    result[dependency.Key] = ToAssetsVersion(version);
                }
            }
        }

        return result;
    }

    private static bool ApplyPromotions(
        JsonObject frameworks,
        Dictionary<string, Dictionary<string, string>> promotedByFramework,
        HashSet<string> bundledIds)
    {
        if (frameworks == null)
        {
            return false;
        }

        var changed = false;

        foreach (var framework in frameworks)
        {
            var frameworkNode = framework.Value?.AsObject();
            if (frameworkNode == null)
            {
                continue;
            }

            var dependencies = frameworkNode["dependencies"]?.AsObject();
            if (dependencies == null)
            {
                dependencies = new JsonObject();
                frameworkNode["dependencies"] = dependencies;
            }

            foreach (var bundledId in bundledIds)
            {
                if (dependencies.Remove(bundledId))
                {
                    changed = true;
                }
            }

            if (!promotedByFramework.TryGetValue(framework.Key, out var promotions)
                && !TryFindFrameworkPromotions(framework.Key, promotedByFramework, out promotions))
            {
                continue;
            }

            foreach (var promotion in promotions)
            {
                if (bundledIds.Contains(promotion.Key))
                {
                    continue;
                }

                if (dependencies.TryGetPropertyValue(promotion.Key, out var existingNode))
                {
                    var existing = existingNode?.AsObject();
                    var existingVersion = existing?["version"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(existingVersion)
                        && !IsHigherVersion(promotion.Value, existingVersion))
                    {
                        continue;
                    }

                    if (existing == null)
                    {
                        existing = new JsonObject();
                        dependencies[promotion.Key] = existing;
                    }

                    existing["version"] = promotion.Value;
                    existing["target"] = "Package";
                    changed = true;
                    continue;
                }

                dependencies[promotion.Key] = new JsonObject
                {
                    ["target"] = "Package",
                    ["version"] = promotion.Value
                };
                changed = true;
            }
        }

        return changed;
    }

    private static bool TryFindFrameworkPromotions(
        string frameworkKey,
        Dictionary<string, Dictionary<string, string>> promotedByFramework,
        out Dictionary<string, string> promotions)
    {
        var shortTfm = NormalizeTfm(frameworkKey);
        foreach (var entry in promotedByFramework)
        {
            if (string.Equals(NormalizeTfm(entry.Key), shortTfm, StringComparison.OrdinalIgnoreCase))
            {
                promotions = entry.Value;
                return true;
            }
        }

        promotions = null;
        return false;
    }

    private static bool TryFindLibrary(
        JsonObject libraries,
        string identity,
        out string libraryKey,
        out JsonObject libraryNode)
    {
        foreach (var library in libraries)
        {
            if (!library.Key.StartsWith(identity + "/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            libraryKey = library.Key;
            libraryNode = library.Value?.AsObject();
            return libraryNode != null;
        }

        libraryKey = null;
        libraryNode = null;
        return false;
    }

    private string ResolvePackageRoot(JsonObject libraryNode, IReadOnlyList<string> packageFolders)
    {
        var relativePath = libraryNode?["path"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        foreach (var folder in packageFolders)
        {
            var fullPath = Path.Combine(
                folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return string.Empty;
    }

    private IReadOnlyList<string> ResolvePackageFolders(JsonObject assetsRoot)
    {
        var folders = new List<string>();
        var packageFolders = assetsRoot["packageFolders"]?.AsObject();
        if (packageFolders != null)
        {
            foreach (var folder in packageFolders)
            {
                if (!string.IsNullOrWhiteSpace(folder.Key))
                {
                    folders.Add(folder.Key);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(NuGetPackageRoot))
        {
            folders.Add(NuGetPackageRoot);
        }

        if (!string.IsNullOrWhiteSpace(NuGetPackageFolders))
        {
            folders.AddRange(
                NuGetPackageFolders.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
        }

        if (folders.Count == 0)
        {
            folders.Add(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".nuget",
                    "packages"));
        }

        return folders
            .Select(folder => folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeTfm(string tfm)
    {
        if (string.IsNullOrWhiteSpace(tfm))
        {
            return string.Empty;
        }

        var value = tfm.Trim();
        var commaIndex = value.IndexOf(',');
        if (commaIndex >= 0)
        {
            value = value.Substring(0, commaIndex);
        }

        if (value.StartsWith(".NETCoreApp,Version=v", StringComparison.OrdinalIgnoreCase))
        {
            value = "net" + value.Substring(".NETCoreApp,Version=v".Length);
        }
        else if (value.StartsWith(".NETStandard,Version=v", StringComparison.OrdinalIgnoreCase))
        {
            value = "netstandard" + value.Substring(".NETStandard,Version=v".Length);
        }
        else if (value.StartsWith(".NETFramework,Version=v", StringComparison.OrdinalIgnoreCase))
        {
            value = "net" + value.Substring(".NETFramework,Version=v".Length).Replace(".", string.Empty);
        }

        return value.ToLowerInvariant();
    }

    private static bool IsHigherVersion(string candidate, string existing)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var candidateParts = ParseVersionParts(candidate);
        var existingParts = ParseVersionParts(existing);
        var length = Math.Max(candidateParts.Length, existingParts.Length);
        for (var i = 0; i < length; i++)
        {
            var candidatePart = i < candidateParts.Length ? candidateParts[i] : 0;
            var existingPart = i < existingParts.Length ? existingParts[i] : 0;
            if (candidatePart != existingPart)
            {
                return candidatePart > existingPart;
            }
        }

        return false;
    }

    private static int[] ParseVersionParts(string version)
    {
        var core = version.Trim();
        if (core.StartsWith("[", StringComparison.Ordinal)
            || core.StartsWith("(", StringComparison.Ordinal))
        {
            core = core.TrimStart('[', '(');
            var commaIndex = core.IndexOf(',');
            if (commaIndex >= 0)
            {
                core = core.Substring(0, commaIndex);
            }
        }

        var slashIndex = core.IndexOf('/');
        if (slashIndex >= 0)
        {
            core = core.Substring(0, slashIndex);
        }

        var dashIndex = core.IndexOf('-');
        if (dashIndex >= 0)
        {
            core = core.Substring(0, dashIndex);
        }

        return core
            .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
                int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                    ? value
                    : 0)
            .ToArray();
    }

    private static void WriteJson(string path, JsonObject root)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        File.WriteAllText(path, root.ToJsonString(options));
    }
}

}
