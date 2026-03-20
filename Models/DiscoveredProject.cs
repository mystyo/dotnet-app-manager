namespace DotnetAppManager.Models;

public class DiscoveredProject
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string Directory { get; set; } = string.Empty;
    public string ProjectFileType { get; set; } = string.Empty;
    public string? TargetFramework { get; set; }
    public string? OutputType { get; set; }
    public string? RootNamespace { get; set; }
    public string? AssemblyName { get; set; }
    public Dictionary<string, string> AdditionalProperties { get; set; } = new();
    public Dictionary<string, Dictionary<string, string>> AppSettings { get; set; } = new();
    public List<string> ProjectReferences { get; set; } = new();

    /// <summary>Direct dependency names as declared in the .csproj</summary>
    public List<string> DependencyNames { get; set; } = new();

    /// <summary>Full dependency tree (resolved with transitive deps)</summary>
    public List<DependencyNode> DependencyTree { get; set; } = new();

    /// <summary>Flattened build order — leaves first, no duplicates</summary>
    public List<DependencyNode> DependencyBuildOrder { get; set; } = new();
}

public class DependencyNode
{
    public string Name { get; set; } = string.Empty;
    public string? ProjectPath { get; set; }
    public List<DependencyNode> Children { get; set; } = new();
}
