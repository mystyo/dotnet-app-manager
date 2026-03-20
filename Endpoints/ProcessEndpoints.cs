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

        app.MapPost("/api/process/build-chain", async (HttpContext ctx, ProcessManagerService mgr) =>
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
            var root = doc.RootElement;
            var projectId = root.GetProperty("projectId").GetString()!;
            var projectPath = root.GetProperty("projectPath").GetString()!;
            var action = root.GetProperty("action").GetString()!;
            var configuration = root.TryGetProperty("configuration", out var cfgEl) ? cfgEl.GetString() : null;
            var dependencyPaths = root.GetProperty("dependencyPaths").EnumerateArray()
                .Select(e => e.GetString()!)
                .ToList();

            var existing = mgr.GetActiveProcess(projectId);
            if (existing != null)
                return Results.Content(SseEndpoints.RenderConsoleFragment(existing.Id, "Already running..."), "text/html");

            var info = mgr.StartBuildChain(projectId, dependencyPaths, projectPath, action, configuration);
            return Results.Content(SseEndpoints.RenderConsoleFragment(info.Id, $"Building {dependencyPaths.Count} dep(s) then {action}..."), "text/html");
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
