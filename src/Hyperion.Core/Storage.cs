using System.Collections.Generic;
using Hyperion.DataStructures;

namespace Hyperion.Core;

/// <summary>
/// Private storage shard owned exclusively by one Worker.
/// Uses plain (non-concurrent) collections because the share-nothing routing model
/// guarantees that only the owning Worker thread ever reads or writes this storage.
/// Switching from ConcurrentDictionary removes striped-lock overhead, volatile reads,
/// and memory barriers — yielding a significant throughput improvement for GET/SET.
/// </summary>
public class Storage
{
    public Dict DictStore { get; } = new();
    public Dictionary<string, SimpleSet> SetStore { get; } = new();
    public Dictionary<string, ZSet> ZSetStore { get; } = new();
    public Dictionary<string, Bloom> BloomStore { get; } = new();
    public Dictionary<string, CMS> CmsStore { get; } = new();
    public Dictionary<string, Dictionary<string, string>> HashStore { get; } = new();
    public Dictionary<string, System.Collections.Generic.LinkedList<string>> ListStore { get; } = new();
}
