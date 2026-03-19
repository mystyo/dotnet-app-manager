using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using DotnetAppManager.Models;
using DotnetAppManager.Services;

namespace DotnetAppManager.Pages;

public class ProfilesModel : PageModel
{
    private readonly ProfileService _profileService;
    private readonly ConfigService _configService;
    private readonly ProjectDiscoveryService _discoveryService;
    private readonly ProjectPreferencesService _preferencesService;

    public ProfilesModel(ProfileService profileService, ConfigService configService, ProjectDiscoveryService discoveryService, ProjectPreferencesService preferencesService)
    {
        _profileService = profileService;
        _configService = configService;
        _discoveryService = discoveryService;
        _preferencesService = preferencesService;
    }

    public List<Profile> Profiles { get; set; } = [];
    public List<DiscoveredProject> AllProjects { get; set; } = [];
    public string? EditingProfile { get; set; }
    public string? Message { get; set; }
    public bool IsError { get; set; }

    [BindProperty]
    public string? NewProfileName { get; set; }

    [BindProperty]
    public string? ProfileName { get; set; }

    [BindProperty]
    public List<string> SelectedProjects { get; set; } = [];

    public void OnGet(string? edit)
    {
        LoadData();
        EditingProfile = edit;
    }

    public IActionResult OnPostCreate()
    {
        LoadData();

        if (string.IsNullOrWhiteSpace(NewProfileName))
        {
            Message = "Profile name cannot be empty.";
            IsError = true;
            return Page();
        }

        if (_profileService.Get(NewProfileName) != null)
        {
            Message = $"Profile \"{NewProfileName}\" already exists.";
            IsError = true;
            return Page();
        }

        _profileService.Save(new Profile { Name = NewProfileName, ProjectPaths = [] });
        Message = $"Profile \"{NewProfileName}\" created.";
        Profiles = _profileService.GetAll();
        EditingProfile = NewProfileName;
        NewProfileName = string.Empty;
        return Page();
    }

    public IActionResult OnPostSave()
    {
        LoadData();

        if (string.IsNullOrWhiteSpace(ProfileName))
            return Page();

        _profileService.Save(new Profile { Name = ProfileName, ProjectPaths = SelectedProjects });
        Message = $"Profile \"{ProfileName}\" saved with {SelectedProjects.Count} project(s).";
        Profiles = _profileService.GetAll();
        EditingProfile = ProfileName;
        return Page();
    }

    public IActionResult OnPostDelete()
    {
        LoadData();

        if (!string.IsNullOrWhiteSpace(ProfileName))
        {
            _profileService.Delete(ProfileName);
            Message = $"Profile \"{ProfileName}\" deleted.";
            Profiles = _profileService.GetAll();
        }

        return Page();
    }

    private void LoadData()
    {
        Profiles = _profileService.GetAll();
        var config = _configService.GetConfig();
        var allPrefs = _preferencesService.GetAll();
        AllProjects = _discoveryService.ScanFolders(config.TargetFolderPaths)
            .Where(p => !(allPrefs.GetValueOrDefault(p.FullPath)?.Ignored ?? false))
            .ToList();
    }
}
