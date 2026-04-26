using System.Buffers;

namespace Hyperion.Protocol;

public static class RespParser
{
    public static bool TryParseCommand(ref SequenceReader<byte> reader, out RespCommand? command)
    {
        command = null;
        
        // A command is typically an array of bulk strings
        if (!RespDecoder.TryDecodeOne(ref reader, out object? value))
            return false;

        if (value is object[] array && array.Length > 0 && array[0] is string cmdName)
        {
            var args = new string[array.Length - 1];
            for (int i = 1; i < array.Length; i++)
            {
                args[i - 1] = array[i]?.ToString() ?? string.Empty;
            }

            command = new RespCommand
            {
                Cmd = cmdName.ToUpperInvariant(),
                Args = args
            };
            return true;
        }

        return false;
    }
}
