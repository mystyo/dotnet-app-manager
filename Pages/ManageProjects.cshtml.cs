using Microsoft.AspNetCore.Mvc.RazorPages;
using DotnetAppManager.Models;
using DotnetAppManager.Services;

namespace DotnetAppManager.Pages;

public class ManageProjectsModel : PageModel
{
    private readonly ConfigService _configService;
    private readonly ProjectDiscoveryService _discoveryService;
    private readonly ProjectPreferencesService _preferencesService;

    public ManageProjectsModel(ConfigService configService, ProjectDiscoveryService discoveryService, ProjectPreferencesService preferencesService)
    {
        _configService = configService;
        _discoveryService = discoveryService;
        _preferencesService = preferencesService;
    }

    public List<DiscoveredProject> Projects { get; set; } = [];
    public Dictionary<string, ProjectPreference> Preferences { get; set; } = new();

    public void OnGet()
    {
        var config = _configService.GetConfig();
        Projects = _discoveryService.ScanFolders(config.EnabledFolderPaths);
        Preferences = _preferencesService.GetAll();
    }
}
