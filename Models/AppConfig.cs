namespace DotnetAppManager.Models;

public class AppConfig
{
    public List<string> TargetFolderPaths { get; set; } = [];
    public List<string> BuildConfigurations { get; set; } = ["Debug", "Release"];
}
