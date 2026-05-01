using System.Collections.Generic;
using Hyperion.Config;
using Hyperion.DataStructures;

namespace Hyperion.Core;

/// <summary>
/// Background task that periodically samples keys and deletes expired ones.
/// Uses a snapshot of the expiry store keys to safely iterate while the worker
/// may concurrently modify the dictionary (though in practice it won't since
/// ActiveExpiry is called from within the same Worker's event loop).
/// </summary>
public class ActiveExpiry
{
    private readonly Storage _storage;

    public ActiveExpiry(Storage storage)
    {
        _storage = storage;
    }

    public void DeleteExpiredKeys()
    {
        while (true)
        {
            int expiredCount = 0;
            int sampleCountRemain = Constants.ActiveExpireSampleSize;

            // Take a snapshot of keys to avoid modifying the dictionary while iterating.
            // ToList() is safe here and only runs on a background expiry sweep, not the hot path.
            var expiryStore = _storage.DictStore.GetExpireDictStore();
            var keys = new List<string>(expiryStore.Keys);

            foreach (var key in keys)
            {
                if (sampleCountRemain-- <= 0) break;

                if (expiryStore.TryGetValue(key, out long expiry) &&
                    CoarseClock.NowMs > expiry)
                {
                    _storage.DictStore.Del(key);
                    expiredCount++;
                }
            }

            if ((double)expiredCount / Constants.ActiveExpireSampleSize <= Constants.ActiveExpireThreshold)
            {
                break;
            }
        }
    }
}
