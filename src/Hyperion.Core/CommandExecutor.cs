using Hyperion.Core.Commands;
using Hyperion.Protocol;

namespace Hyperion.Core;

/// <summary>
/// Central command dispatcher for single-threaded mode.
/// Routes parsed RESP commands to their handler functions.
/// Go source: executor.go:ExecuteAndResponse
/// 
/// In multi-threaded mode (Phase 6), each Worker has its own executor
/// instance with its own private Storage — this class is the single-thread version.
/// </summary>
public class CommandExecutor : ICommandExecutor
{
    public byte[] Execute(RespCommand command)
    {
        return command.Cmd switch
        {
            "PING" => StringCommands.Ping(command.Args),
            _ => RespEncoder.Encode(new Exception($"ERR unknown command '{command.Cmd}'"))
        };
    }
}
