using System.Threading.Tasks;
using Hyperion.Protocol;

namespace Hyperion.Core;

/// <summary>
/// Represents a unit of work (a command) to be executed by a Worker.
/// </summary>
public class WorkerTask
{
    public RespCommand Command { get; }
    
    // A TaskCompletionSource allows the worker thread to asynchronously notify
    // the IO thread that the execution is complete and provide the response bytes.
    public TaskCompletionSource<byte[]> ReplyCompletion { get; }

    public WorkerTask(RespCommand command)
    {
        Command = command;
        ReplyCompletion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
