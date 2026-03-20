using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using DotnetAppManager.Models;
using DotnetAppManager.Services;

namespace DotnetAppManager.Pages;

public class IndexModel : PageModel
{
    private readonly ConfigService _configService;
    private readonly ProjectDiscoveryService _discoveryService;
    private readonly ProcessManagerService _processManager;
    private readonly ProjectPreferencesService _preferencesService;
    private readonly ProfileService _profileService;

    public IndexModel(ConfigService configService, ProjectDiscoveryService discoveryService, ProcessManagerService processManager, ProjectPreferencesService preferencesService, ProfileService profileService)
    {
        _configService = configService;
        _discoveryService = discoveryService;
        _processManager = processManager;
        _preferencesService = preferencesService;
        _profileService = profileService;
    }

    public List<DiscoveredProject> Projects { get; set; } = [];
    public List<string> CurrentPaths { get; set; } = [];
    public List<string> BuildConfigurations { get; set; } = [];
    public List<Profile> Profiles { get; set; } = [];
    public string? SelectedProfile { get; set; }
    public Dictionary<string, ProcessInfo?> ActiveProcesses { get; set; } = new();
    public Dictionary<string, string> KnownProjectPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void OnGet([FromQuery] string? profile)
    {
        var config = _configService.GetConfig();
        CurrentPaths = config.EnabledFolderPaths;
        BuildConfigurations = config.BuildConfigurations;
        Profiles = _profileService.GetAll();
        SelectedProfile = profile;

        var allPrefs = _preferencesService.GetAll();
        var projects = _discoveryService.ScanFolders(CurrentPaths)
            .Where(p => !(allPrefs.GetValueOrDefault(p.FullPath)?.Ignored ?? false))
            .ToList();

        if (!string.IsNullOrEmpty(profile))
        {
            var profileData = _profileService.Get(profile);
            if (profileData != null)
            {
                var profilePaths = new HashSet<string>(profileData.ProjectPaths, StringComparer.OrdinalIgnoreCase);
                projects = projects.Where(p => profilePaths.Contains(p.FullPath)).ToList();
            }
        }

        Projects = projects;
        KnownProjectPaths = _discoveryService.ScanAllProjectPaths(config.AllFolderPaths);

        foreach (var project in Projects)
        {
            ActiveProcesses[project.Id] = _processManager.GetActiveProcess(project.Id);
        }
    }
}
