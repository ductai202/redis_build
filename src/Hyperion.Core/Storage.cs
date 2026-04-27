using Hyperion.DataStructures;

namespace Hyperion.Core;

public class Storage
{
    public Dict DictStore { get; } = new();
    public System.Collections.Concurrent.ConcurrentDictionary<string, SimpleSet> SetStore { get; } = new();
    public System.Collections.Concurrent.ConcurrentDictionary<string, ZSet> ZSetStore { get; } = new();
    public System.Collections.Concurrent.ConcurrentDictionary<string, Bloom> BloomStore { get; } = new();
    public System.Collections.Concurrent.ConcurrentDictionary<string, CMS> CmsStore { get; } = new();
    public System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Generic.Dictionary<string, string>> HashStore { get; } = new();
    public System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Generic.LinkedList<string>> ListStore { get; } = new();
}
