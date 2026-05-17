using System.Text;
using Hyperion.Core;
using Hyperion.DataStructures;

namespace Hyperion.Persistence;

/// <summary>
/// Serializes one or more Storage shards into the Hyperion RDB binary format.
///
/// FILE LAYOUT (single-thread mode, single shard):
///   [Magic "REDIS"] [Version "0011"]
///   [0xFA Aux: "hyperion-ver" → version string]
///   [0xFA Aux: "ctime" → creation unix timestamp]
///   [0xFA Aux: "used-mem" → approximate heap usage]
///   [0xFA Aux: "mode" → "single" or "multi"]
///   [0xFE SelectDb → db index 0]
///   [0xFB ResizeDb → dict count, expiry count]
///   [for each key: optional 0xFC + 8-byte TTL, then type byte, key, value]
///   [0xFF EOF]
///   [8-byte CRC64, little-endian]
///
/// MULTI-THREAD MODE:
///   Each shard is written as a separate logical block preceded by a shard-id
///   marker. The reader uses FNV-1a re-partitioning on load to distribute keys
///   across the correct workers.
/// </summary>
public sealed class RdbWriter : IDisposable
{
    private readonly Stream _stream;
    private readonly BinaryWriter _writer;
    private ulong _crc;
    private bool _disposed;

