# Hyperion Performance Optimization Plan

## Goal
Match or exceed Nietzsche (Go) performance: **>100k RPS** for both single-thread and multi-thread modes on Linux.

## Root Causes Found

### 1. [CRITICAL] Global `SemaphoreSlim` in `SingleThreadServer` — Single-Thread Killer
**File:** `src/Hyperion.Server/SingleThreadServer.cs`

The original implementation spawned one `Task.Run` per TCP connection, with all connections competing on a single `SemaphoreSlim(1,1)` for every command. On Linux, a contended `SemaphoreSlim` goes through kernel futex syscalls, costing ~1–5 µs per acquisition under contention. At 50k RPS with 50 connections that's millions of futex calls/sec.

**Fix:** True single event-loop — one `Channel<WorkItem>` fed by all connections, consumed by exactly one `Task.Factory.StartNew(..., TaskCreationOptions.LongRunning)` which pins to a dedicated OS thread (pthread on Linux). No lock anywhere. Responses returned via `TaskCompletionSource<byte[]>` back to the awaiting connection handler. This exactly mirrors Nietzsche's single goroutine loop.

### 2. [HIGH] `ConcurrentDictionary` in private worker storage
**Files:** `src/Hyperion.DataStructures/Dict.cs`, `src/Hyperion.Core/Storage.cs`

Every worker owns private storage via the share-nothing model — a key always routes to the same worker (FNV hash % numWorkers), so there is ZERO cross-thread access to storage. Yet `ConcurrentDictionary` was used everywhere, paying striped-lock overhead, volatile reads, and memory barriers on every single Get/Set.

**Fix:** Replace all `ConcurrentDictionary<K,V>` in `Storage` and `Dict` with plain `Dictionary<K,V>`. Thread safety is guaranteed by the routing layer, not the data structure.

### 3. [MEDIUM] `DateTimeOffset.UtcNow` syscall on every GET
**File:** `src/Hyperion.DataStructures/Dict.cs`

`obj.LastAccessTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` was called inside every `Get()`. At 100k RPS this is 100k OS clock queries/sec just for LRU tracking.

**Fix:** Cache a coarse timestamp refreshed by a `System.Threading.Timer` every 10ms (Redis's approach). All hot-path operations read the cached value — no syscall.

### 4. [MEDIUM] `Encoding.UTF8.GetBytes(key)` heap allocation in routing hot path
**File:** `src/Hyperion.Server/HyperionServer.cs`

Every dispatched command allocated a new `byte[]` for FNV hashing the routing key. At 100k+ RPS this means 100k+ heap allocations/sec just for routing, adding GC pressure.

**Fix:** `stackalloc Span<byte>` (256 bytes, covers 99.9% of keys) — zero heap allocation per hash.

### 5. [ALREADY DONE] `RunContinuationsAsynchronously` on `TaskCompletionSource`
**File:** `src/Hyperion.Core/WorkerTask.cs`

Already correctly set. No change needed.

---

## Changes Made

| File | Change |
|---|---|
| `SingleThreadServer.cs` | Replaced `SemaphoreSlim` + per-connection `Task.Run` executor with single `Channel<WorkItem>` + `LongRunning` event loop |
| `Storage.cs` | All `ConcurrentDictionary` → `Dictionary` |
| `Dict.cs` | `ConcurrentDictionary` → `Dictionary`, added `CoarseClock` static class |
| `HyperionServer.cs` | `Fnv1aHash` uses `stackalloc Span<byte>` instead of `byte[]` heap alloc |
| `ActiveExpiry.cs` | Snapshot iteration for plain `Dictionary` (thread-safe for single-owner workers) |

---

## How to Build & Test on WSL/Linux

### Prerequisites
```bash
# Install .NET 9 SDK if not already
wget https://dot.net/v1/dotnet-install.sh
bash dotnet-install.sh --version latest
export PATH=$PATH:$HOME/.dotnet

# Install redis-benchmark (comes with redis-tools)
sudo apt-get install -y redis-tools
```

### Build
```bash
git clone https://github.com/ductai202/Hyperion.git
cd Hyperion
git checkout perf/optimize-single-and-multi-thread
dotnet build -c Release
```

### Benchmark Script
Run each server in one terminal, benchmark from another.

#### Terminal 1 — Redis (baseline)
```bash
redis-server --port 6379
```
#### Terminal 2 — Benchmark Redis
```bash
redis-benchmark -h 127.0.0.1 -p 6379 -t set,get -n 1000000 -c 50 -P 16 --csv
```

#### Terminal 1 — Hyperion Single-Thread
```bash
cd src/Hyperion.Server
dotnet run -c Release -- --mode single --port 3000 --log Warning
```
#### Terminal 2 — Benchmark Hyperion Single
```bash
redis-benchmark -h 127.0.0.1 -p 3000 -t set,get -n 1000000 -c 50 -P 16 --csv
```

#### Terminal 1 — Hyperion Multi-Thread
```bash
cd src/Hyperion.Server
dotnet run -c Release -- --mode multi --port 3000 --log Warning
```
#### Terminal 2 — Benchmark Hyperion Multi
```bash
# Adjust --workers and --io to your CPU core count
redis-benchmark -h 127.0.0.1 -p 3000 -t set,get -n 1000000 -c 50 -P 16 --csv
```

### Tips for Maximum RPS on WSL
- Use `-P 16` (pipeline 16 commands per request) — this is how Nietzsche reaches 100k+
- Use `-c 50` connections
- Run server and benchmark in the same WSL instance (loopback)
- Make sure you build with `-c Release` not Debug
- Pin Hyperion to specific cores if needed: `taskset -c 0-3 dotnet run -c Release ...`

### Expected Results (target)
| Server | GET RPS | SET RPS |
|---|---|---|
| Redis (baseline) | ~80–100k | ~80–100k |
| Hyperion Single (before) | <50k | <50k |
| Hyperion Single (after) | >100k | >100k |
| Hyperion Multi (after) | >100k | >100k |
