using System.Text;
using Hyperion.Core;
using Hyperion.DataStructures;

namespace Hyperion.Persistence;

/// <summary>
/// Reads an RDB file created by RdbWriter and populates Storage instances.
///
/// PARSING STRATEGY — STATE MACHINE:
/// The reader processes the file byte by byte using a simple state machine.
/// It reads one byte (the opcode), then switches to the correct handler for
/// that opcode to consume the following bytes. This mirrors how Redis's own
/// rdb.c parser works.
///
/// ON LOAD (multi-thread mode):
/// All keys are loaded into a single temporary Storage, then re-partitioned
/// across Worker shards using the same FNV-1a hash that the router uses.
/// This ensures that after restart, every key lands on the same Worker it
/// would have been routed to for a fresh SET command.
/// </summary>
public sealed class RdbReader
{
    private readonly string _filePath;

    public RdbReader(string filePath) => _filePath = filePath;

    public bool FileExists() => File.Exists(_filePath);

    /// <summary>
    /// Loads the RDB file into a single Storage (single-thread mode).
    /// Returns null if the file doesn't exist or the checksum fails.
    /// </summary>
    public Storage? LoadSingle()
    {
        if (!FileExists()) return null;

        var storage = new Storage();
        try
        {
            Load([storage]);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RDB] Failed to load '{_filePath}': {ex.Message}");
            return null;
        }
        return storage;
    }

    /// <summary>
    /// Loads the RDB file and distributes keys across N Storage shards (multi-thread mode).
    /// Uses FNV-1a hash to route each key to the correct shard — same algorithm as the router.
    /// </summary>
    public Storage[]? LoadSharded(int numShards)
    {
        if (!FileExists()) return null;

        var shards = new Storage[numShards];
        for (int i = 0; i < numShards; i++) shards[i] = new Storage();

        try
        {
            Load(shards);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RDB] Failed to load '{_filePath}': {ex.Message}");
            return null;
        }
        return shards;
    }

    // -------------------------------------------------------------------------
    // Core parser
    // -------------------------------------------------------------------------

    private void Load(Storage[] targets)
    {
        byte[] fileBytes = File.ReadAllBytes(_filePath);

        if (fileBytes.Length < 10)
            throw new InvalidDataException("RDB file is too small.");

        // Validate CRC64 — last 8 bytes are the checksum over everything before them
        ulong storedCrc  = ReadLe64(fileBytes, fileBytes.Length - 8);
        ulong computedCrc = Crc64.Compute(fileBytes.AsSpan(0, fileBytes.Length - 8));
        if (storedCrc != computedCrc)
            throw new InvalidDataException($"RDB CRC64 mismatch. File may be corrupted. Stored=0x{storedCrc:X16}, Computed=0x{computedCrc:X16}");

        // Validate magic + version
        string magic   = Encoding.ASCII.GetString(fileBytes, 0, 5);
        string version = Encoding.ASCII.GetString(fileBytes, 5, 4);
        if (magic != RdbConstants.Magic)
            throw new InvalidDataException($"Invalid RDB magic '{magic}'. Expected '{RdbConstants.Magic}'.");

        int pos = 9; // skip magic (5) + version (4)
        long? pendingExpiryMs = null;
        int currentDb = 0;

        while (pos < fileBytes.Length - 8) // -8 to skip the CRC64 footer
        {
            byte opcode = fileBytes[pos++];

            switch (opcode)
            {
                case RdbConstants.OpcodeAux:
                    // Skip aux fields — just metadata we don't need to restore
                    ReadStringAt(fileBytes, ref pos); // key
                    ReadStringAt(fileBytes, ref pos); // value
                    break;

                case RdbConstants.OpcodeSelectDb:
                    currentDb = (int)ReadLengthAt(fileBytes, ref pos);
                    break;

                case RdbConstants.OpcodeResizeDb:
                    ReadLengthAt(fileBytes, ref pos); // dict size hint (ignored)
                    ReadLengthAt(fileBytes, ref pos); // expiry size hint (ignored)
                    break;

                case RdbConstants.OpcodeExpireMs:
                    pendingExpiryMs = (long)ReadLe64(fileBytes, pos);
                    pos += 8;
                    break;

                case RdbConstants.OpcodeEof:
                    // Done — the CRC was already validated above
                    return;

                default:
                    // opcode is actually a value-type byte
                    byte valueType = opcode;
                    string key = ReadStringAt(fileBytes, ref pos);

                    // Route key to the correct shard
                    Storage target = targets.Length == 1
                        ? targets[0]
                        : targets[Fnv1aHash(key) % (uint)targets.Length];

                    LoadKeyValue(fileBytes, ref pos, target, valueType, key, pendingExpiryMs);
                    pendingExpiryMs = null;
                    break;
            }
        }
    }

    private void LoadKeyValue(byte[] data, ref int pos, Storage target,
                              byte valueType, string key, long? expiryMs)
    {
        // Compute TTL from absolute expiry timestamp stored in the file
        long ttlMs = -1;
        if (expiryMs.HasValue)
        {
            long remaining = expiryMs.Value - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (remaining <= 0) return; // Already expired — skip key
            ttlMs = remaining;
        }

        switch (valueType)
        {
            case RdbConstants.TypeString:
            {
                string value = ReadStringAt(data, ref pos);
                var obj = target.DictStore.NewObj(key, value, ttlMs);
                target.DictStore.Set(key, obj);
                break;
            }

            case RdbConstants.TypeHash:
            {
                uint count = ReadLengthAt(data, ref pos);
                var dict = new Dictionary<string, string>((int)count);
                for (uint i = 0; i < count; i++)
                {
                    string field = ReadStringAt(data, ref pos);
                    string val   = ReadStringAt(data, ref pos);
                    dict[field] = val;
                }
                target.HashStore[key] = dict;
                break;
            }

            case RdbConstants.TypeList:
            {
                uint count = ReadLengthAt(data, ref pos);
                var list = new LinkedList<string>();
                for (uint i = 0; i < count; i++)
                    list.AddLast(ReadStringAt(data, ref pos));
                target.ListStore[key] = list;
                break;
            }

            case RdbConstants.TypeSet:
            {
                uint count = ReadLengthAt(data, ref pos);
                var set = new SimpleSet(key);
                for (uint i = 0; i < count; i++)
                    set.Add(ReadStringAt(data, ref pos));
                target.SetStore[key] = set;
                break;
            }

            case RdbConstants.TypeZSet:
            {
                uint count = ReadLengthAt(data, ref pos);
                var zset = new ZSet();
                for (uint i = 0; i < count; i++)
                {
                    string member = ReadStringAt(data, ref pos);
                    double score  = BitConverter.UInt64BitsToDouble(ReadLe64(data, pos));
                    pos += 8;
                    zset.Add(score, member);
                }
                target.ZSetStore[key] = zset;
                break;
            }

            case RdbConstants.TypeBloom:
            {
                ulong entries = ReadLe64(data, pos); pos += 8;
                double error  = BitConverter.UInt64BitsToDouble(ReadLe64(data, pos)); pos += 8;
                uint byteLen  = ReadLengthAt(data, ref pos);
                byte[] bits   = data[pos..(pos + (int)byteLen)];
                pos += (int)byteLen;
                var bloom = new Bloom(entries, error);
                bloom.LoadBitArray(bits);
                target.BloomStore[key] = bloom;
                break;
            }

            case RdbConstants.TypeCms:
            {
                uint width = ReadLe32(data, pos); pos += 4;
                uint depth = ReadLe32(data, pos); pos += 4;
                var cms = new CMS(width, depth);
                var counters = cms.GetCounters();
                for (int i = 0; i < depth; i++)
                    for (int j = 0; j < width; j++)
                    {
                        counters[i][j] = ReadLe32(data, pos);
                        pos += 4;
                    }
                target.CmsStore[key] = cms;
                break;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Primitive decoding helpers
    // -------------------------------------------------------------------------

    private static string ReadStringAt(byte[] data, ref int pos)
    {
        uint length = ReadLengthAt(data, ref pos);
        string result = Encoding.UTF8.GetString(data, pos, (int)length);
        pos += (int)length;
        return result;
    }

    private static uint ReadLengthAt(byte[] data, ref int pos)
    {
        byte first = data[pos++];
        int encodingType = (first & 0xC0) >> 6;

        return encodingType switch
        {
            0 => (uint)(first & 0x3F),
            1 => (uint)(((first & 0x3F) << 8) | data[pos++]),
            2 => (uint)(data[pos++] << 24 | data[pos++] << 16 | data[pos++] << 8 | data[pos++]),
            _ => throw new InvalidDataException($"Unsupported length encoding type: {encodingType}")
        };
    }

    private static ulong ReadLe64(byte[] data, int pos) =>
        (ulong)data[pos]
        | ((ulong)data[pos + 1] << 8)
        | ((ulong)data[pos + 2] << 16)
        | ((ulong)data[pos + 3] << 24)
        | ((ulong)data[pos + 4] << 32)
        | ((ulong)data[pos + 5] << 40)
        | ((ulong)data[pos + 6] << 48)
        | ((ulong)data[pos + 7] << 56);

    private static uint ReadLe32(byte[] data, int pos) =>
        (uint)data[pos]
        | ((uint)data[pos + 1] << 8)
        | ((uint)data[pos + 2] << 16)
        | ((uint)data[pos + 3] << 24);

    // Same FNV-1a as HyperionServer.GetPartitionId — must stay in sync
    private static uint Fnv1aHash(string key)
    {
        const uint fnvPrime       = 16777619;
        const uint fnvOffsetBasis = 2166136261;
        uint hash = fnvOffsetBasis;

        int maxBytes = Encoding.UTF8.GetMaxByteCount(key.Length);
        if (maxBytes <= 256)
        {
            Span<byte> bytes = stackalloc byte[maxBytes];
            int written = Encoding.UTF8.GetBytes(key, bytes);
            for (int i = 0; i < written; i++) { hash ^= bytes[i]; hash *= fnvPrime; }
        }
        else
        {
            foreach (byte b in Encoding.UTF8.GetBytes(key)) { hash ^= b; hash *= fnvPrime; }
        }
        return hash;
    }
}
