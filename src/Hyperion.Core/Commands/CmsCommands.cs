using System;
using System.Globalization;
using Hyperion.Config;
using Hyperion.DataStructures;
using Hyperion.Protocol;

namespace Hyperion.Core.Commands;

public class CmsCommands
{
    private readonly Storage _storage;

    public CmsCommands(Storage storage)
    {
        _storage = storage;
    }

    public byte[] CmsInitByDim(string[] args)
    {
        if (args.Length != 3)
            return RespEncoder.Encode(new Exception("ERR wrong number of arguments for 'CMS.INITBYDIM' command"));

        string key = args[0];
        if (!uint.TryParse(args[1], out uint width))
            return RespEncoder.Encode(new Exception($"ERR width must be an integer number {args[1]}"));

        if (!uint.TryParse(args[2], out uint height))
            return RespEncoder.Encode(new Exception($"ERR height must be an integer number {args[2]}"));

        if (_storage.CmsStore.ContainsKey(key))
            return RespEncoder.Encode(new Exception("ERR CMS: key already exists"));

        var cms = new CMS(width, height);
        _storage.CmsStore.TryAdd(key, cms);
        return Constants.RespOk;
    }

    public byte[] CmsInitByProb(string[] args)
    {
        if (args.Length != 3)
            return RespEncoder.Encode(new Exception("ERR wrong number of arguments for 'CMS.INITBYPROB' command"));

        string key = args[0];
        if (!double.TryParse(args[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double errRate))
            return RespEncoder.Encode(new Exception($"ERR errRate must be a floating point number {args[1]}"));

        if (errRate >= 1 || errRate <= 0)
            return RespEncoder.Encode(new Exception("ERR CMS: invalid overestimation value"));

        if (!double.TryParse(args[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double probability))
            return RespEncoder.Encode(new Exception($"ERR probability must be a floating point number {args[2]}"));

        if (probability >= 1 || probability <= 0)
            return RespEncoder.Encode(new Exception("ERR CMS: invalid prob value"));

        if (_storage.CmsStore.ContainsKey(key))
            return RespEncoder.Encode(new Exception("ERR CMS: key already exists"));

        var (w, d) = CMS.CalcCMSDim(errRate, probability);
        var cms = new CMS(w, d);
        _storage.CmsStore.TryAdd(key, cms);
        return Constants.RespOk;
    }

    public byte[] CmsIncrBy(string[] args)
    {
        if (args.Length < 3 || args.Length % 2 == 0)
            return RespEncoder.Encode(new Exception("ERR wrong number of arguments for 'CMS.INCRBY' command"));

        string key = args[0];
        if (!_storage.CmsStore.TryGetValue(key, out var cms))
            return RespEncoder.Encode(new Exception("ERR CMS: key does not exist"));

        int pairs = (args.Length - 1) / 2;
        object[] res = new object[pairs];

        for (int i = 1, resIdx = 0; i < args.Length; i += 2, resIdx++)
        {
            string item = args[i];
            if (!uint.TryParse(args[i + 1], out uint increment))
                return RespEncoder.Encode(new Exception($"ERR increment must be a non negative integer number {args[i + 1]}"));

            uint count = cms.IncrBy(item, increment);
            if (count == uint.MaxValue)
            {
                res[resIdx] = "CMS: INCRBY overflow";
            }
            else
            {
                res[resIdx] = (long)count;
            }
        }

        return RespEncoder.Encode(res);
    }

    public byte[] CmsQuery(string[] args)
    {
        if (args.Length < 2)
            return RespEncoder.Encode(new Exception("ERR wrong number of arguments for 'CMS.QUERY' command"));

        string key = args[0];
        if (!_storage.CmsStore.TryGetValue(key, out var cms))
            return RespEncoder.Encode(new Exception("ERR CMS: key does not exist"));

        object[] res = new object[args.Length - 1];
        for (int i = 1; i < args.Length; i++)
        {
            string item = args[i];
            res[i - 1] = (long)cms.Count(item);
        }

        return RespEncoder.Encode(res);
    }
}
