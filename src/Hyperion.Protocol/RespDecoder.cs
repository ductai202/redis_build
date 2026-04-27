using System.Buffers;
using System.Text;
using Hyperion.Config;

namespace Hyperion.Protocol;

public static class RespDecoder
{
    public static bool TryReadSimpleString(ref SequenceReader<byte> reader, out string result)
    {
        result = string.Empty;
        if (!reader.TryReadTo(out ReadOnlySequence<byte> line, "\r\n"u8))
            return false;
        
        result = Encoding.UTF8.GetString(line);
        return true;
    }

    public static bool TryReadInt64(ref SequenceReader<byte> reader, out long result)
    {
        result = 0;
        if (!reader.TryReadTo(out ReadOnlySequence<byte> line, "\r\n"u8))
            return false;
        
        long res = 0;
        long sign = 1;
        var span = line.IsSingleSegment ? line.FirstSpan : line.ToArray();
        
        int pos = 0;
        if (span.Length > 0 && span[0] == '-')
        {
            sign = -1;
            pos++;
        }
        else if (span.Length > 0 && span[0] == '+')
        {
            pos++;
        }

        for (; pos < span.Length; pos++)
        {
            res = res * 10 + (span[pos] - '0');
        }

        result = sign * res;
        return true;
    }

    public static bool TryReadError(ref SequenceReader<byte> reader, out string result)
    {
        return TryReadSimpleString(ref reader, out result);
    }

    public static bool TryReadBulkString(ref SequenceReader<byte> reader, out string? result)
    {
        result = null;
        if (!TryReadInt64(ref reader, out long length))
            return false;
        
        if (length == -1)
        {
            return true; // null bulk string
        }

        if (reader.Remaining < length + 2) // length + \r\n
            return false;
        
        var strSeq = reader.Sequence.Slice(reader.Position, length);
        result = Encoding.UTF8.GetString(strSeq);
        
        reader.Advance(length + 2); // advance past string and \r\n
        return true;
    }

    public static bool TryDecodeOne(ref SequenceReader<byte> reader, out object? result)
    {
        result = null;
        if (!reader.TryRead(out byte type))
            return false;

        switch (type)
        {
            case (byte)'+':
                if (TryReadSimpleString(ref reader, out string str))
                {
                    result = str;
                    return true;
                }
                break;
            case (byte)':':
                if (TryReadInt64(ref reader, out long val))
                {
                    result = val;
                    return true;
                }
                break;
            case (byte)'-':
                if (TryReadError(ref reader, out string err))
                {
                    result = new Exception(err);
                    return true;
                }
                break;
            case (byte)'$':
                if (TryReadBulkString(ref reader, out string? bulk))
                {
                    result = bulk;
                    return true;
                }
                break;
            case (byte)'*':
                if (TryReadArray(ref reader, out object[]? arr))
                {
                    result = arr;
                    return true;
                }
                break;
        }
        
        // If we reach here and haven't returned true, rewind the byte we read for 'type'
        reader.Rewind(1);
        return false;
    }

    public static bool TryReadArray(ref SequenceReader<byte> reader, out object[]? result)
    {
        result = null;
        if (!TryReadInt64(ref reader, out long length))
            return false;
        
        if (length == -1)
            return true;

        var list = new object[(int)length];
        for (int i = 0; i < length; i++)
        {
            if (!TryDecodeOne(ref reader, out object? item))
                return false;
            list[i] = item!;
        }
        result = list;
        return true;
    }

    public static void Decode(byte[] input, out object? result, out int bytesConsumed)
    {
        var seq = new ReadOnlySequence<byte>(input);
        var reader = new SequenceReader<byte>(seq);
        TryDecodeOne(ref reader, out result);
        bytesConsumed = (int)reader.Consumed;
    }
}
