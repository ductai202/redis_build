using System.Threading.Tasks;
using Hyperion.Protocol;

namespace Hyperion.Core;

/// <summary>What kind of work this task represents.</summary>
public enum TaskKind
{
    /// <summary>A RESP command to execute against storage.</summary>
    Command,

    /// <summary>
    /// A snapshot request. The Worker serializes its own Storage shard
    /// into the provided MemoryStream when it processes this task.
    /// Because the Worker is the sole owner of its Storage, no locks are needed.
    /// </summary>
    Snapshot
}

/// <summary>
/// Represents a unit of work dispatched to a Worker thread.
/// </summary>
public class WorkerTask
{
    public TaskKind Kind { get; }
    public RespCommand Command { get; }

    // A TaskCompletionSource allows the worker thread to asynchronously notify
    // the IO thread that the execution is complete and provide the response bytes.
    public TaskCompletionSource<byte[]> ReplyCompletion { get; }

    /// <summary>Creates a command execution task.</summary>
    public WorkerTask(RespCommand command)
    {
        Kind    = TaskKind.Command;
        Command = command;
        ReplyCompletion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Creates a snapshot task (no command).</summary>
    public static WorkerTask CreateSnapshot() => new();

    private WorkerTask()
    {
        Kind    = TaskKind.Snapshot;
        Command = new RespCommand();
        ReplyCompletion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
