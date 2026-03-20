using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotnetAppManager.Models;

namespace DotnetAppManager.Services;

public class ProcessManagerService
{
    private readonly ConcurrentDictionary<string, ProcessInfo> _processes = new();

    public ProcessInfo? GetProcess(string processId)
    {
        _processes.TryGetValue(processId, out var info);
        return info;
    }

    public List<ProcessInfo> GetProcessesForProject(string projectId)
    {
        return _processes.Values
            .Where(p => p.ProjectId == projectId)
            .OrderByDescending(p => p.StartedAt)
            .ToList();
    }

    public ProcessInfo? GetActiveProcess(string projectId)
    {
        return _processes.Values
            .FirstOrDefault(p => p.ProjectId == projectId && p.Status == ProcessStatus.Running);
    }

    public ProcessInfo StartBuild(string projectId, string projectPath, string? configuration = null)
    {
        var args = string.IsNullOrEmpty(configuration) ? projectPath : $"{projectPath} -c {configuration}";
        return StartProcess(projectId, projectPath, "build", "build", args);
    }

    public ProcessInfo StartRun(string projectId, string projectPath, string? configuration = null)
    {
        var args = string.IsNullOrEmpty(configuration) ? $"--project {projectPath}" : $"--project {projectPath} -c {configuration}";
        return StartProcess(projectId, projectPath, "run", "run", args);
    }

    public void StopProcess(string processId)
    {
        if (!_processes.TryGetValue(processId, out var info))
            return;

        if (info.Status != ProcessStatus.Running)
            return;

        try
        {
            info.Cts.Cancel();
            if (info.SystemProcess is { HasExited: false })
            {
                info.SystemProcess.Kill(entireProcessTree: true);
            }
            info.Status = ProcessStatus.Stopped;
            info.AppendOutput("[Process stopped]");
        }
        catch
        {
            info.Status = ProcessStatus.Stopped;
        }
    }

    public void StopAllProcesses()
    {
        var running = _processes.Values
            .Where(p => p.Status == ProcessStatus.Running)
            .ToList();

        foreach (var info in running)
        {
            StopProcess(info.Id);
        }
    }

    public int GetRunningCount()
    {
        return _processes.Values.Count(p => p.Status == ProcessStatus.Running);
    }

    public async IAsyncEnumerable<string> StreamOutput(string processId, [EnumeratorCancellation] CancellationToken ct)
    {
        if (!_processes.TryGetValue(processId, out var info))
        {
            yield return "[Process not found]";
            yield break;
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await info.OutputSignal.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            while (info.OutputBuffer.TryDequeue(out var line))
            {
                yield return line;
            }

            if (info.Status != ProcessStatus.Running && info.OutputBuffer.IsEmpty)
                yield break;
        }
    }

    public ProcessInfo StartBuildChain(string projectId, List<string> dependencyPaths, string targetPath, string action, string? configuration = null)
    {
        var info = new ProcessInfo
        {
            ProjectId = projectId,
            Command = $"{action} (with deps)"
        };
        _processes[info.Id] = info;

        _ = Task.Run(async () =>
        {
            try
            {
                // Build each dependency sequentially
                foreach (var depPath in dependencyPaths)
                {
                    var depName = Path.GetFileNameWithoutExtension(depPath);
                    info.AppendOutput($"[Building dependency: {depName}]");

                    var args = string.IsNullOrEmpty(configuration) ? depPath : $"{depPath} -c {configuration}";
                    var exitCode = await RunProcessToCompletionAsync(info, "build", args);

                    if (exitCode != 0)
                    {
                        info.Status = ProcessStatus.Failed;
                        info.AppendOutput($"[Dependency build failed: {depName}]");
                        return;
                    }

                    info.AppendOutput($"[Dependency built: {depName}]");
                }

                // Now do the final action
                var targetName = Path.GetFileNameWithoutExtension(targetPath);
                info.AppendOutput($"[{action}: {targetName}]");

                if (action == "build")
                {
                    var args = string.IsNullOrEmpty(configuration) ? targetPath : $"{targetPath} -c {configuration}";
                    var exitCode = await RunProcessToCompletionAsync(info, "build", args);
                    info.Status = exitCode == 0 ? ProcessStatus.Completed : ProcessStatus.Failed;
                }
                else
                {
                    // For run, start the process and leave it running
                    var args = string.IsNullOrEmpty(configuration)
                        ? $"--project {targetPath}"
                        : $"--project {targetPath} -c {configuration}";
                    RunAttachedProcess(info, "run", args, Path.GetDirectoryName(targetPath)!);
                }
            }
            catch (Exception ex)
            {
                info.Status = ProcessStatus.Failed;
                info.AppendOutput($"[Error: {ex.Message}]");
            }
        });

        return info;
    }

    private async Task<int> RunProcessToCompletionAsync(ProcessInfo info, string dotnetCommand, string arguments)
    {
        var tcs = new TaskCompletionSource<int>();

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"{dotnetCommand} {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) info.AppendOutput(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) info.AppendOutput(e.Data);
        };

        process.Exited += (_, _) =>
        {
            tcs.TrySetResult(process.ExitCode);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return await tcs.Task;
    }

    private void RunAttachedProcess(ProcessInfo info, string dotnetCommand, string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"{dotnetCommand} {arguments}",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) info.AppendOutput(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) info.AppendOutput(e.Data);
        };

        process.Exited += (_, _) =>
        {
            if (info.Status == ProcessStatus.Running)
            {
                info.Status = process.ExitCode == 0 ? ProcessStatus.Completed : ProcessStatus.Failed;
            }
            info.AppendOutput($"[Process exited with code {process.ExitCode}]");
        };

        info.SystemProcess = process;

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    private ProcessInfo StartProcess(string projectId, string projectPath, string command, string dotnetCommand, string arguments)
    {
        var info = new ProcessInfo
        {
            ProjectId = projectId,
            Command = command
        };

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"{dotnetCommand} {arguments}",
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            info.AppendOutput(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            info.AppendOutput(e.Data);
        };

        process.Exited += (_, _) =>
        {
            if (info.Status == ProcessStatus.Running)
            {
                info.Status = process.ExitCode == 0 ? ProcessStatus.Completed : ProcessStatus.Failed;
            }
            info.AppendOutput($"[Process exited with code {process.ExitCode}]");
        };

        info.SystemProcess = process;
        _processes[info.Id] = info;

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return info;
    }
}
