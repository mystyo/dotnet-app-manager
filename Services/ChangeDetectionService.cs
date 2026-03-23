using DotnetAppManager.Models;

namespace DotnetAppManager.Services;

public class ChangeDetectionService
{
    private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj" };

    public List<DependencyNode> GetChangedDependencies(
        List<DependencyNode> dependencies,
        string nugetSourcePath,
        Dictionary<string, List<string>> depGraph)
    {
        var changedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Determine which projects have source changes newer than their .nupkg
        foreach (var node in dependencies)
        {
            if (node.ProjectPath == null) continue;
            if (HasSourceChanges(node, nugetSourcePath))
                changedNames.Add(node.Name);
        }

        // Propagate: if a dependency changed, all dependents must also rebuild
        // Walk the flat list (leaves-first order) and mark dependents
        bool added;
        do
        {
            added = false;
            foreach (var node in dependencies)
            {
                if (changedNames.Contains(node.Name)) continue;
                if (node.ProjectPath == null) continue;

                // Check if any of this project's dependencies are in the changed set
                if (depGraph.TryGetValue(node.Name, out var nodeDeps))
                {
                    if (nodeDeps.Any(d => changedNames.Contains(d)))
                    {
                        changedNames.Add(node.Name);
                        added = true;
                    }
                }
            }
        } while (added);

        return dependencies.Where(n => changedNames.Contains(n.Name)).ToList();
    }

    private static bool HasSourceChanges(DependencyNode node, string nugetSourcePath)
    {
        var projectDir = Path.GetDirectoryName(node.ProjectPath);
        if (projectDir == null || !Directory.Exists(projectDir)) return true;

        var packageTime = GetPackageLastWriteTime(node.Name, nugetSourcePath);
        if (packageTime == null) return true; // No package found — needs build

        var newestSource = GetNewestFileTime(projectDir);
        return newestSource > packageTime.Value;
    }

    private static DateTime? GetPackageLastWriteTime(string projectName, string nugetSourcePath)
    {
        if (!Directory.Exists(nugetSourcePath)) return null;

        // Find .nupkg files matching the project name (e.g. Core.1.0.0.nupkg)
        var packages = Directory.GetFiles(nugetSourcePath, $"{projectName}.*.nupkg");
        if (packages.Length == 0) return null;

        // Return the newest package time
        return packages.Max(f => File.GetLastWriteTimeUtc(f));
    }

    private static DateTime GetNewestFileTime(string directory)
    {
        var newest = DateTime.MinValue;

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            // Skip bin and obj directories
            var relativePath = Path.GetRelativePath(directory, file);
            var topDir = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (ExcludedDirs.Contains(topDir)) continue;

            var writeTime = File.GetLastWriteTimeUtc(file);
            if (writeTime > newest) newest = writeTime;
        }

        return newest;
    }
}
