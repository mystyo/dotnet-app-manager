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

    public ProcessInfo StartMigration(string projectId, string projectPath, string assemblyPath, string assemblyName, string connectionString, string nugetSourcePath, string? configuration = null, List<List<DependencyNode>>? dependencyWaves = null)
    {
        var info = new ProcessInfo
        {
            ProjectId = projectId,
            Command = $"migrate {assemblyName}"
        };
        _processes[info.Id] = info;

        var baseArgs = $"up -p LocalDev -c \"{connectionString}\" -n \"{assemblyName}\" -ns \"{nugetSourcePath}\"";
        var preDeployArgs = $"{baseArgs} -hk PreDeployment";
        var postDeployArgs = $"{baseArgs} -hk PostDeployment";

        _ = Task.Run(async () =>
        {
            try
            {
                // Build changed dependencies in parallel waves
                if (dependencyWaves != null && dependencyWaves.Count > 0)
                {
                    for (var i = 0; i < dependencyWaves.Count; i++)
                    {
                        var wave = dependencyWaves[i];
                        var names = string.Join(", ", wave.Select(n => n.Name));
                        info.AppendOutput($"[Wave {i + 1}/{dependencyWaves.Count}: building {names} in parallel]");

                        var tasks = wave.Select(node => Task.Run(async () =>
                        {
                            var args = string.IsNullOrEmpty(configuration) ? node.ProjectPath! : $"{node.ProjectPath!} -c {configuration}";
                            var code = await RunProcessToCompletionAsync(info, "build", args, node.Name);
                            return (node.Name, code);
                        })).ToList();

                        var results = await Task.WhenAll(tasks);

                        foreach (var (name, code) in results)
                        {
                            if (code != 0)
                            {
                                info.Status = ProcessStatus.Failed;
                                info.AppendOutput($"[Wave {i + 1} FAILED: {name} exited with code {code}]");
                                return;
                            }
                        }

                        info.AppendOutput($"[Wave {i + 1} completed]");
                    }
                }

                // Build the migration project itself
                var buildArgs = string.IsNullOrEmpty(configuration) ? projectPath : $"{projectPath} -c {configuration}";
                info.AppendOutput($"[Building migration project: {assemblyName}]");
                info.AppendOutput($"[Command: dotnet build {buildArgs}]");
                var buildExitCode = await RunProcessToCompletionAsync(info, "build", buildArgs);
                if (buildExitCode != 0)
                {
                    info.Status = ProcessStatus.Failed;
                    info.AppendOutput($"[Build failed with exit code {buildExitCode}]");
                    return;
                }
                info.AppendOutput("[Build completed]");

                // Run PreDeployment migration
                info.AppendOutput($"[Running PreDeployment migration: {assemblyName}]");
                info.AppendOutput($"[Command: dotnet {assemblyPath} {preDeployArgs}]");
                var preExitCode = await RunProcessToCompletionAsync(info, assemblyPath, preDeployArgs);
                if (preExitCode != 0)
                {
                    info.Status = ProcessStatus.Failed;
                    info.AppendOutput($"[PreDeployment migration failed with exit code {preExitCode}]");
                    return;
                }
                info.AppendOutput("[PreDeployment migration completed]");

                // Run PostDeployment migration
                info.AppendOutput($"[Running PostDeployment migration: {assemblyName}]");
                info.AppendOutput($"[Command: dotnet {assemblyPath} {postDeployArgs}]");
                var postExitCode = await RunProcessToCompletionAsync(info, assemblyPath, postDeployArgs);
                info.Status = postExitCode == 0 ? ProcessStatus.Completed : ProcessStatus.Failed;
                info.AppendOutput(postExitCode == 0 ? "[PostDeployment migration completed]" : $"[PostDeployment migration failed with exit code {postExitCode}]");
            }
            catch (Exception ex)
            {
                info.Status = ProcessStatus.Failed;
                info.AppendOutput($"[Error: {ex.Message}]");
            }
        });

        return info;
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
        // Convert flat dep list into waves for parallel execution
        var waves = BuildWavesFromFlatList(dependencyPaths);
        return StartParallelBuildWaves(projectId, waves, action, targetPath, configuration);
    }

    public ProcessInfo StartParallelBuildWaves(
        string projectId,
        List<List<DependencyNode>> waves,
        string? finalAction,
        string? finalProjectPath,
        string? configuration)
    {
        var info = new ProcessInfo
        {
            ProjectId = projectId,
            Command = finalAction != null ? $"{finalAction} (parallel deps)" : "build-all"
        };
        _processes[info.Id] = info;

        _ = Task.Run(async () =>
        {
            try
            {
                for (var i = 0; i < waves.Count; i++)
                {
                    var wave = waves[i];
                    var names = string.Join(", ", wave.Select(n => n.Name));
                    info.AppendOutput($"[Wave {i + 1}/{waves.Count}: building {names} in parallel]");

                    var tasks = wave.Select(node => Task.Run(async () =>
                    {
                        var args = string.IsNullOrEmpty(configuration) ? node.ProjectPath! : $"{node.ProjectPath!} -c {configuration}";
                        var exitCode = await RunProcessToCompletionAsync(info, "build", args, node.Name);
                        return (node.Name, exitCode);
                    })).ToList();

                    var results = await Task.WhenAll(tasks);

                    foreach (var (name, exitCode) in results)
                    {
                        if (exitCode != 0)
                        {
                            info.Status = ProcessStatus.Failed;
                            info.AppendOutput($"[Wave {i + 1} FAILED: {name} exited with code {exitCode}]");
                            return;
                        }
                    }

                    info.AppendOutput($"[Wave {i + 1} completed]");
                }

                // Execute final action if specified
                if (finalAction != null && finalProjectPath != null)
                {
                    var targetName = Path.GetFileNameWithoutExtension(finalProjectPath);
                    info.AppendOutput($"[{finalAction}: {targetName}]");

                    if (finalAction == "build")
                    {
                        var args = string.IsNullOrEmpty(configuration) ? finalProjectPath : $"{finalProjectPath} -c {configuration}";
                        var exitCode = await RunProcessToCompletionAsync(info, "build", args, targetName);
                        info.Status = exitCode == 0 ? ProcessStatus.Completed : ProcessStatus.Failed;
                    }
                    else
                    {
                        var args = string.IsNullOrEmpty(configuration)
                            ? $"--project {finalProjectPath}"
                            : $"--project {finalProjectPath} -c {configuration}";
                        RunAttachedProcess(info, "run", args, Path.GetDirectoryName(finalProjectPath)!);
                    }
                }
                else
                {
                    info.Status = ProcessStatus.Completed;
                    info.AppendOutput("[All waves completed]");
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

    private static List<List<DependencyNode>> BuildWavesFromFlatList(List<string> dependencyPaths)
    {
        // Simple grouping: since we receive a flat ordered list without graph info,
        // put each dep in its own wave (preserves sequential semantics).
        // The endpoint will compute proper waves when it has access to the discovery service.
        return dependencyPaths.Select(path => new List<DependencyNode>
        {
            new() { Name = Path.GetFileNameWithoutExtension(path), ProjectPath = path }
        }).ToList();
    }

    private Task<int> RunProcessToCompletionAsync(ProcessInfo info, string dotnetCommand, string arguments)
    {
        return RunProcessToCompletionAsync(info, dotnetCommand, arguments, null);
    }

    private async Task<int> RunProcessToCompletionAsync(ProcessInfo info, string dotnetCommand, string arguments, string? outputPrefix)
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
            if (e.Data != null)
                info.AppendOutput(outputPrefix != null ? $"[{outputPrefix}] {e.Data}" : e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                info.AppendOutput(outputPrefix != null ? $"[{outputPrefix}] {e.Data}" : e.Data);
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
