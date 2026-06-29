using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Devlead.SourcePack
{

/// <summary>
/// Ensures packed nuspec dependencies flow build and analyzer assets to consumers.
/// </summary>
public sealed class PatchSourcePackDependencyFlow : Task
{
    /// <summary>
    /// Gets or sets the path to <c>project.assets.json</c>.
    /// </summary>
    [Required]
    public string ProjectAssetsFile { get; set; }

    /// <summary>
    /// Gets or sets the path to the matching <c>*.nuget.dgspec.json</c> file.
    /// </summary>
    public string DgspecFile { get; set; }

    /// <inheritdoc />
    public override bool Execute()
    {
        if (!File.Exists(ProjectAssetsFile))
        {
            Log.LogWarning("SourcePackFlowBuildAssets: project.assets.json was not found at '{0}'.", ProjectAssetsFile);
            return true;
        }

        PatchDependencyFlow(ProjectAssetsFile, "project");

        if (!string.IsNullOrWhiteSpace(DgspecFile) && File.Exists(DgspecFile))
        {
            PatchDependencyFlow(DgspecFile, "projects");
        }

        return true;
    }

    private static void PatchDependencyFlow(string jsonPath, string rootSectionName)
    {
        var root = JsonNode.Parse(File.ReadAllText(jsonPath))?.AsObject();
        if (root == null)
        {
            return;
        }

        var changed = false;

        if (string.Equals(rootSectionName, "project", StringComparison.Ordinal))
        {
            changed |= PatchFrameworkDependencies(root["project"]?["frameworks"]?.AsObject());
        }
        else
        {
            var projects = root["projects"]?.AsObject();
            if (projects != null)
            {
                foreach (var project in projects)
                {
                    changed |= PatchFrameworkDependencies(project.Value?["frameworks"]?.AsObject());
                }
            }
        }

        if (!changed)
        {
            return;
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        File.WriteAllText(jsonPath, root.ToJsonString(options));
    }

    private static bool PatchFrameworkDependencies(JsonObject frameworks)
    {
        if (frameworks == null)
        {
            return false;
        }

        var changed = false;

        foreach (var framework in frameworks)
        {
            var dependencies = framework.Value?["dependencies"]?.AsObject();
            if (dependencies == null)
            {
                continue;
            }

            foreach (var dependency in dependencies)
            {
                var dependencyNode = dependency.Value?.AsObject();
                if (dependencyNode == null || dependencyNode.ContainsKey("suppressParent"))
                {
                    continue;
                }

                dependencyNode["suppressParent"] = "None";
                changed = true;
            }
        }

        return changed;
    }
}

}
