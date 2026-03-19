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
                    return new AppConfig { TargetFolderPaths = [singlePath.GetString()!] };
                }

                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config != null && config.TargetFolderPaths.Count > 0)
                {
                    if (config.BuildConfigurations.Count == 0)
                        config.BuildConfigurations = ["Debug", "Release"];
                    return config;
                }
            }
            return new AppConfig { TargetFolderPaths = [_defaultTargetFolder] };
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
