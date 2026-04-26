using System.Text;
using Hyperion.Config;

namespace Hyperion.Protocol;

public static class RespEncoder
{
    public const string CRLF = "\r\n";

    public static byte[] Encode(object? value, bool isSimpleString = false)
    {
        if (value == null) return Constants.RespNil;

        switch (value)
        {
            case string s:
                if (isSimpleString)
                {
                    return Encoding.UTF8.GetBytes($"+{s}{CRLF}");
                }
                return Encoding.UTF8.GetBytes($"${s.Length}{CRLF}{s}{CRLF}");
            
            case int i:
                return Encoding.UTF8.GetBytes($":{i}{CRLF}");
            case long l:
                return Encoding.UTF8.GetBytes($":{l}{CRLF}");
            case short sh:
                return Encoding.UTF8.GetBytes($":{sh}{CRLF}");
            case byte b:
                return Encoding.UTF8.GetBytes($":{b}{CRLF}");
            
            case Exception ex:
                return Encoding.UTF8.GetBytes($"-{ex.Message}{CRLF}");
            
            case string[] sa:
                return EncodeStringArray(sa);
            
            case string[][] ssa:
                return Encode2DStringArray(ssa);
            
            case object[] oa:
                return EncodeObjectArray(oa);
            
            default:
                return Constants.RespNil;
        }
    }

    private static byte[] EncodeStringArray(string[] sa)
    {
        var sb = new StringBuilder();
        sb.Append($"*{sa.Length}{CRLF}");
        foreach (var s in sa)
        {
            sb.Append($"${s.Length}{CRLF}{s}{CRLF}");
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] Encode2DStringArray(string[][] ssa)
    {
        var sb = new StringBuilder();
        sb.Append($"*{ssa.Length}{CRLF}");
        foreach (var sa in ssa)
        {
            sb.Append($"*{sa.Length}{CRLF}");
            foreach (var s in sa)
            {
                sb.Append($"${s.Length}{CRLF}{s}{CRLF}");
            }
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] EncodeObjectArray(object[] oa)
    {
        using var ms = new MemoryStream();
        var header = Encoding.UTF8.GetBytes($"*{oa.Length}{CRLF}");
        ms.Write(header);
        foreach (var obj in oa)
        {
            var encoded = Encode(obj, false);
            ms.Write(encoded);
        }
        return ms.ToArray();
    }
}
