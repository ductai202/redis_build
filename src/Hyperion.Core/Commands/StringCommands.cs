using Hyperion.Config;
using Hyperion.Protocol;

namespace Hyperion.Core.Commands;

/// <summary>
/// Handlers for string/scalar commands: PING, SET, GET, DEL, TTL, EXPIRE, INFO.
/// More handlers are added in Phase 4 when Dict is available.
/// </summary>
public static class StringCommands
{
    /// <summary>
    /// PING [message]
    /// Returns PONG or echoes the message back as a bulk string.
    /// Go source: executor.go:cmdPING / worker.go:cmdPING
    /// </summary>
    public static byte[] Ping(string[] args)
    {
        if (args.Length > 1)
            return RespEncoder.Encode(new Exception("ERR wrong number of arguments for 'ping' command"));

        if (args.Length == 0)
            return RespEncoder.Encode("PONG", isSimpleString: true);

        // Single arg: echo it back as a bulk string
        return RespEncoder.Encode(args[0], isSimpleString: false);
    }
}
