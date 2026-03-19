using System.Text.Json;
using DotnetAppManager.Models;

namespace DotnetAppManager.Services;

public class ProfileService
{
    private readonly string _filePath;
    private readonly Lock _lock = new();

    public ProfileService(IWebHostEnvironment env)
    {
        _filePath = Path.Combine(env.ContentRootPath, "data", "profiles.json");
    }

    public List<Profile> GetAll()
    {
        lock (_lock)
        {
            if (!File.Exists(_filePath))
                return [];

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<Profile>>(json) ?? [];
        }
    }

    public Profile? Get(string name)
    {
        return GetAll().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public void Save(Profile profile)
    {
        lock (_lock)
        {
            var all = GetAllUnsafe();
            var existing = all.FindIndex(p => p.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
                all[existing] = profile;
            else
                all.Add(profile);
            WriteFile(all);
        }
    }

    public void Delete(string name)
    {
        lock (_lock)
        {
            var all = GetAllUnsafe();
            all.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            WriteFile(all);
        }
    }

    private List<Profile> GetAllUnsafe()
    {
        if (!File.Exists(_filePath))
            return [];

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<List<Profile>>(json) ?? [];
    }

    private void WriteFile(List<Profile> data)
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}
