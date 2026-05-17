using System.Collections.Generic;
using Hyperion.DataStructures;

namespace Hyperion.Core;

/// <summary>
/// Private storage shard owned exclusively by one Worker.
/// Uses plain (non-concurrent) collections because the share-nothing routing model
/// guarantees that only the owning Worker thread ever reads or writes this storage.
/// Switching from ConcurrentDictionary removes striped-lock overhead, volatile reads,
/// and memory barriers — yielding a significant throughput improvement for GET/SET.
///
/// DirtyCount is incremented on every write command and read (and reset) by the
/// SnapshotCoordinator to decide when to trigger an automatic RDB save.
/// </summary>
public class Storage
{
    public Dict DictStore { get; } = new();
    public Dictionary<string, SimpleSet> SetStore { get; } = new();
    public Dictionary<string, ZSet> ZSetStore { get; } = new();
    public Dictionary<string, Bloom> BloomStore { get; } = new();
    public Dictionary<string, CMS> CmsStore { get; } = new();
    public Dictionary<string, Dictionary<string, string>> HashStore { get; } = new();
    public Dictionary<string, LinkedList<string>> ListStore { get; } = new();

    // Tracks write commands since the last RDB save. Atomics not needed because
    // only the owning Worker thread ever calls IncrementDirty(), and the coordinator
    // reads it with a simple volatile read on the same or a different thread.
    private long _dirtyCount;
    public long DirtyCount => Interlocked.Read(ref _dirtyCount);
    public void IncrementDirty() => Interlocked.Increment(ref _dirtyCount);
    public void ResetDirty() => Interlocked.Exchange(ref _dirtyCount, 0);
}
