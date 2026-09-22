# ADR-004: L1/L2 TTL Separation and Graceful Degradation in HybridCacheProvider

> **Sequence:** ← [ADR-003: Distributed Stampede Protection](./adr-003-distributed-stampede-protection.md) | **Next:** [ADR-005: Tag-Based Invalidation Strategy](./adr-005-tag-based-invalidation.md) →

## Status
Accepted (2026-09-03)

## Date
2026-09-03

## Context
A two-tier caching architecture combines the sub-microsecond latency of an in-memory L1 cache with the cluster-wide consistency and massive capacity of a distributed L2 cache (Redis). However, operating both tiers with identical eviction parameters causes significant architectural drawbacks:

1. **RAM Bloat vs. Network Overhead**: In-memory L1 cache consumes expensive application process RAM. Keeping items in L1 for the same duration as L2 (e.g., 2 hours) causes high memory pressure and GC pauses. Conversely, setting a short global TTL (e.g., 30 seconds) invalidates L2 prematurely, forcing unnecessary database recalculations across the entire cluster.
2. **Fragility Under L2 Outages**: If Redis experiences transient network spikes, failovers, or connection timeouts, an unhardened hybrid cache fails the user request immediately, ignoring the fact that the application node has memory and CPU available to compute and serve the value locally.

## Decision
We implement independent TTL tiering and graceful degradation inside `HybridCacheProvider`.

### 1. Distinct Tiered Options (`HybridCacheEntryOptions`)
We introduce `HybridCacheEntryOptions` inheriting from `CacheEntryOptions`:
- `LocalCacheDuration`: Defines the time-to-live in the local L1 memory cache (typically short, e.g., 1 to 5 minutes).
- `DistributedCacheDuration`: Defines the time-to-live in the distributed L2 Redis cache (typically long, e.g., 1 hour to 24 hours).
- When a standard `CacheEntryOptions` is provided, both tiers adopt the same duration, ensuring 100% backward compatibility.

### 2. Resilient Graceful Degradation
`HybridCacheProvider` enforces failure resilience across all operational paths:
- **`GetAsync`**: If L1 misses and L2 returns an error (`RedisException` or `Result.Failure`), the failure is recorded to metrics (`cache.errors`), and `GetAsync` returns `Result.Success(default)`. This allows the application pipeline to proceed normally.
- **`GetOrCreateAsync`**: If L2 re-check or write fails inside the factory pipeline, the exception is caught, telemetry is recorded, and the calculated factory value is stored in L1 and returned to the caller as `Result.Success(value)`. An outage in Redis will never crash user transactions.
- **`SetAsync`**: If L1 write succeeds, `SetAsync` returns `Result.Success(true)` even if L2 write encounters a transient network exception.

## Consequences

### Positive
- **Optimal Memory Footprint**: Application processes maintain compact L1 memory pools with rapid turnover while offloading long-term data retention to Redis.
- **High Availability**: Application pods remain fully functional during Redis maintenance, restarts, or network partitions.
- **Drop-in Compatibility**: Existing callers using `CacheEntryOptions` continue to work without code changes.

### Negative
- **Temporary Cross-Node Inconsistency**: During an L2 outage, pods will rely solely on local L1 and may compute independent values until Redis connectivity is restored.

---
*Navigation: ← [ADR-003: Distributed Stampede Protection](./adr-003-distributed-stampede-protection.md) | [ADR Index](./README.md) | Next: [ADR-005: Tag-Based Invalidation Strategy](./adr-005-tag-based-invalidation.md) →*
