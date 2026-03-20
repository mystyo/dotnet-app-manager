using System.Text.Json;
using DotnetAppManager.Models;

namespace DotnetAppManager.Services;

public class ConfigService
{
    private readonly string _configFilePath;
    private readonly string _defaultTargetFolder;
    private readonly Lock _lock = new();

    public ConfigService(IConfiguration configuration, IWebHostEnvironment env)
    {
        _configFilePath = Path.Combine(env.ContentRootPath, "data", "config.json");
        _defaultTargetFolder = configuration["AppDefaults:TargetFolderPath"] ?? "/home";
    }

    public AppConfig GetConfig()
    {
        lock (_lock)
        {
            if (File.Exists(_configFilePath))
            {
                var json = File.ReadAllText(_configFilePath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Support legacy single-path config
                if (root.TryGetProperty("TargetFolderPath", out var singlePath))
                {
                    return new AppConfig { TargetFolders = [new TargetFolder { Path = singlePath.GetString()! }] };
                }

                // Support legacy TargetFolderPaths (list of strings)
                if (root.TryGetProperty("TargetFolderPaths", out var legacyPaths) && legacyPaths.ValueKind == JsonValueKind.Array)
                {
                    var config = new AppConfig();
                    foreach (var item in legacyPaths.EnumerateArray())
                    {
                        config.TargetFolders.Add(new TargetFolder { Path = item.GetString()! });
                    }
                    if (root.TryGetProperty("BuildConfigurations", out var cfgs) && cfgs.ValueKind == JsonValueKind.Array)
                    {
                        config.BuildConfigurations = cfgs.EnumerateArray().Select(e => e.GetString()!).ToList();
                    }
                    if (config.BuildConfigurations.Count == 0)
                        config.BuildConfigurations = ["Debug", "Release"];
                    return config;
                }

                var parsed = JsonSerializer.Deserialize<AppConfig>(json);
                if (parsed != null && parsed.TargetFolders.Count > 0)
                {
                    if (parsed.BuildConfigurations.Count == 0)
                        parsed.BuildConfigurations = ["Debug", "Release"];
                    return parsed;
                }
            }
            return new AppConfig { TargetFolders = [new TargetFolder { Path = _defaultTargetFolder }] };
        }
    }

    public void SaveConfig(AppConfig config)
    {
        lock (_lock)
        {
            var dir = Path.GetDirectoryName(_configFilePath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFilePath, json);
        }
    }
}
