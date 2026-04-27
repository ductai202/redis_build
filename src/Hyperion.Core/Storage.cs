using Hyperion.DataStructures;

namespace Hyperion.Core;

public class Storage
{
    public Dict DictStore { get; } = new();
    public System.Collections.Concurrent.ConcurrentDictionary<string, SimpleSet> SetStore { get; } = new();
}
