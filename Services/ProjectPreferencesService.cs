using System.Text.Json;
using DotnetAppManager.Models;

namespace DotnetAppManager.Services;

public class ProjectPreferencesService
{
    private readonly string _filePath;
    private readonly Lock _lock = new();

    public ProjectPreferencesService(IWebHostEnvironment env)
    {
        _filePath = Path.Combine(env.ContentRootPath, "data", "project-preferences.json");
    }

    public Dictionary<string, ProjectPreference> GetAll()
    {
        lock (_lock)
        {
            if (!File.Exists(_filePath))
                return new();

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<Dictionary<string, ProjectPreference>>(json) ?? new();
        }
    }

    public ProjectPreference Get(string projectPath)
    {
        var all = GetAll();
        return all.GetValueOrDefault(projectPath) ?? new ProjectPreference();
    }

    public void Save(string projectPath, ProjectPreference pref)
    {
        lock (_lock)
        {
            var all = GetAllUnsafe();

            // Only store non-default values; remove entry if everything is default
            if (!pref.Ignored && pref.StartOrder == 100)
            {
                all.Remove(projectPath);
            }
            else
            {
                all[projectPath] = pref;
            }

            WriteFile(all);
        }
    }

    private Dictionary<string, ProjectPreference> GetAllUnsafe()
    {
        if (!File.Exists(_filePath))
            return new();

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<Dictionary<string, ProjectPreference>>(json) ?? new();
    }

    private void WriteFile(Dictionary<string, ProjectPreference> data)
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}
