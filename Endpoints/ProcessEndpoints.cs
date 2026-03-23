using System.Text.Json;
using DotnetAppManager.Models;
using DotnetAppManager.Services;

namespace DotnetAppManager.Endpoints;

public static class ProcessEndpoints
{
    public static void MapProcessEndpoints(WebApplication app)
    {
        app.MapPost("/api/process/build", (string projectId, string projectPath, string? configuration, ProcessManagerService mgr) =>
        {
            var existing = mgr.GetActiveProcess(projectId);
            if (existing != null)
                return Results.Content(SseEndpoints.RenderConsoleFragment(existing.Id, "Build already running..."), "text/html");

            var info = mgr.StartBuild(projectId, projectPath, configuration);
            return Results.Content(SseEndpoints.RenderConsoleFragment(info.Id, "Building..."), "text/html");
        });

        app.MapPost("/api/process/run", (string projectId, string projectPath, string? configuration, ProcessManagerService mgr) =>
        {
            var existing = mgr.GetActiveProcess(projectId);
            if (existing != null)
                return Results.Content(SseEndpoints.RenderConsoleFragment(existing.Id, "Already running..."), "text/html");

            var info = mgr.StartRun(projectId, projectPath, configuration);
            return Results.Content(SseEndpoints.RenderConsoleFragment(info.Id, "Starting..."), "text/html");
        });

        app.MapPost("/api/process/build-chain", async (HttpContext ctx, ProcessManagerService mgr, ProjectDiscoveryService discovery, ConfigService cfg, ChangeDetectionService changeDetection) =>
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
            var root = doc.RootElement;
            var projectId = root.GetProperty("projectId").GetString()!;
            var projectPath = root.GetProperty("projectPath").GetString()!;
            var action = root.GetProperty("action").GetString()!;
            var configuration = root.TryGetProperty("configuration", out var cfgEl) ? cfgEl.GetString() : null;
            var changesOnly = root.TryGetProperty("changesOnly", out var coEl) && coEl.GetBoolean();
            var dependencyPaths = root.GetProperty("dependencyPaths").EnumerateArray()
                .Select(e => e.GetString()!)
                .ToList();

            var existing = mgr.GetActiveProcess(projectId);
            if (existing != null)
                return Results.Content(SseEndpoints.RenderConsoleFragment(existing.Id, "Already running..."), "text/html");

            // Compute waves server-side for parallel execution
            var appConfig = cfg.GetConfig();
            var projectMap = discovery.ScanAllProjectPaths(appConfig.EnabledFolderPaths);
            var depGraph = discovery.BuildDependencyGraph(projectMap);
            var flatNodes = dependencyPaths.Select(p => new DotnetAppManager.Models.DependencyNode
            {
                Name = Path.GetFileNameWithoutExtension(p),
                ProjectPath = p
            }).ToList();

            // Filter to changed dependencies only if requested
            if (changesOnly && !string.IsNullOrEmpty(appConfig.NugetSourcePath))
            {
                flatNodes = changeDetection.GetChangedDependencies(flatNodes, appConfig.NugetSourcePath, depGraph);
                if (flatNodes.Count == 0)
                {
                    // No dependencies changed — just run the final action directly
                    var directInfo = action == "build"
                        ? mgr.StartBuild(projectId, projectPath, configuration)
                        : mgr.StartRun(projectId, projectPath, configuration);
                    return Results.Content(SseEndpoints.RenderConsoleFragment(directInfo.Id, $"No dependency changes detected. {action}..."), "text/html");
                }
            }

            var waves = ProjectDiscoveryService.ComputeBuildWaves(flatNodes, depGraph);

            var info = mgr.StartParallelBuildWaves(projectId, waves, action, projectPath, configuration);
            return Results.Content(SseEndpoints.RenderConsoleFragment(info.Id, $"Building {flatNodes.Count} dep(s) in {waves.Count} wave(s) then {action}..."), "text/html");
        });

        app.MapPost("/api/process/build-all", (string? configuration, string? profile, ProcessManagerService mgr, ConfigService cfg, ProjectDiscoveryService discovery, ProfileService profiles) =>
        {
            var folderPaths = cfg.GetConfig().EnabledFolderPaths;

            // If profile specified, filter to profile's project folders
            IEnumerable<string>? profileFilter = null;
            if (!string.IsNullOrEmpty(profile))
            {
                var profileData = profiles.Get(profile);
                if (profileData != null)
                    profileFilter = profileData.ProjectPaths;
            }

            var waves = discovery.ComputeGlobalBuildWaves(folderPaths);

            // Filter waves to only include profile projects if specified
            if (profileFilter != null)
            {
                var profilePaths = new HashSet<string>(profileFilter, StringComparer.OrdinalIgnoreCase);
                waves = waves
                    .Select(wave => wave.Where(n => profilePaths.Contains(n.ProjectPath!)).ToList())
                    .Where(wave => wave.Count > 0)
                    .ToList();
            }

            if (waves.Count == 0)
                return Results.Content("<div class='alert alert-info'>No projects to build.</div>", "text/html");

            var info = mgr.StartParallelBuildWaves("build-all", waves, null, null, configuration);
            return Results.Content(SseEndpoints.RenderConsoleFragment(info.Id, $"Building all projects in {waves.Count} wave(s)..."), "text/html");
        });

