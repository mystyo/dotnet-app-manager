using System.Text.Json.Serialization;

namespace DotnetAppManager.Models;

public class AppConfig
{
    public List<TargetFolder> TargetFolders { get; set; } = [];
    public List<string> BuildConfigurations { get; set; } = ["Debug", "Release"];
    public string? DefaultBuildConfiguration { get; set; }
    public string? NugetSourcePath { get; set; }
    public string? MigrationAssemblyPath { get; set; }
    public string MigrationProjectSuffix { get; set; } = "Db.Migrations";
    public string? MigrationConnectionString { get; set; }

    [JsonIgnore]
    public List<string> EnabledFolderPaths => TargetFolders
        .Where(f => f.Enabled)
        .Select(f => f.Path)
        .ToList();

    [JsonIgnore]
    public List<string> AllFolderPaths => TargetFolders
        .Select(f => f.Path)
        .ToList();
}

public class TargetFolder
{
    public string Path { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}
