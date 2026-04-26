using Hyperion.Protocol;

namespace Hyperion.Core;

/// <summary>
/// Contract for executing a parsed RESP command and returning a raw RESP-encoded response.
/// Both single-threaded mode (direct call) and multi-threaded mode (Worker) implement this.
/// </summary>
public interface ICommandExecutor
{
    byte[] Execute(RespCommand command);
}