    public RdbWriter(Stream stream)
    {
        _stream = stream;
        _writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        _crc = 0;
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>Writes the RDB header (magic + version).</summary>
    public void WriteHeader()
    {
        WriteRaw(Encoding.ASCII.GetBytes(RdbConstants.Magic));
        WriteRaw(Encoding.ASCII.GetBytes(RdbConstants.Version));
    }

    /// <summary>Writes all metadata auxiliary fields.</summary>
    public void WriteMetadata(string mode)
    {
        WriteAuxField("hyperion-ver", "1.0.0");
        WriteAuxField("ctime", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        WriteAuxField("used-mem", GC.GetTotalMemory(false).ToString());
        WriteAuxField("mode", mode);
    }

    /// <summary>Writes a complete database section for one Storage shard.</summary>
    public void WriteDatabase(int dbIndex, Storage storage)
    {
        // --- 0xFE: Select Database ---
        WriteOpcode(RdbConstants.OpcodeSelectDb);
        WriteLengthEncoding((uint)dbIndex);

        // Count only non-expired keys for the ResizeDB hint
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int totalKeys  = CountNonExpiredKeys(storage, now);
        int expiryKeys = CountExpiryKeys(storage, now);

        // --- 0xFB: ResizeDB hint ---
        WriteOpcode(RdbConstants.OpcodeResizeDb);
        WriteLengthEncoding((uint)totalKeys);
        WriteLengthEncoding((uint)expiryKeys);

        // --- Key-Value pairs ---
        WriteDict(storage, now);
        WriteHashStore(storage);
        WriteListStore(storage);
        WriteSetStore(storage);
        WriteZSetStore(storage);
        WriteBloomStore(storage);
        WriteCmsStore(storage);
    }

    /// <summary>
    /// Writes the EOF opcode and 8-byte CRC64 checksum.
    /// Must be called after all data is written.
    /// </summary>
    public void WriteFooter()
    {
        WriteOpcode(RdbConstants.OpcodeEof);
        // Write CRC64 of everything written so far (little-endian).
        // We do NOT include the CRC itself in the checksum calculation.
        _writer.Write(_crc); // BinaryWriter writes little-endian
        _writer.Flush();
    }

    // -------------------------------------------------------------------------
    // Data-type serializers
    // -------------------------------------------------------------------------

    private void WriteDict(Storage storage, long nowMs)
    {
        var expiryStore = storage.DictStore.GetExpireDictStore();

        // Enumerate via a snapshot to avoid mutation-while-iterating issues
        foreach (var kv in storage.DictStore.GetAllEntries())
        {
            string key   = kv.Key;
            string value = kv.Value.Value;

            // Skip keys that are already expired — don't persist them
            if (expiryStore.TryGetValue(key, out long expiry) && nowMs > expiry)
                continue;

            // Write optional expiry opcode
            if (expiryStore.TryGetValue(key, out long expiryMs))
            {
                WriteOpcode(RdbConstants.OpcodeExpireMs);
                WriteLe64((ulong)expiryMs);
            }

            WriteOpcode(RdbConstants.TypeString);
            WriteString(key);
            WriteString(value);
        }
    }

    private void WriteHashStore(Storage storage)
    {
        foreach (var kv in storage.HashStore)
        {
            WriteOpcode(RdbConstants.TypeHash);
            WriteString(kv.Key);
            WriteLengthEncoding((uint)kv.Value.Count);
            foreach (var field in kv.Value)
            {
                WriteString(field.Key);
                WriteString(field.Value);
            }
        }
    }

    private void WriteListStore(Storage storage)
    {
        foreach (var kv in storage.ListStore)
        {
            WriteOpcode(RdbConstants.TypeList);
            WriteString(kv.Key);
            WriteLengthEncoding((uint)kv.Value.Count);
            foreach (var item in kv.Value)
            {
                WriteString(item);
            }
        }
    }

    private void WriteSetStore(Storage storage)
    {
        foreach (var kv in storage.SetStore)
        {
            var members = kv.Value.Members();
            WriteOpcode(RdbConstants.TypeSet);
            WriteString(kv.Key);
            WriteLengthEncoding((uint)members.Length);
            foreach (var member in members)
            {
                WriteString(member);
            }
        }
    }

    private void WriteZSetStore(Storage storage)
    {
        foreach (var kv in storage.ZSetStore)
        {
            var entries = kv.Value.GetAllEntries();
            WriteOpcode(RdbConstants.TypeZSet);
            WriteString(kv.Key);
            WriteLengthEncoding((uint)entries.Count);
            foreach (var (member, score) in entries)
            {
                WriteString(member);
                // Scores are stored as 8-byte IEEE 754 double (little-endian)
                WriteLe64(BitConverter.DoubleToUInt64Bits(score));
            }
        }
    }

    private void WriteBloomStore(Storage storage)
    {
        foreach (var kv in storage.BloomStore)
        {
            WriteOpcode(RdbConstants.TypeBloom);
            WriteString(kv.Key);

            var bloom = kv.Value;
            // Store the parameters so we can reconstruct the exact filter on load
            WriteLe64(bloom.Entries);
            WriteDouble(bloom.Error);

            // Store the raw bit-array bytes
            var bytes = bloom.GetBitArray();
            WriteLengthEncoding((uint)bytes.Length);
            WriteRaw(bytes);
        }
    }

    private void WriteCmsStore(Storage storage)
    {
        foreach (var kv in storage.CmsStore)
        {
            WriteOpcode(RdbConstants.TypeCms);
            WriteString(kv.Key);

            var cms = kv.Value;
            WriteLe32(cms.Width);
            WriteLe32(cms.Depth);

            // Write the entire 2D counter array row by row
            var counters = cms.GetCounters();
            foreach (var row in counters)
            {
                foreach (var cell in row)
                {
                    WriteLe32(cell);
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Primitive encoding helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Variable-length integer encoding (same as Redis).
    /// Uses the top 2 bits of the first byte to signal the width:
    ///   00xxxxxx → 6-bit (values 0-63)
    ///   01xxxxxx + 1 byte → 14-bit (values 64-16383)
    ///   10000000 + 4 bytes → 32-bit (big-endian, values 16384+)
    /// This saves space for the common case of small lengths.
    /// </summary>
    public void WriteLengthEncoding(uint length)
    {
        if (length <= 63)
        {
            WriteByte((byte)(length & 0x3F));
        }
        else if (length <= 16383)
        {
            WriteByte((byte)(((length >> 8) & 0x3F) | 0x40));
            WriteByte((byte)(length & 0xFF));
        }
        else
        {
            WriteByte(0x80);
            // Big-endian 4-byte integer
            WriteByte((byte)((length >> 24) & 0xFF));
            WriteByte((byte)((length >> 16) & 0xFF));
            WriteByte((byte)((length >> 8) & 0xFF));
            WriteByte((byte)(length & 0xFF));
        }
    }

    /// <summary>
    /// Writes a string as: length-encoded-size followed by raw UTF-8 bytes.
    /// </summary>
    public void WriteString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteLengthEncoding((uint)bytes.Length);
        WriteRaw(bytes);
    }

    private void WriteAuxField(string key, string value)
    {
        WriteOpcode(RdbConstants.OpcodeAux);
        WriteString(key);
        WriteString(value);
    }

    private void WriteOpcode(byte opcode) => WriteByte(opcode);

    private void WriteLe64(ulong value)
    {
        Span<byte> buf = stackalloc byte[8];
        buf[0] = (byte)(value);
        buf[1] = (byte)(value >> 8);
        buf[2] = (byte)(value >> 16);
        buf[3] = (byte)(value >> 24);
        buf[4] = (byte)(value >> 32);
        buf[5] = (byte)(value >> 40);
        buf[6] = (byte)(value >> 48);
        buf[7] = (byte)(value >> 56);
        WriteRaw(buf);
    }

    private void WriteLe32(uint value)
    {
        Span<byte> buf = stackalloc byte[4];
        buf[0] = (byte)(value);
        buf[1] = (byte)(value >> 8);
        buf[2] = (byte)(value >> 16);
        buf[3] = (byte)(value >> 24);
        WriteRaw(buf);
    }

    private void WriteDouble(double value) => WriteLe64(BitConverter.DoubleToUInt64Bits(value));

    private void WriteByte(byte b)
    {
        _crc = Crc64.Update(_crc, new ReadOnlySpan<byte>(ref b));
        _writer.Write(b);
    }

    private void WriteRaw(ReadOnlySpan<byte> data)
    {
        _crc = Crc64.Update(_crc, data);
        _writer.Write(data);
    }

    // -------------------------------------------------------------------------
    // Counting helpers (used for ResizeDB hint)
    // -------------------------------------------------------------------------

    private static int CountNonExpiredKeys(Storage storage, long nowMs)
    {
        int count = 0;
        var expiry = storage.DictStore.GetExpireDictStore();
        foreach (var kv in storage.DictStore.GetAllEntries())
        {
            if (!expiry.TryGetValue(kv.Key, out long exp) || nowMs <= exp)
                count++;
        }
        count += storage.HashStore.Count;
        count += storage.ListStore.Count;
        count += storage.SetStore.Count;
        count += storage.ZSetStore.Count;
        count += storage.BloomStore.Count;
        count += storage.CmsStore.Count;
        return count;
    }

    private static int CountExpiryKeys(Storage storage, long nowMs)
    {
        int count = 0;
        var expiry = storage.DictStore.GetExpireDictStore();
        foreach (var kv in expiry)
        {
            if (nowMs <= kv.Value) count++;
        }
        return count;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _writer.Dispose();
            _disposed = true;
        }
    }
}
