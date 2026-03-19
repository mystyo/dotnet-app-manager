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
}
