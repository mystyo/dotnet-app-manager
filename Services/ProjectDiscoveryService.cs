using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DotnetAppManager.Models;

namespace DotnetAppManager.Services;

public class ProjectDiscoveryService
{
    private static readonly HashSet<string> KnownProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "TargetFramework", "TargetFrameworks", "OutputType", "RootNamespace", "AssemblyName"
    };

    public List<DiscoveredProject> ScanFolders(IEnumerable<string> folderPaths)
    {
        return folderPaths
            .SelectMany(ScanFolder)
            .GroupBy(p => p.Id)
            .Select(g => g.First())
            .OrderBy(p => p.Name)
            .ToList();
    }

    private List<DiscoveredProject> ScanFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return [];

        var projects = new List<DiscoveredProject>();

        var projectFiles = Directory.EnumerateFiles(folderPath, "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(folderPath, "*.fsproj", SearchOption.AllDirectories));

        foreach (var file in projectFiles)
        {
            try
            {
                var projectDir = Path.GetDirectoryName(file)!;
                var launchSettings = Path.Combine(projectDir, "Properties", "launchSettings.json");
                if (!File.Exists(launchSettings))
                    continue;

                var project = ParseProjectFile(file);
                projects.Add(project);
            }
            catch
            {
                // Skip unparseable project files
            }
        }

        return projects.OrderBy(p => p.Name).ToList();
    }

    private static DiscoveredProject ParseProjectFile(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath)!;
        var doc = XDocument.Load(filePath);

        var project = new DiscoveredProject
        {
            Id = GenerateId(filePath),
            FullPath = filePath,
            Directory = dir,
            ProjectFileType = Path.GetExtension(filePath).TrimStart('.'),
            Name = Path.GetFileNameWithoutExtension(filePath)
        };

        // Parse PropertyGroup elements
        var properties = doc.Descendants()
            .Where(e => e.Parent?.Name.LocalName == "PropertyGroup" && !e.HasElements)
            .GroupBy(e => e.Name.LocalName)
            .ToDictionary(g => g.Key, g => g.First().Value);

        project.TargetFramework = properties.GetValueOrDefault("TargetFramework")
            ?? properties.GetValueOrDefault("TargetFrameworks");
        project.OutputType = properties.GetValueOrDefault("OutputType");
        project.RootNamespace = properties.GetValueOrDefault("RootNamespace");
        project.AssemblyName = properties.GetValueOrDefault("AssemblyName");

        if (project.AssemblyName != null)
            project.Name = project.AssemblyName;

        foreach (var (key, value) in properties)
        {
            if (!KnownProperties.Contains(key) && !string.IsNullOrWhiteSpace(value))
                project.AdditionalProperties[key] = value;
        }

        // Parse project references (local projects only, not NuGet packages)
        var projectRefs = doc.Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => v != null)
            .Select(v => Path.GetFileNameWithoutExtension(v!))
            .OrderBy(n => n)
            .ToList();
        project.ProjectReferences = projectRefs;

        // Parse custom <Dependency> elements
        var dependencies = doc.Descendants()
            .Where(e => e.Name.LocalName == "Dependency")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => v != null)
            .Select(v => v!)
            .OrderBy(n => n)
            .ToList();
        project.Dependencies = dependencies;

        // Parse appsettings files
        var appSettingsFiles = Directory.EnumerateFiles(dir, "appsettings*.json");
        foreach (var settingsFile in appSettingsFiles)
        {
            try
            {
                var fileName = Path.GetFileName(settingsFile);
                var json = File.ReadAllText(settingsFile);
                using var jsonDoc = JsonDocument.Parse(json);
                var flattened = new Dictionary<string, string>();
                FlattenJson(jsonDoc.RootElement, "", flattened);
                project.AppSettings[fileName] = flattened;
            }
            catch
            {
                // Skip unparseable settings files
            }
        }

        return project;
    }

    /// <summary>
    /// Scans all .csproj/.fsproj names under the parent directories of the given target paths.
    /// This allows cross-solution resolution (e.g. merkle.loyalty can find merkle.platform projects).
    /// </summary>
    public HashSet<string> ScanAllProjectNames(IEnumerable<string> targetPaths)
    {
        var parentDirs = targetPaths
            .Select(p => Path.GetDirectoryName(p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
            .Where(p => p != null && Directory.Exists(p))
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var parent in parentDirs)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(parent, "*.csproj", SearchOption.AllDirectories)
                    .Concat(Directory.EnumerateFiles(parent, "*.fsproj", SearchOption.AllDirectories)))
                {
                    names.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
            catch
            {
                // Skip inaccessible directories
            }
        }

        return names;
    }

    private static void FlattenJson(JsonElement element, string prefix, Dictionary<string, string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}:{prop.Name}";
                    FlattenJson(prop.Value, key, result);
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    FlattenJson(item, $"{prefix}:{index}", result);
                    index++;
                }
                break;
            default:
                result[prefix] = element.ToString();
                break;
        }
    }

    private static string GenerateId(string path)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(path));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}
