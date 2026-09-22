# ADR-001: Cache Architecture & Functional Invariants

> **Sequence:** `Start (Genesis)` | **Next:** [ADR-002: Package Existence Justification](./adr-002-package-existence-justification.md) →

## Status
Accepted (2026-09-03)

## Date
2026-09-03

## Context
High-performance APIs and data-intensive applications require caching to reduce latency and database load. Standard `.NET` caching implementations (`IMemoryCache`, `IDistributedCache`) rely heavily on throwing exceptions during transient failures, return nulls ambiguously, and do not provide robust protection against "Cache Stampedes" (thundering herd problem) out-of-the-box. Furthermore, ad-hoc caching implementations often leak memory or cause excessive GC pressure.

We need a standardized caching abstraction (`EricksonLopez.Caching`) that adheres to our functional `Result<T>` paradigm and protects backend resources inherently.

## Decision
We establish `EricksonLopez.Caching` as the foundational caching abstraction for the repository.

1. **Functional Responses**: All cache operations MUST return `Result<T>` or `Result<bool>` (from `EricksonLopez.Result`). Exceptions must never be thrown for cache misses or transient caching backend failures (e.g., Redis timeouts, socket disconnects).
2. **Stampede Protection (Dual Scope)**:
   - **In-Process Single-Flight**: Implemented in `MemoryCacheProvider` via keyed `SemaphoreSlim` instances, guaranteeing that multiple concurrent threads hitting a cold local cache key execute the factory delegate exactly once.
   - **Distributed Single-Flight**: Implemented in `RedisCacheProvider` via Redis atomic `SETNX` locking with lease timeouts (`LockTimeout`) and token-validated non-blocking Lua scripts for unlock (detailed in [ADR-003](./adr-003-distributed-stampede-protection.md)). Concurrent cluster instances spin-wait with exponential/fixed interval re-checks, preventing database query storms across all distributed nodes.
3. **Provider Agnosticism**: The core contract `ICacheProvider` remains agnostic to the underlying storage (Memory, Redis, Memcached) and serialization formats.
4. **Zero-Allocation Structured Logging**: Implementations MUST avoid boxing and utilize source-generated `LoggerMessage` definitions (via `[LoggerMessage]` attribute) for zero string-interpolation-allocation log messages. Note that `System.Diagnostics.Metrics` counters (OpenTelemetry observability) incur minimal `KeyValuePair<string, object?>` allocations per metric call — these are accepted and necessary for telemetry correctness.
5. **Dynamic AOP Exclusion (Native AOT Invariant)**: Runtime proxy generation and dynamic bytecode weaving (e.g., Castle DynamicProxy, AspectCore) are permanently prohibited. Caching decorations must be applied via compile-time source generation or explicit DI composition to guarantee 100% Native AOT trimming compatibility.
6. **Precondition Safety (Non-Destructive Invariant)**: Invalidation methods (`RemoveByPrefixAsync`, `RemoveByTagAsync`, `InvalidateByPrefixAsync`, `InvalidateByTagAsync`) strictly validate that prefixes and tags cannot be `null`, empty, or white space (`ArgumentException.ThrowIfNullOrWhiteSpace`). An empty prefix is never allowed to evaluate to a wildcard wipeout of the cache keyspace.

## Consequences

### Positive
- **Backend Protection**: Dual-scope stampede protection safeguards backend databases (e.g. PostgreSQL) from connection pool exhaustion and query storms during traffic spikes and cold restarts.
- **Predictable Control Flow**: Using `Result<T>` forces consumers to explicitly handle cache unavailability without relying on expensive `try/catch` blocks.
- **Keyspace Security**: Strict empty-prefix precondition guards prevent inadvertent complete cache deletions in multi-tenant environments.
- **Unified API**: Seamlessly swap between Memory, Redis, and Hybrid implementations without changing consumer business logic.

### Negative
- **Lock Management Overhead**: Distributed locking incurs minimal Redis round-trips for cold keys, which is offset by the total prevention of expensive database queries.

## Compliance
- All application layers requiring caching must depend on `ICacheProvider` or `ITaggedCacheProvider`. Direct usage of raw `IMemoryCache` or `IDistributedCache` in application layers is prohibited.

---
*Navigation: [ADR Index](./README.md) | Next: [ADR-002: Package Existence Justification](./adr-002-package-existence-justification.md) →*
