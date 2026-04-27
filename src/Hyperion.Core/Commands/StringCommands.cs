using System.Text;
using Hyperion.Config;
using Hyperion.DataStructures;
using Hyperion.Protocol;

namespace Hyperion.Core.Commands;

public class StringCommands
{
    private readonly Storage _storage;
    public StringCommands(Storage storage) => _storage = storage;

    public byte[] Ping(string[] args)
    {
        if (args.Length > 1) return RespEncoder.Encode(new Exception("ERR wrong number of arguments for 'ping' command"));
        if (args.Length == 0) return RespEncoder.Encode("PONG", isSimpleString: true);
        return RespEncoder.Encode(args[0], isSimpleString: false);
    }

    public byte[] Set(string[] args)
    {
        if (args.Length < 2 || args.Length == 3 || args.Length > 4) return RespEncoder.Encode(new Exception("ERR wrong number of arguments for 'SET' command"));
        string key = args[0];
        string value = args[1];
        long ttlMs = -1;
        if (args.Length > 2)
        {
            if (args[2].ToUpperInvariant() != "EX") return RespEncoder.Encode(new Exception("ERR syntax error"));
            if (!long.TryParse(args[3], out long ttlSec)) return RespEncoder.Encode(new Exception("ERR value is not an integer or out of range"));
            ttlMs = ttlSec * 1000;
        }
        var obj = _storage.DictStore.NewObj(key, value, ttlMs);
        _storage.DictStore.Set(key, obj);
        return Constants.RespOk;
    }

    public byte[] Get(string[] args)
    {
        if (args.Length != 1) return RespEncoder.Encode(new Exception("ERR wrong number of arguments for 'GET' command"));
        string key = args[0];
        var obj = _storage.DictStore.Get(key);
        if (obj == null || _storage.DictStore.HasExpired(key)) return Constants.RespNil;
        return RespEncoder.Encode(obj.Value, isSimpleString: false);
    }

    public byte[] Ttl(string[] args)
    {
        if (args.Length != 1) return RespEncoder.Encode(new Exception("ERR wrong number of arguments for 'TTL' command"));
        string key = args[0];
        var obj = _storage.DictStore.Get(key);
        if (obj == null) return Constants.TtlKeyNotExist;
        var (expiry, isExpirySet) = _storage.DictStore.GetExpiry(key);
        if (!isExpirySet) return Constants.TtlKeyExistNoExpire;
        long remainMs = expiry - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (remainMs < 0)
        {
            _storage.DictStore.Del(key);
            return Constants.TtlKeyNotExist;
        }
        return RespEncoder.Encode(remainMs / 1000, isSimpleString: false);
    }

    public byte[] Del(string[] args)
    {
        if (args.Length == 0) return RespEncoder.Encode(new Exception("ERR wrong number of arguments for 'DEL' command"));
        long count = 0;
        foreach (var key in args)
        {
            if (_storage.DictStore.Del(key)) count++;
        }
        return RespEncoder.Encode(count, isSimpleString: false);
    }

    public byte[] Info(string[] args)
    {
        var sb = new StringBuilder();
        sb.Append("# Keyspace\r\n");
        sb.Append($"db0:keys={Stats.HashKeySpaceStat.Key},expires=0,avg_ttl=0\r\n");
        return RespEncoder.Encode(sb.ToString(), isSimpleString: false);
    }
}
