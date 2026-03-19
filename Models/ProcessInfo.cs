using System.Collections.Concurrent;
using System.Diagnostics;

namespace DotnetAppManager.Models;

public enum ProcessStatus
{
    Running,
    Completed,
    Failed,
    Stopped
}

public class ProcessInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string ProjectId { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public Process? SystemProcess { get; set; }
    public ProcessStatus Status { get; set; } = ProcessStatus.Running;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public ConcurrentQueue<string> OutputBuffer { get; } = new();
    public List<string> OutputHistory { get; } = new();
    private readonly object _historyLock = new();
    public SemaphoreSlim OutputSignal { get; } = new(0);
    public CancellationTokenSource Cts { get; } = new();

    public void AppendOutput(string line)
    {
        lock (_historyLock)
        {
            OutputHistory.Add(line);
        }
        OutputBuffer.Enqueue(line);
        OutputSignal.Release();
    }

    public List<string> GetHistorySnapshot()
    {
        lock (_historyLock)
        {
            return new List<string>(OutputHistory);
        }
    }
}
