using Hyperion.Core.Commands;
using Hyperion.Protocol;

namespace Hyperion.Core;

public class CommandExecutor : ICommandExecutor
{
    private readonly Storage _storage;
    private readonly StringCommands _stringCommands;
    private readonly SetCommands _setCommands;
    private readonly ZSetCommands _zsetCommands;
    private readonly BloomCommands _bloomCommands;
    private readonly CmsCommands _cmsCommands;
    private readonly HashCommands _hashCommands;
    private readonly ListCommands _listCommands;

    /// <summary>
    /// Optional callback invoked after every write command.
    /// The server wires this to SnapshotCoordinator.NotifyWrite() so the
    /// periodic-save policy can track changes without coupling Core to Persistence.
    /// </summary>
    public Action? OnWriteCommand { get; set; }

    /// <summary>
    /// Optional callback invoked when SAVE is requested.
    /// Returns true on success.
    /// </summary>
    public Func<bool>? OnSave { get; set; }

    /// <summary>
    /// Optional callback invoked when BGSAVE is requested.
    /// Returns a Task that completes when the background save finishes.
    /// </summary>
    public Func<Task<bool>>? OnBgSave { get; set; }

    /// <summary>Returns the Unix timestamp of the last successful save.</summary>
    public Func<long>? GetLastSaveTime { get; set; }

    public CommandExecutor()
    {
        _storage = new Storage();
        _stringCommands = new StringCommands(_storage);
        _setCommands = new SetCommands(_storage);
        _zsetCommands = new ZSetCommands(_storage);
        _bloomCommands = new BloomCommands(_storage);
        _cmsCommands = new CmsCommands(_storage);
        _hashCommands = new HashCommands(_storage);
        _listCommands = new ListCommands(_storage);
    }

    public CommandExecutor(Storage storage)
    {
        _storage = storage;
        _stringCommands = new StringCommands(_storage);
        _setCommands = new SetCommands(_storage);
        _zsetCommands = new ZSetCommands(_storage);
        _bloomCommands = new BloomCommands(_storage);
        _cmsCommands = new CmsCommands(_storage);
        _hashCommands = new HashCommands(_storage);
        _listCommands = new ListCommands(_storage);
    }

    public int DelayUs { get; set; } = 0;

    // Commands that mutate storage — used to fire OnWriteCommand
    private static readonly HashSet<string> WriteCmds =
    [
        "SET", "DEL", "INCR", "DECR",
        "HSET", "HDEL",
        "LPUSH", "RPUSH", "LPOP", "RPOP",
        "SADD", "SREM",
        "ZADD", "ZREM",
        "BF.RESERVE", "BF.MADD",
        "CMS.INITBYDIM", "CMS.INITBYPROB", "CMS.INCRBY"
    ];

    public byte[] Execute(RespCommand command)
    {
        if (DelayUs > 0)
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMicroseconds(DelayUs));
        }

        var result = command.Cmd switch
        {
            "PING"     => _stringCommands.Ping(command.Args),
            "SET"      => _stringCommands.Set(command.Args),
            "GET"      => _stringCommands.Get(command.Args),
            "TTL"      => _stringCommands.Ttl(command.Args),
            "DEL"      => _stringCommands.Del(command.Args),
            "INFO"     => _stringCommands.Info(command.Args),
            "INCR"     => _stringCommands.Incr(command.Args),
            "DECR"     => _stringCommands.Decr(command.Args),
            "SADD"     => _setCommands.Sadd(command.Args),
            "SMEMBERS" => _setCommands.Smembers(command.Args),
            "SISMEMBER"=> _setCommands.Sismember(command.Args),
            "SREM"     => _setCommands.Srem(command.Args),
            "ZADD"     => _zsetCommands.Zadd(command.Args),
            "ZREM"     => _zsetCommands.Zrem(command.Args),
            "ZSCORE"   => _zsetCommands.Zscore(command.Args),
            "ZRANK"    => _zsetCommands.Zrank(command.Args),
            "ZRANGE"   => _zsetCommands.Zrange(command.Args),
            "HSET"     => _hashCommands.HSet(command.Args),
            "HGET"     => _hashCommands.HGet(command.Args),
            "HDEL"     => _hashCommands.HDel(command.Args),
            "HGETALL"  => _hashCommands.HGetAll(command.Args),
            "LPUSH"    => _listCommands.LPush(command.Args),
            "RPUSH"    => _listCommands.RPush(command.Args),
            "LPOP"     => _listCommands.LPop(command.Args),
            "RPOP"     => _listCommands.RPop(command.Args),
            "LRANGE"   => _listCommands.LRange(command.Args),
            "BF.RESERVE"    => _bloomCommands.BfReserve(command.Args),
            "BF.MADD"       => _bloomCommands.BfMadd(command.Args),
            "BF.EXISTS"     => _bloomCommands.BfExists(command.Args),
            "CMS.INITBYDIM" => _cmsCommands.CmsInitByDim(command.Args),
            "CMS.INITBYPROB"=> _cmsCommands.CmsInitByProb(command.Args),
            "CMS.INCRBY"    => _cmsCommands.CmsIncrBy(command.Args),
            "CMS.QUERY"     => _cmsCommands.CmsQuery(command.Args),

            // --- Persistence commands ---
            "SAVE"      => ExecuteSave(),
            "BGSAVE"    => ExecuteBgSave(),
            "LASTSAVE"  => ExecuteLastSave(),
            "DBSIZE"    => ExecuteDbSize(),

            _ => RespEncoder.Encode(new Exception($"ERR unknown command '{command.Cmd}'"))
        };

        // Notify coordinator of write so the save policy can track changes
        if (WriteCmds.Contains(command.Cmd))
            OnWriteCommand?.Invoke();

        return result;
    }

    private byte[] ExecuteSave()
    {
        if (OnSave == null)
            return RespEncoder.Encode(new Exception("ERR persistence is not configured"));

        bool ok = OnSave();
        return ok ? Config.Constants.RespOk : RespEncoder.Encode(new Exception("ERR RDB save failed"));
    }

    private byte[] ExecuteBgSave()
    {
        if (OnBgSave == null)
            return RespEncoder.Encode(new Exception("ERR persistence is not configured"));

        // Fire-and-forget; the background task runs independently
        _ = OnBgSave();
        return RespEncoder.Encode("Background saving started", isSimpleString: true);
    }

    private byte[] ExecuteLastSave()
    {
        long ts = GetLastSaveTime?.Invoke() ?? 0;
        return RespEncoder.Encode(ts, isSimpleString: false);
    }

    private byte[] ExecuteDbSize()
    {
        long count = DataStructures.Stats.HashKeySpaceStat.Key;
        return RespEncoder.Encode(count, isSimpleString: false);
    }

    public void RunActiveExpiry()
    {
        var activeExpiry = new ActiveExpiry(_storage);
        activeExpiry.DeleteExpiredKeys();
    }
}
