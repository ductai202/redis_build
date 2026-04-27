# Data Structures Deep Dive

This document explains every custom data structure in Hyperion: the algorithm, the math behind it, its time complexity, and how our implementation follows the design of the original Redis source code.

---

## Table of Contents

1. [Dict (Hash Table with TTL)](#1-dict-hash-table-with-ttl)
2. [Eviction Pool (Approximate LRU)](#2-eviction-pool-approximate-lru)
3. [Skip List](#3-skip-list)
4. [ZSet (Sorted Set)](#4-zset-sorted-set)
5. [Bloom Filter](#5-bloom-filter)
6. [Count-Min Sketch (CMS)](#6-count-min-sketch-cms)

---

## 1. Dict (Hash Table with TTL)

**File:** `src/Hyperion.DataStructures/Dict.cs`

### What It Does
The `Dict` is Hyperion's core storage engine. It is the equivalent of Redis's `redisDb` — a key-value store that also tracks TTL (Time-To-Live) for key expiry and last-access timestamps for LRU eviction.

### Architecture

```
Dict
├── _store:  ConcurrentDictionary<string, DictObject>   ← Main key → value store
└── _expiryStore: ConcurrentDictionary<string, long>    ← Key → expiry timestamp (ms)

DictObject
├── Key: string
├── Value: string
└── LastAccessTime: long (Unix ms)     ← Updated on every Get(), used by LRU
```

### How It Works
- **SET** creates a `DictObject` with the current timestamp and stores it. If a TTL is specified, the expiry time is recorded separately in `_expiryStore`.
- **GET** retrieves the object and updates `LastAccessTime` to the current time (LRU touch).
- **Expiry Check** (`HasExpired`): Before returning a value, we compare the current time against the stored expiry. If expired, the key is lazily deleted and `nil` is returned.

### Redis Equivalence
In Redis, the `redisDb` struct has two dictionaries:
```c
typedef struct redisDb {
    dict *dict;     /* The keyspace */
    dict *expires;  /* Timeout of keys with a timeout set */
    ...
} redisDb;
```
Our `Dict` mirrors this exact pattern — `_store` is the keyspace, `_expiryStore` is the expires table.

### Complexity
| Operation | Time |
|---|---|
| SET | O(1) |
| GET | O(1) |
| DEL | O(1) |
| TTL check | O(1) |

---

## 2. Eviction Pool (Approximate LRU)

**File:** `src/Hyperion.DataStructures/EvictionPool.cs`

### What It Does
When the database reaches its memory limit, Hyperion must evict keys. Instead of maintaining a full LRU linked list (expensive), Redis uses a **sampling-based approximate LRU** approach. The Eviction Pool is the sorting buffer for sampled candidates.

### How It Works (The Algorithm)

1. **Sampling**: Periodically, N random keys are sampled from the keyspace.
2. **Insertion**: Each sampled key's `LastAccessTime` is compared against the current pool. If the key is "older" (less recently accessed) than the newest item in the pool, it replaces it.
3. **Eviction**: When memory pressure triggers eviction, the **oldest** item in the pool (position 0) is removed and its key is deleted from the database.

```
Pool (sorted by LastAccessTime, oldest first):
┌──────────────────────────────────────────────────────┐
│  key_A (1000ms)  │  key_C (1500ms)  │  key_B (2000ms)│
│    ← oldest                              newest →    │
│    ← evict first                                     │
└──────────────────────────────────────────────────────┘
```

### Redis Equivalence
Redis source (`evict.c`) uses the same approach:
```c
struct evictionPoolEntry {
    unsigned long long idle;    /* Object idle time */
    sds key;                    /* Key name */
    ...
};
```
The pool maintains a fixed number of entries (`EVPOOL_SIZE = 16` in Redis). Our implementation uses `ServerConfig.EpoolMaxSize`.

### Why Not Full LRU?
A true LRU would require a doubly-linked list + hash map, adding 16+ bytes of overhead per key. Redis's author (Antirez) found that sampling just 5 keys per eviction cycle achieves **~95% accuracy** compared to perfect LRU, at zero per-key overhead.

### Complexity
| Operation | Time |
|---|---|
| Push (insert + sort) | O(N) where N = pool size (typically 16) |
| Pop (evict oldest) | O(1) |

---

## 3. Skip List

**File:** `src/Hyperion.DataStructures/Skiplist.cs`

### What It Does
The Skip List is a probabilistic data structure that provides sorted-order operations (insert, delete, rank, range) with O(log N) average complexity — similar to a balanced binary tree, but simpler to implement and with good cache behavior.

### How It Works

A Skip List is a hierarchy of linked lists. The bottom level (Level 0) contains all elements in sorted order. Each higher level is a "fast lane" that skips over elements, allowing binary-search-like traversal.

```
Level 3:  HEAD ─────────────────────────────────────────▶ NIL
Level 2:  HEAD ──────────▶ 3 ──────────────▶ 7 ────────▶ NIL
Level 1:  HEAD ──▶ 1 ───▶ 3 ───▶ 5 ───────▶ 7 ────────▶ NIL
Level 0:  HEAD ──▶ 1 ──▶ 2 ──▶ 3 ──▶ 4 ──▶ 5 ──▶ 6 ──▶ 7 ──▶ NIL
```

**Random Level Assignment**: When inserting a node, its level is determined by a coin-flip loop:
```csharp
private int RandomLevel()
{
    int level = 1;
    while (_random.Next(2) == 1) level++;
    return Math.Min(level, MaxLevel); // MaxLevel = 32
}
```
This gives each level a **50% probability** of being promoted to the next level, producing an expected O(log N) depth.

### Span Tracking
Unlike a basic Skip List, our implementation (following Redis exactly) tracks a **span** value on each forward pointer. The span counts how many nodes are skipped by that pointer. This allows O(log N) **rank queries** — "what is the rank of element X?" — by summing spans during traversal.

```csharp
public class SkiplistLevel
{
    public SkiplistNode? Forward { get; set; }  // Next node at this level
    public uint Span { get; set; }              // # of nodes skipped
}
```

### Redis Equivalence
Our implementation follows `redis/src/t_zset.c`:
```c
typedef struct zskiplistNode {
    sds ele;
    double score;
    struct zskiplistNode *backward;
    struct zskiplistLevel {
        struct zskiplistNode *forward;
        unsigned long span;
    } level[];
} zskiplistNode;
```
The mapping is exact:
- `Ele` → `ele`
- `Score` → `score`
- `Backward` → `backward`
- `Levels[i].Forward` → `level[i].forward`
- `Levels[i].Span` → `level[i].span`

### Complexity
| Operation | Average | Worst |
|---|---|---|
| Insert | O(log N) | O(N) |
| Delete | O(log N) | O(N) |
| GetRank | O(log N) | O(N) |
| Range | O(log N + M) | O(N + M) |

where M = number of elements in the returned range.

---

## 4. ZSet (Sorted Set)

**File:** `src/Hyperion.DataStructures/ZSet.cs`

### What It Does
ZSet implements Redis's Sorted Set — a collection where each element has an associated score. Elements are unique by name, but sorted by score. It supports both O(1) score lookups and O(log N) rank/range queries.

### The Dual-Structure Design

This is one of Redis's most elegant architectural decisions. A single data structure cannot efficiently support both "get score by name" and "get rank by score". Redis solves this by using **two synchronized structures**:

```
ZSet
├── _dict: Dictionary<string, double>     ← O(1) member → score lookup
└── _zskiplist: Skiplist                  ← O(log N) sorted traversal, rank, range
```

**Every mutation (Add, Rem, UpdateScore) updates both structures atomically.**

### Insert Flow

```
ZADD leaderboard 100 "alice"
         │
         ▼
┌─ Check _dict for "alice" ──────────────────────────────┐
│                                                         │
│  NOT FOUND:                    FOUND (score changed):   │
│  ├─ _zskiplist.Insert(100, "alice")  ├─ _zskiplist.UpdateScore(...)  │
│  └─ _dict["alice"] = 100             └─ _dict["alice"] = newScore    │
└─────────────────────────────────────────────────────────┘
```

### Score Update Optimization
When updating a score, if the new score doesn't change the node's relative position (it's still between its predecessor and successor), we perform an **in-place update** — just modifying `node.Score` without removing and reinserting. This is a direct port of the optimization in Redis's `zslUpdateScore`.

```csharp
// If new position is still valid, just update in place
if ((x.Backward == null || x.Backward.Score < newScore) &&
    (x.Levels[0].Forward == null || x.Levels[0].Forward.Score > newScore))
{
    x.Score = newScore;  // No structural change needed!
    return x;
}
// Otherwise, delete + reinsert
DeleteNode(x, update);
return Insert(newScore, ele);
```

### Redis Equivalence
Redis `t_zset.c`:
```c
typedef struct zset {
    dict *dict;
    zskiplist *zsl;
} zset;
```
Our `ZSet` is a 1:1 equivalent.

### Complexity
| Operation | Time |
|---|---|
| ZADD | O(log N) |
| ZREM | O(log N) |
| ZSCORE | O(1) — via dictionary |
| ZRANK | O(log N) — via skip list span |
| ZRANGE | O(log N + M) |

---

## 5. Bloom Filter

**File:** `src/Hyperion.DataStructures/Bloom.cs`

### What It Does
A Bloom Filter answers the question "Is this element **possibly** in the set?" with zero false negatives but a controlled false positive rate. It uses no actual storage of elements — only a compact bit array and hash functions.

### How It Works

1. **Reserve**: Given the expected number of entries (n) and desired error rate (p), calculate the optimal bit array size and number of hash functions.
2. **Add**: Hash the element k times, set each corresponding bit to 1.
3. **Exists**: Hash the element k times, check if ALL corresponding bits are 1. If any is 0, the element is **definitely not** in the set.

```
Add("hello"):
  hash_0("hello") = 3   → bits[3]  = 1
  hash_1("hello") = 17  → bits[17] = 1
  hash_2("hello") = 42  → bits[42] = 1

Exists("hello"):
  bits[3] = 1 ✓, bits[17] = 1 ✓, bits[42] = 1 ✓ → "Probably yes"

Exists("world"):
  bits[7] = 1 ✓, bits[17] = 1 ✓, bits[55] = 0 ✗ → "Definitely no"
```

### The Math

The optimal parameters are derived from information theory (see [Wikipedia: Bloom filter](https://en.wikipedia.org/wiki/Bloom_filter)):

**Optimal number of bits per element:**
```
bitsPerEntry = -ln(errorRate) / ln(2)²
```

Where:
- `ln(2)  = 0.693147...` — Natural logarithm of 2
- `ln(2)² = 0.480453...` — Square of ln(2)

**Optimal number of hash functions:**
```
k = bitsPerEntry × ln(2)
```

These are the "magic numbers" `Ln2` and `Ln2Square` in our code. They ensure the filter achieves the minimum possible false positive rate for its size.

### Enhanced Double Hashing

Instead of computing k independent hash functions (expensive), we use the **Kirsch-Mitzenmacker optimization**: compute one 128-bit MD5 hash, split it into two 64-bit halves (A and B), then generate k hash values as:

```
hash_i = (A + B × i) mod bits
```

This has been proven to provide the same false positive guarantees as k independent hashes.

```csharp
public HashValue CalcHash(string entry)
{
    byte[] hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(entry));
    ulong a = BitConverter.ToUInt64(hashBytes, 0);  // First 64 bits
    ulong b = BitConverter.ToUInt64(hashBytes, 8);  // Last 64 bits
    return new HashValue { A = a, B = b };
}
```

### Redis Equivalence
Redis's Bloom Filter module (`RedisBloom`) uses the same optimal sizing formulas and the enhanced double hashing technique. Our implementation directly follows these algorithms.

### Complexity
| Operation | Time |
|---|---|
| Add | O(k) where k = number of hash functions |
| Exists | O(k) |
| Space | ~1.44 × ln(1/p) bits per element |

For a filter with 1% error rate: ~9.6 bits per element (~1.2 bytes).

---

## 6. Count-Min Sketch (CMS)

**File:** `src/Hyperion.DataStructures/CMS.cs`

### What It Does
A Count-Min Sketch estimates the **frequency** of items in a stream. Unlike a hash map, it uses sub-linear space and can handle massive cardinalities. The trade-off is that estimates may be **slightly over-counted** (never under-counted).

### How It Works

The CMS is a 2D array of counters with `width` columns and `depth` rows. Each row uses a different hash function.

```
                    width (w) columns
               ┌───┬───┬───┬───┬───┬───┐
  depth 0 (h₀) │ 0 │ 3 │ 0 │ 1 │ 0 │ 2 │
               ├───┼───┼───┼───┼───┼───┤
  depth 1 (h₁) │ 1 │ 0 │ 2 │ 0 │ 3 │ 0 │
               ├───┼───┼───┼───┼───┼───┤
  depth 2 (h₂) │ 0 │ 0 │ 1 │ 3 │ 0 │ 0 │
               └───┴───┴───┴───┴───┴───┘
```

**IncrBy(item, value):**
For each row `i`, compute `hash_i(item) % width` to find the column. Increment that cell by `value`.

**Count(item):**
For each row `i`, compute `hash_i(item) % width`. Return the **minimum** across all rows. The minimum is the best estimate because it's least likely to be inflated by hash collisions.

### The Math

Given desired error rate (`errRate`) and error probability (`errProb`):

**Width** (controls accuracy):
```
w = ceil(2 / errRate)
```

**Depth** (controls confidence):
```
d = ceil(log₁₀(errProb) / log₁₀(0.5))
```

Where `log₁₀(0.5) = -0.30103...` is the precomputed constant `Log10PointFive` in our code.

**Intuition**: Each additional row halves the probability of a false over-count. So `d = 7` means the probability of a bad estimate is at most `0.5^7 ≈ 0.8%`.

### Hash Functions
We simulate `d` independent hash functions by concatenating the item with the row index and computing MD5:

```csharp
private uint CalcHash(string item, uint seed)
{
    byte[] inputBytes = Encoding.UTF8.GetBytes(item + seed.ToString());
    byte[] hashBytes = MD5.HashData(inputBytes);
    return BitConverter.ToUInt32(hashBytes, 0);
}
```

This is simpler than the Bloom Filter's double-hashing approach because each CMS row only needs one hash value (not k).

### Redis Equivalence
Redis's CMS module (`RedisBloom/CMS`) uses the same width/depth formulas and the minimum-of-rows query strategy. The space-efficiency trade-off is identical.

### Complexity
| Operation | Time | Space |
|---|---|---|
| IncrBy | O(d) | — |
| Count | O(d) | — |
| Total space | — | O(w × d) counters |

For a sketch with 0.1% error rate and 1% error probability: w=2000, d=7 → 14,000 uint32 counters = ~56 KB.

---

## Summary Table

| Structure | Purpose | Space | Key Operation | Complexity |
|---|---|---|---|---|
| **Dict** | Key-value store with TTL | O(N) | GET/SET | O(1) |
| **Eviction Pool** | Approximate LRU | O(1) fixed | Evict oldest | O(pool_size) |
| **Skip List** | Sorted element storage | O(N) | Insert/Rank | O(log N) |
| **ZSet** | Sorted Set (Dict + SkipList) | O(N) | ZADD/ZRANK | O(log N) |
| **Bloom Filter** | Membership testing | ~1.2 B/element | Add/Exists | O(k) |
| **CMS** | Frequency estimation | O(w × d) | IncrBy/Count | O(d) |

All implementations are written from scratch without third-party dependencies, following the algorithms and design patterns established in the original Redis source code.
