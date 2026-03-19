using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using DotnetAppManager.Models;
using DotnetAppManager.Services;

namespace DotnetAppManager.Pages;

public class ConfigurationModel : PageModel
{
    private readonly ConfigService _configService;
    private readonly ProjectDiscoveryService _discoveryService;

    public ConfigurationModel(ConfigService configService, ProjectDiscoveryService discoveryService)
    {
        _configService = configService;
        _discoveryService = discoveryService;
    }

    [BindProperty]
    public List<string> TargetFolderPaths { get; set; } = [];

    [BindProperty]
    public string? NewFolderPath { get; set; }

    [BindProperty]
    public List<string> BuildConfigurations { get; set; } = [];

    [BindProperty]
    public string? NewBuildConfiguration { get; set; }

    public string? Message { get; set; }
    public bool IsError { get; set; }
    public int ProjectCount { get; set; }

    public void OnGet()
    {
        var config = _configService.GetConfig();
        TargetFolderPaths = config.TargetFolderPaths;
        BuildConfigurations = config.BuildConfigurations;
        ProjectCount = _discoveryService.ScanFolders(TargetFolderPaths).Count;
    }

    public IActionResult OnPostAdd()
    {
        LoadBuildConfigurations();

        if (string.IsNullOrWhiteSpace(NewFolderPath))
        {
            Message = "Path cannot be empty.";
            IsError = true;
            ProjectCount = _discoveryService.ScanFolders(TargetFolderPaths).Count;
            return Page();
        }

        if (!Directory.Exists(NewFolderPath))
        {
            Message = $"Directory does not exist: {NewFolderPath}";
            IsError = true;
            ProjectCount = _discoveryService.ScanFolders(TargetFolderPaths).Count;
            return Page();
        }

        if (TargetFolderPaths.Contains(NewFolderPath, StringComparer.OrdinalIgnoreCase))
        {
            Message = "This folder is already in the list.";
            IsError = true;
            ProjectCount = _discoveryService.ScanFolders(TargetFolderPaths).Count;
            return Page();
        }

        TargetFolderPaths.Add(NewFolderPath);
        SaveFullConfig();
        ProjectCount = _discoveryService.ScanFolders(TargetFolderPaths).Count;
        Message = $"Folder added. Found {ProjectCount} project(s) total.";
        NewFolderPath = string.Empty;
        return Page();
    }

    public IActionResult OnPostRemove(int index)
    {
        LoadBuildConfigurations();

        if (index >= 0 && index < TargetFolderPaths.Count)
        {
            TargetFolderPaths.RemoveAt(index);
            SaveFullConfig();
        }

        ProjectCount = _discoveryService.ScanFolders(TargetFolderPaths).Count;
        Message = $"Folder removed. Found {ProjectCount} project(s) total.";
        return Page();
    }

    public IActionResult OnPostAddConfig()
    {
        if (string.IsNullOrWhiteSpace(NewBuildConfiguration))
        {
            Message = "Configuration name cannot be empty.";
            IsError = true;
            ProjectCount = _discoveryService.ScanFolders(TargetFolderPaths).Count;
            return Page();
        }

        if (BuildConfigurations.Contains(NewBuildConfiguration, StringComparer.OrdinalIgnoreCase))
        {
            Message = "This configuration already exists.";
            IsError = true;
            ProjectCount = _discoveryService.ScanFolders(TargetFolderPaths).Count;
            return Page();
        }

        BuildConfigurations.Add(NewBuildConfiguration);
        SaveFullConfig();
        ProjectCount = _discoveryService.ScanFolders(TargetFolderPaths).Count;
        Message = $"Configuration \"{NewBuildConfiguration}\" added.";
        NewBuildConfiguration = string.Empty;
        return Page();
    }

    public IActionResult OnPostRemoveConfig(int index)
    {
        if (index >= 0 && index < BuildConfigurations.Count)
        {
            var removed = BuildConfigurations[index];
            BuildConfigurations.RemoveAt(index);
            SaveFullConfig();
            Message = $"Configuration \"{removed}\" removed.";
        }

        ProjectCount = _discoveryService.ScanFolders(TargetFolderPaths).Count;
        return Page();
    }

    private void SaveFullConfig()
    {
        _configService.SaveConfig(new AppConfig
        {
            TargetFolderPaths = TargetFolderPaths,
            BuildConfigurations = BuildConfigurations
        });
    }

    private void LoadBuildConfigurations()
    {
        if (BuildConfigurations.Count == 0)
            BuildConfigurations = _configService.GetConfig().BuildConfigurations;
    }
}
