using System.Net;
using System.Text.Json;
using DotnetAppManager.Models;
using DotnetAppManager.Services;

namespace DotnetAppManager.Endpoints;

public static class SseEndpoints
{
    public static void MapSseEndpoints(WebApplication app)
    {
        app.MapPost("/api/process/build", (string projectId, string projectPath, string? configuration, ProcessManagerService mgr) =>
        {
            var existing = mgr.GetActiveProcess(projectId);
            if (existing != null)
                return Results.Content(RenderConsoleFragment(existing.Id, "Build already running..."), "text/html");

            var info = mgr.StartBuild(projectId, projectPath, configuration);
            return Results.Content(RenderConsoleFragment(info.Id, "Building..."), "text/html");
        });

        app.MapPost("/api/process/run", (string projectId, string projectPath, string? configuration, ProcessManagerService mgr) =>
        {
            var existing = mgr.GetActiveProcess(projectId);
            if (existing != null)
                return Results.Content(RenderConsoleFragment(existing.Id, "Already running..."), "text/html");

            var info = mgr.StartRun(projectId, projectPath, configuration);
            return Results.Content(RenderConsoleFragment(info.Id, "Starting..."), "text/html");
        });

        app.MapGet("/api/process/stream/{processId}", async (string processId, ProcessManagerService mgr, HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            var info = mgr.GetProcess(processId);
            if (info == null)
            {
                await ctx.Response.WriteAsync("event: message\ndata: <div class=\"text-danger\">[Process not found]</div>\n\n");
                await ctx.Response.Body.FlushAsync();
                return;
            }

            // Replay historical output first
            foreach (var histLine in info.GetHistorySnapshot())
            {
                var histEncoded = WebUtility.HtmlEncode(histLine);
                var histCssClass = histLine.Contains("error", StringComparison.OrdinalIgnoreCase) ? "text-danger" : "";
                await ctx.Response.WriteAsync($"event: message\ndata: <div class=\"console-line {histCssClass}\">{histEncoded}</div>\n\n");
            }
            await ctx.Response.Body.FlushAsync();

            // Drain any buffered lines that arrived during history replay
            while (info.OutputBuffer.TryDequeue(out _)) { }

            // Stream live output
            if (info.Status == ProcessStatus.Running)
            {
                await foreach (var line in mgr.StreamOutput(processId, ctx.RequestAborted))
                {
                    var encoded = WebUtility.HtmlEncode(line);
                    var cssClass = line.Contains("error", StringComparison.OrdinalIgnoreCase) ? "text-danger" : "";
                    await ctx.Response.WriteAsync($"event: message\ndata: <div class=\"console-line {cssClass}\">{encoded}</div>\n\n");
                    await ctx.Response.Body.FlushAsync();
                }
            }

            // Send final status event
            var finalStatus = info.Status.ToString().ToLowerInvariant();
            var badgeClass = info.Status == ProcessStatus.Completed ? "bg-success" : "bg-danger";
            await ctx.Response.WriteAsync($"event: status\ndata: <span class=\"badge {badgeClass}\">{finalStatus}</span>\n\n");
            await ctx.Response.Body.FlushAsync();
        });

        app.MapPost("/api/process/run-all", (string? configuration, string? profile, ProcessManagerService mgr, ConfigService cfg, ProjectDiscoveryService discovery, ProjectPreferencesService prefs, ProfileService profiles) =>
        {
            var allPrefs = prefs.GetAll();
            var filtered = discovery.ScanFolders(cfg.GetConfig().TargetFolderPaths)
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

    private static string RenderConsoleFragment(string processId, string initialMessage)
    {
        return $"""
            <div class="console-wrapper mt-2" hx-ext="sse" sse-connect="/api/process/stream/{processId}">
                <div class="d-flex justify-content-between align-items-center mb-1">
                    <small class="text-muted">{initialMessage}</small>
                    <button class="btn btn-sm btn-outline-danger"
                            hx-post="/api/process/stop/{processId}"
                            hx-target="closest .console-wrapper"
                            hx-swap="afterbegin">
                        Stop
                    </button>
                </div>
                <pre class="console-output" sse-swap="message" hx-swap="beforeend"></pre>
            </div>
            """;
    }
}
