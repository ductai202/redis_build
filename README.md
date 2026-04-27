# Hyperion

A Redis-compatible in-memory database, built from scratch in C#/.NET 10.

I started this project to understand how Redis actually works under the hood — not just the API, but the internals: how it parses the wire protocol, how it manages key expiry, how a Skip List enables O(log N) ranked queries, and why Bloom Filters use `ln(2)²` in their sizing formula.

The multi-threaded mode is inspired by [DragonflyDB](https://www.dragonflydb.io/)'s share-nothing architecture — each worker thread owns a private data shard, so there are no locks on the hot path.

Hyperion speaks standard RESP2, so you can connect with any `redis-cli` or Redis client library.

---

## What's implemented

**Protocol** — Hand-written RESP2 parser, encoder, and decoder. Zero-copy parsing via `System.IO.Pipelines`.

**Data Structures** — All implemented from scratch, following the same algorithms used in Redis's source code:
- **Dict** — Key-value store with separate TTL tracking and LRU timestamps (mirrors Redis's `redisDb` with its dual-dictionary design)
- **Skip List** — Probabilistic sorted structure with span tracking for O(log N) rank queries (same as Redis's `zskiplist`)
- **ZSet** — Sorted Set using Dict + Skip List together (the classic Redis dual-structure trick)
- **Bloom Filter** — Probabilistic membership test with optimal sizing via Kirsch-Mitzenmacker double hashing
- **Count-Min Sketch** — Frequency estimation with configurable error/probability bounds
- **Eviction Pool** — Sampling-based approximate LRU (Redis's approach of sampling 5 keys instead of maintaining a full LRU list)

**Commands:**

| Category | Commands |
|---|---|
| Connection | `PING` |
| String | `SET`, `GET`, `DEL`, `TTL` |
| Set | `SADD`, `SREM`, `SISMEMBER`, `SMEMBERS` |
| Sorted Set | `ZADD`, `ZREM`, `ZSCORE`, `ZRANK`, `ZRANGE` |
| Bloom Filter | `BF.RESERVE`, `BF.MADD`, `BF.EXISTS` |
| Count-Min Sketch | `CMS.INITBYDIM`, `CMS.INITBYPROB`, `CMS.INCRBY`, `CMS.QUERY` |

**Server modes:**
- **Single-threaded** — Classic Redis-style. One thread handles all IO and execution.
- **Multi-threaded (share-nothing)** — N worker threads, each with its own storage shard. Keys are routed via FNV-1a hash. No locks needed.

**Key expiry** — Both lazy (check on access) and active (background sweep that samples and deletes expired keys).

---

## Architecture

### Single-thread mode

Straightforward — one `TcpListener`, one `CommandExecutor`, one `Storage`. All clients share the same thread. Simple and lock-free.

### Multi-thread mode

```
    Clients
      │
      ▼
  TCP Listener (round-robin assigns to IO handlers)
      │
  ┌───┴───┐
  ▼       ▼
IO H.1  IO H.2  ...    ← parse RESP, dispatch to router
  │       │
  ▼       ▼
  Key Router (FNV-1a)   ← hash(key) % N → worker ID
  │       │
  ▼       ▼
Worker 0  Worker 1  ... ← each owns private Storage shard
```

On a machine with 8 logical cores, Hyperion spins up 4 IO handlers + 4 workers. Each worker has its own `Storage`, `CommandExecutor`, and a `Channel<WorkerTask>` inbox. Since a given key always maps to the same worker, there's no contention.

---

## Getting started

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

```bash
git clone https://github.com/ductai202/Hyperion.git
cd Hyperion

# Multi-threaded mode (default)
dotnet run --project src/Hyperion.Server -- --port 3000

# Single-threaded mode
dotnet run --project src/Hyperion.Server -- --port 3000 --mode single
```

Then connect:

```bash
redis-cli -p 3000

127.0.0.1:3000> SET hello world
OK
127.0.0.1:3000> GET hello
"world"
127.0.0.1:3000> ZADD leaderboard 100 "alice" 200 "bob"
(integer) 2
127.0.0.1:3000> ZRANK leaderboard "alice"
(integer) 0
```

Run tests:

```bash
dotnet test
```

---

## Benchmark

Tested with `redis-benchmark` (500 clients, 1M requests, 1M unique keys) on an Intel i5-1135G7 (4C/8T), 40GB RAM, Windows 11.

| Mode | SET (req/s) | GET (req/s) |
|---|---|---|
| Redis (official) | 17,850 | 17,653 |
| Hyperion single-thread | 16,594 | 17,717 |
| Hyperion multi-thread | 16,909 | 16,148 |

Hyperion hits ~93-100% of Redis throughput on basic SET/GET. The multi-thread mode's real advantage shows under slow-command scenarios — when one command blocks, other workers keep processing.

Full details in [doc/Benchmark.md](doc/Benchmark.md).

---

## Documentation

- [Data Structures Deep Dive](doc/DataStructures.md) — how each structure works, the math behind it, and how it maps to the original Redis implementation
- [Benchmark Report](doc/Benchmark.md) — full methodology and results

---

## What's next

- [ ] More commands (HSET/HGET, LPUSH/LPOP, INCR/DECR, etc.)
- [ ] Redis Cluster protocol
- [ ] RDB persistence
