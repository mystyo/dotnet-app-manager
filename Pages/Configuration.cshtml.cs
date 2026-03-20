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

    public List<TargetFolder> TargetFolders { get; set; } = [];
    public List<string> BuildConfigurations { get; set; } = [];

    [BindProperty]
    public string? NewFolderPath { get; set; }

    [BindProperty]
    public string? NewBuildConfiguration { get; set; }

    public string? Message { get; set; }
    public bool IsError { get; set; }
    public int ProjectCount { get; set; }

    public void OnGet()
    {
        LoadFromConfig();
    }

    public IActionResult OnPostAdd()
    {
        LoadFromConfig();

        if (string.IsNullOrWhiteSpace(NewFolderPath))
        {
            Message = "Path cannot be empty.";
            IsError = true;
            return Page();
        }

        if (!Directory.Exists(NewFolderPath))
        {
            Message = $"Directory does not exist: {NewFolderPath}";
            IsError = true;
            return Page();
        }

        if (TargetFolders.Any(f => string.Equals(f.Path, NewFolderPath, StringComparison.OrdinalIgnoreCase)))
        {
            Message = "This folder is already in the list.";
            IsError = true;
            return Page();
        }

        TargetFolders.Add(new TargetFolder { Path = NewFolderPath, Enabled = true });
        SaveAndReload();
        Message = $"Folder added. Found {ProjectCount} project(s) total.";
        NewFolderPath = string.Empty;
        return Page();
    }

    public IActionResult OnPostRemove(int index)
    {
        LoadFromConfig();

        if (index >= 0 && index < TargetFolders.Count)
        {
            TargetFolders.RemoveAt(index);
            SaveAndReload();
            Message = $"Folder removed. Found {ProjectCount} project(s) total.";
        }

        return Page();
    }

    public IActionResult OnPostToggle(int index)
    {
        LoadFromConfig();

        if (index >= 0 && index < TargetFolders.Count)
        {
            TargetFolders[index].Enabled = !TargetFolders[index].Enabled;
            SaveAndReload();
            var folder = TargetFolders[index];
            Message = $"Folder {(folder.Enabled ? "enabled" : "disabled")}. Found {ProjectCount} project(s) total.";
        }

        return Page();
    }

    public IActionResult OnPostAddConfig()
    {
        LoadFromConfig();

        if (string.IsNullOrWhiteSpace(NewBuildConfiguration))
        {
            Message = "Configuration name cannot be empty.";
            IsError = true;
            return Page();
        }

        if (BuildConfigurations.Contains(NewBuildConfiguration, StringComparer.OrdinalIgnoreCase))
        {
            Message = "This configuration already exists.";
            IsError = true;
            return Page();
        }

        BuildConfigurations.Add(NewBuildConfiguration);
        SaveAndReload();
        Message = $"Configuration \"{NewBuildConfiguration}\" added.";
        NewBuildConfiguration = string.Empty;
        return Page();
    }

    public IActionResult OnPostRemoveConfig(int index)
    {
        LoadFromConfig();

        if (index >= 0 && index < BuildConfigurations.Count)
        {
            var removed = BuildConfigurations[index];
            BuildConfigurations.RemoveAt(index);
            SaveAndReload();
            Message = $"Configuration \"{removed}\" removed.";
        }

        return Page();
    }

    private void LoadFromConfig()
    {
        var config = _configService.GetConfig();
        TargetFolders = config.TargetFolders;
        BuildConfigurations = config.BuildConfigurations;
        ProjectCount = _discoveryService.ScanFolders(config.EnabledFolderPaths).Count;
    }

    private void SaveAndReload()
    {
        _configService.SaveConfig(new AppConfig
        {
            TargetFolders = TargetFolders,
            BuildConfigurations = BuildConfigurations
        });
        ProjectCount = _discoveryService.ScanFolders(
            TargetFolders.Where(f => f.Enabled).Select(f => f.Path).ToList()
        ).Count;
    }
}
