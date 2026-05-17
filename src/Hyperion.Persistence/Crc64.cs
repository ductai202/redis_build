namespace Hyperion.Persistence;

/// <summary>
/// CRC-64/Jones implementation used by Redis to detect RDB file corruption.
///
/// WHY CRC64 INSTEAD OF MD5/SHA?
/// CRC is a mathematical error-detection algorithm, not a cryptographic one.
/// It is 10–50x faster than MD5 or SHA because it uses a pre-computed lookup
/// table (256 entries × 8 bytes = 2 KB) rather than complex round functions.
///
/// Security is NOT required here — we only need to detect accidental bit-flips
/// caused by disk errors, incomplete writes, or OS crashes. CRC64 is ideal.
///
/// HOW IT WORKS:
/// 1. Start with an initial value (0xFFFFFFFFFFFFFFFF in the Jones variant).
/// 2. For each byte in the data, XOR it with the low byte of the current CRC,
///    use that as a lookup index into the table, and XOR the result back.
/// 3. Finalize by XORing with 0xFFFFFFFFFFFFFFFF.
///
/// The 256-entry table below was generated from the polynomial:
///   0xad93d23594c935a9 (the Jones polynomial, same as Redis uses).
/// </summary>
public static class Crc64
{
    private static readonly ulong[] Table = GenerateTable(0xad93d23594c935a9UL);

    private static ulong[] GenerateTable(ulong polynomial)
    {
        var table = new ulong[256];
        for (uint i = 0; i < 256; i++)
        {
            ulong crc = i;
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 1) == 1)
                    crc = (crc >> 1) ^ polynomial;
                else
                    crc >>= 1;
            }
            table[i] = crc;
        }
        return table;
    }

    /// <summary>
    /// Computes the CRC64 checksum over <paramref name="data"/>.
    /// Call this after writing the entire file content (excluding the checksum itself).
    /// </summary>
    public static ulong Compute(ReadOnlySpan<byte> data)
    {
        ulong crc = 0; // Redis uses 0 as the initial value (not ~0)
        foreach (byte b in data)
        {
            crc = Table[(byte)(crc ^ b)] ^ (crc >> 8);
        }
        return crc;
    }

    /// <summary>
    /// Incrementally update an existing CRC64 value with more data.
    /// Allows computing the checksum as bytes are written, without buffering.
    /// </summary>
    public static ulong Update(ulong crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
        {
            crc = Table[(byte)(crc ^ b)] ^ (crc >> 8);
        }
        return crc;
    }
}