        app.MapPost("/api/process/run-all", (string? configuration, string? profile, ProcessManagerService mgr, ConfigService cfg, ProjectDiscoveryService discovery, ProjectPreferencesService prefs, ProfileService profiles) =>
        {
            var allPrefs = prefs.GetAll();
            var filtered = discovery.ScanFolders(cfg.GetConfig().EnabledFolderPaths)
                .Where(p => !(allPrefs.GetValueOrDefault(p.FullPath)?.Ignored ?? false));

            if (!string.IsNullOrEmpty(profile))
            {
                var profileData = profiles.Get(profile);
                if (profileData != null)
                {
                    var profilePaths = new HashSet<string>(profileData.ProjectPaths, StringComparer.OrdinalIgnoreCase);
                    filtered = filtered.Where(p => profilePaths.Contains(p.FullPath));
                }
            }

            var ordered = filtered
                .OrderBy(p => allPrefs.GetValueOrDefault(p.FullPath)?.StartOrder ?? 100)
                .ThenBy(p => p.Name);

            foreach (var project in ordered)
            {
                if (mgr.GetActiveProcess(project.Id) == null)
                {
                    mgr.StartRun(project.Id, project.FullPath, configuration);
                }
            }

            var redirect = string.IsNullOrEmpty(profile) ? "/" : $"/?profile={Uri.EscapeDataString(profile)}";
            return Results.Redirect(redirect);
        });

        app.MapPost("/api/process/stop/{processId}", (string processId, ProcessManagerService mgr) =>
        {
            mgr.StopProcess(processId);
            return Results.Content("<span class=\"badge bg-warning\">stopped</span>", "text/html");
        });

        app.MapPost("/api/process/stop-all", (ProcessManagerService mgr) =>
        {
            mgr.StopAllProcesses();
            return Results.Redirect("/");
        });

        app.MapGet("/api/process/running-count", (ProcessManagerService mgr) =>
        {
            var count = mgr.GetRunningCount();
            return Results.Content(count.ToString(), "text/plain");
        });

        app.MapGet("/api/process/status/{projectId}", (string projectId, ProcessManagerService mgr) =>
        {
            var active = mgr.GetActiveProcess(projectId);
            if (active != null)
                return Results.Content("<span class=\"badge bg-info\">running</span>", "text/html");

            var processes = mgr.GetProcessesForProject(projectId);
            if (processes.Count == 0)
                return Results.Content("<span class=\"badge bg-secondary\">idle</span>", "text/html");

            var last = processes[0];
            var (badgeClass, label) = last.Status switch
            {
                ProcessStatus.Completed => ("bg-success", "completed"),
                ProcessStatus.Failed => ("bg-danger", "failed"),
                ProcessStatus.Stopped => ("bg-warning", "stopped"),
                _ => ("bg-secondary", "idle")
            };
            return Results.Content($"<span class=\"badge {badgeClass}\">{label}</span>", "text/html");
        });

        app.MapPost("/api/process/migrate", (string projectId, string assemblyName, ProcessManagerService mgr, ConfigService cfg) =>
        {
            var config = cfg.GetConfig();
            if (string.IsNullOrEmpty(config.MigrationAssemblyPath))
                return Results.Content("<div class='alert alert-warning'>Migration assembly path not configured.</div>", "text/html");
            if (string.IsNullOrEmpty(config.MigrationConnectionString))
                return Results.Content("<div class='alert alert-warning'>Migration connection string not configured.</div>", "text/html");
            if (string.IsNullOrEmpty(config.NugetSourcePath))
                return Results.Content("<div class='alert alert-warning'>NuGet source path not configured.</div>", "text/html");

            var existing = mgr.GetActiveProcess(projectId);
            if (existing != null)
                return Results.Content(SseEndpoints.RenderConsoleFragment(existing.Id, "Already running..."), "text/html");

            var info = mgr.StartMigration(projectId, config.MigrationAssemblyPath, assemblyName, config.MigrationConnectionString, config.NugetSourcePath);
            return Results.Content(SseEndpoints.RenderConsoleFragment(info.Id, $"Running migration: {assemblyName}..."), "text/html");
        });

        app.MapPost("/api/project-preferences", async (HttpContext ctx, ProjectPreferencesService prefs) =>
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
            var root = doc.RootElement;
            var projectPath = root.GetProperty("projectPath").GetString()!;
            var ignored = root.GetProperty("ignored").GetBoolean();
            var startOrder = root.GetProperty("startOrder").GetInt32();
            prefs.Save(projectPath, new ProjectPreference { Ignored = ignored, StartOrder = startOrder });
            return Results.Ok();
        });
    }
}
