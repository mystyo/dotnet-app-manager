using System.Net;
using DotnetAppManager.Models;
using DotnetAppManager.Services;

namespace DotnetAppManager.Endpoints;

public static class SseEndpoints
{
    public static void MapSseEndpoints(WebApplication app)
    {
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
    }

    public static string RenderConsoleFragment(string processId, string initialMessage)
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
