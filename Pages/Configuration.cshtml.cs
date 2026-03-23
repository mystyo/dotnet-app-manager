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
    public string? DefaultBuildConfiguration { get; set; }

    [BindProperty]
    public string? NewFolderPath { get; set; }

    [BindProperty]
    public string? NewBuildConfiguration { get; set; }

    [BindProperty]
    public string? NugetSourcePath { get; set; }

    [BindProperty]
    public string? MigrationAssemblyPath { get; set; }

    [BindProperty]
    public string MigrationProjectSuffix { get; set; } = "Db.Migrations";

    [BindProperty]
    public string? MigrationConnectionString { get; set; }

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

    public IActionResult OnPostSetDefaultConfig(string name)
    {
        LoadFromConfig();
        DefaultBuildConfiguration = name;
        SaveAndReload();
        Message = $"Configuration \"{name}\" set as default.";
        return Page();
    }

    public IActionResult OnPostRemoveConfig(int index)
    {
        LoadFromConfig();

        if (index >= 0 && index < BuildConfigurations.Count)
        {
            var removed = BuildConfigurations[index];
            BuildConfigurations.RemoveAt(index);
            if (string.Equals(DefaultBuildConfiguration, removed, StringComparison.OrdinalIgnoreCase))
                DefaultBuildConfiguration = null;
            SaveAndReload();
            Message = $"Configuration \"{removed}\" removed.";
        }

        return Page();
    }

    public IActionResult OnPostSetMigrationSettings()
    {
        var submittedAssemblyPath = MigrationAssemblyPath?.Trim();
        var submittedSuffix = MigrationProjectSuffix?.Trim();
        var submittedConnectionString = MigrationConnectionString?.Trim();
        LoadFromConfig();
        MigrationAssemblyPath = submittedAssemblyPath;
        MigrationProjectSuffix = string.IsNullOrEmpty(submittedSuffix) ? "Db.Migrations" : submittedSuffix;
        MigrationConnectionString = submittedConnectionString;

        if (!string.IsNullOrEmpty(MigrationAssemblyPath) && !System.IO.File.Exists(MigrationAssemblyPath))
        {
            Message = $"File does not exist: {MigrationAssemblyPath}";
            IsError = true;
            return Page();
        }

        SaveAndReload();
        Message = "Migration settings saved.";
        return Page();
    }

    public IActionResult OnPostSetNugetSource()
    {
        var submittedPath = NugetSourcePath?.Trim();
        LoadFromConfig();
        NugetSourcePath = submittedPath;

        if (!string.IsNullOrEmpty(NugetSourcePath) && !Directory.Exists(NugetSourcePath))
        {
            Message = $"Directory does not exist: {NugetSourcePath}";
            IsError = true;
            return Page();
        }

        SaveAndReload();
        Message = string.IsNullOrEmpty(NugetSourcePath)
            ? "NuGet source path cleared."
            : $"NuGet source path set to: {NugetSourcePath}";
        return Page();
    }

    private void LoadFromConfig()
    {
        var config = _configService.GetConfig();
        TargetFolders = config.TargetFolders;
        BuildConfigurations = config.BuildConfigurations;
        DefaultBuildConfiguration = config.DefaultBuildConfiguration;
        NugetSourcePath = config.NugetSourcePath;
        MigrationAssemblyPath = config.MigrationAssemblyPath;
        MigrationProjectSuffix = config.MigrationProjectSuffix;
        MigrationConnectionString = config.MigrationConnectionString;
        ProjectCount = _discoveryService.ScanFolders(config.EnabledFolderPaths).Count;
    }

    private void SaveAndReload()
    {
        _configService.SaveConfig(new AppConfig
        {
            TargetFolders = TargetFolders,
            BuildConfigurations = BuildConfigurations,
            DefaultBuildConfiguration = DefaultBuildConfiguration,
            NugetSourcePath = NugetSourcePath,
            MigrationAssemblyPath = MigrationAssemblyPath,
            MigrationProjectSuffix = MigrationProjectSuffix,
            MigrationConnectionString = MigrationConnectionString
        });
        ProjectCount = _discoveryService.ScanFolders(
            TargetFolders.Where(f => f.Enabled).Select(f => f.Path).ToList()
        ).Count;
    }
}
