namespace DotnetAppManager.Models;

public class Profile
{
    public string Name { get; set; } = string.Empty;
    public List<string> ProjectPaths { get; set; } = [];
}
