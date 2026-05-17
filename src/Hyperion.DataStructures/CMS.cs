using System;
using System.Security.Cryptography;
using System.Text;

namespace Hyperion.DataStructures;

/// <summary>
/// Count-Min Sketch data structure for estimating frequencies of items.
/// </summary>
public class CMS
{
    // Log10PointFive is a precomputed value for log10(0.5).
    // It's used in calculating the optimal depth (number of hash functions) for the CMS.
    // Depth (d) = ceil(log10(error_probability) / log10(0.5))
    // This formula ensures the probability that the estimate exceeds the true frequency by more than the error bound is at most errProb.
    private const double Log10PointFive = -0.30102999566;

    public uint Width { get; }
    public uint Depth { get; }

    private readonly uint[][] _counter;

    public CMS(uint width, uint depth)
    {
        Width = width;
        Depth = depth;

        _counter = new uint[depth][];
        for (uint i = 0; i < depth; i++)
        {
            _counter[i] = new uint[width];
        }
    }

    public static (uint width, uint depth) CalcCMSDim(double errRate, double errProb)
    {
        uint w = (uint)Math.Ceiling(2.0 / errRate);
        uint d = (uint)Math.Ceiling(Math.Log10(errProb) / Log10PointFive);
        return (w, d);
    }

    private uint CalcHash(string item, uint seed)
    {
        // Simple hash combining the item and the seed using MD5 to simulate seeded hash.
        byte[] inputBytes = Encoding.UTF8.GetBytes(item + seed.ToString());
        byte[] hashBytes = MD5.HashData(inputBytes);

        return BitConverter.ToUInt32(hashBytes, 0);
    }

    public uint IncrBy(string item, uint value)
    {
        uint minCount = uint.MaxValue;

        for (uint i = 0; i < Depth; i++)
        {
            uint hash = CalcHash(item, i);
            uint j = hash % Width;

            if (uint.MaxValue - _counter[i][j] < value)
            {
                _counter[i][j] = uint.MaxValue;
            }
            else
            {
                _counter[i][j] += value;
            }

            if (_counter[i][j] < minCount)
            {
                minCount = _counter[i][j];
            }
        }

        return minCount;
    }

    public uint Count(string item)
    {
        uint minCount = uint.MaxValue;

        for (uint i = 0; i < Depth; i++)
        {
            uint hash = CalcHash(item, i);
            uint j = hash % Width;

            if (_counter[i][j] < minCount)
            {
                minCount = _counter[i][j];
            }
        }

        return minCount;
    }

    /// <summary>
    /// Exposes the internal counter matrix for RDB serialization.
    /// The caller reads/writes each cell directly for fast I/O.
    /// </summary>
    public uint[][] GetCounters() => _counter;
}
