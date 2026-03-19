namespace DotnetAppManager.Models;

public class ProjectPreference
{
    public bool Ignored { get; set; }
    public int StartOrder { get; set; } = 100;
}
