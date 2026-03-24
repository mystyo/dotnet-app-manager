using Microsoft.AspNetCore.Mvc.RazorPages;
using DotnetAppManager.Models;
using DotnetAppManager.Services;

namespace DotnetAppManager.Pages;

public class DbMigratorModel : PageModel
{
    private readonly ConfigService _configService;
    private readonly ProjectDiscoveryService _discoveryService;

    public DbMigratorModel(ConfigService configService, ProjectDiscoveryService discoveryService)
    {
        _configService = configService;
        _discoveryService = discoveryService;
    }

    public List<DiscoveredProject> MigrationProjects { get; set; } = [];
    public string? MigrationAssemblyPath { get; set; }
    public string MigrationProjectSuffix { get; set; } = "Db.Migrations";
    public List<string> BuildConfigurations { get; set; } = [];
    public string? DefaultBuildConfiguration { get; set; }

    public void OnGet()
    {
        var config = _configService.GetConfig();
        MigrationAssemblyPath = config.MigrationAssemblyPath;
        MigrationProjectSuffix = config.MigrationProjectSuffix;
        BuildConfigurations = config.BuildConfigurations;
        DefaultBuildConfiguration = config.DefaultBuildConfiguration;

        var allMigrationProjects = _discoveryService.ScanFoldersBySuffix(config.EnabledFolderPaths, MigrationProjectSuffix);

        // Collect all project names that appear as a dependency of another migration project
        var dependencyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in allMigrationProjects)
        {
            foreach (var dep in project.DependencyBuildOrder)
            {
                // Only consider dependencies that are themselves migration projects
                if (allMigrationProjects.Any(p => p.Name.Equals(dep.Name, StringComparison.OrdinalIgnoreCase)))
                    dependencyNames.Add(dep.Name);
            }
        }

        // Filter each project's dependency list to only include migration projects
        var migrationNames = new HashSet<string>(
            allMigrationProjects.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var project in allMigrationProjects)
        {
            project.DependencyBuildOrder = project.DependencyBuildOrder
                .Where(d => migrationNames.Contains(d.Name))
                .ToList();
        }

        // Show only top-level projects (not a dependency of any other migration project)
        MigrationProjects = allMigrationProjects
            .Where(p => !dependencyNames.Contains(p.Name))
            .ToList();
    }
}
