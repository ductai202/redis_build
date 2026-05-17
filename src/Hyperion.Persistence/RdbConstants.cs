namespace Hyperion.Persistence;

/// <summary>
/// All opcode bytes and value-type bytes used in the RDB binary format.
///
/// HOW OPCODES WORK:
/// The RDB file is a sequential stream of bytes. Without opcodes, the reader
/// would have no way to know whether the next bytes represent a key, a value,
/// a TTL timestamp, a database selector, or the end of the file.
///
/// Opcodes act as "signposts": before any logical section of data, we write
/// one special byte (the opcode). The reader checks this byte first and then
/// knows exactly how to interpret the bytes that follow.
///
/// Example sequence in the file:
///   0xFA  → "Aux field follows: read key-value string pair"
///   0xFE  → "Database selector follows: read 1 length-encoded integer"
///   0xFB  → "ResizeDB hint follows: read 2 length-encoded integers"
///   0xFC  → "Expiry-ms follows: read 8-byte little-endian uint64, then the key-value"
///   0xFF  → "EOF: read 8-byte CRC64 checksum, then stop"
/// </summary>
public static class RdbConstants
{
    // --- File Identity ---
    public const string Magic   = "REDIS";
    public const string Version = "0011"; // RDB version 11 (Hyperion-native format)

    // --- Opcodes (signpost bytes the reader checks before interpreting data) ---
    public const byte OpcodeAux        = 0xFA; // Auxiliary metadata field (key-value pair of strings)
    public const byte OpcodeSelectDb   = 0xFE; // Database index selector
    public const byte OpcodeResizeDb   = 0xFB; // Resize hint: (dict size, expiry dict size)
    public const byte OpcodeExpireMs   = 0xFC; // Key expiry in milliseconds (8-byte uint64)
    public const byte OpcodeExpireSec  = 0xFD; // Key expiry in seconds (4-byte uint32)  [unused, for compat]
    public const byte OpcodeEof        = 0xFF; // End of file, followed by 8-byte CRC64

    // --- Value Type bytes (written after optional expiry, before the key string) ---
    public const byte TypeString = 0;   // String value  (stored in DictStore)
    public const byte TypeList   = 1;   // LinkedList<string>
    public const byte TypeSet    = 2;   // SimpleSet (HashSet-like)
    public const byte TypeZSet   = 3;   // ZSet (Dict + Skiplist)
    public const byte TypeHash   = 4;   // Dictionary<string,string>
    public const byte TypeBloom  = 10;  // Bloom filter  (Hyperion-native, not in stock Redis)
    public const byte TypeCms    = 11;  // Count-Min Sketch (Hyperion-native)

    // --- Length-encoding special values ---
    // The top 2 bits of the first byte determine the encoding width:
    //   00xxxxxx  → 6-bit length (max 63)
    //   01xxxxxx  → 14-bit length (read 1 more byte)
    //   10xxxxxx  → 32-bit length (read 4 more bytes, big-endian)
    //   11xxxxxx  → Special integer encoding (value is in lower 6 bits)
    public const byte Len6Bit  = 0; // top 2 bits = 00
    public const byte Len14Bit = 1; // top 2 bits = 01
    public const byte Len32Bit = 2; // top 2 bits = 10
    public const byte LenSpecial = 3; // top 2 bits = 11 (integer encoded as string)

    // Special integer encodings (stored in lower 6 bits when top 2 bits = 11)
    public const byte EncInt8  = 0; // 8-bit signed integer
    public const byte EncInt16 = 1; // 16-bit signed integer
    public const byte EncInt32 = 2; // 32-bit signed integer

    // --- Shard metadata marker for multi-thread RDB files ---
    // Written before each shard block so the reader can split data across workers.
    public const byte OpcodeShardStart = 0xFD - 1; // 0xFC used, pick safe unused value

    // --- File defaults ---
    public const string DefaultFileName = "dump.rdb";
    public const string TempFileSuffix  = ".tmp";
}
