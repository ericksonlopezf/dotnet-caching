# ADR-002: Package Existence & Invariant Justification for EricksonLopez.Caching

> **Sequence:** ← [ADR-001: Cache Architecture](./adr-001-cache-architecture.md) | **Next:** [ADR-003: Distributed Stampede Protection](./adr-003-distributed-stampede-protection.md) →

## Status
Accepted (2026-09-03)

## Date
2026-09-03

## Context
Under EricksonLopez Design Principles 14 ("Every abstraction must justify its existence") and 15 ("Complexity must be paid for deliberately"), libraries in the ecosystem cannot exist merely as convenience wrappers around third-party or BCL features. They must defend critical architectural invariants that cannot be satisfied by standard primitives.

A formal evaluation was conducted to determine whether caching should remain an autonomous Tier 0 package (`EricksonLopez.Caching`) or be retired in favor of raw `Microsoft.Extensions.Caching` or .NET 9's `HybridCache`.

## Decision
We formally approve `EricksonLopez.Caching` as an autonomous foundational package within the EricksonLopez ecosystem.

Its existence is justified by the following non-negotiable invariants:
1. **Stampede Protection**: `ICacheProvider.GetOrCreateAsync` guarantees single-flight execution both in-process (keyed `SemaphoreSlim`, established in [ADR-001](./adr-001-cache-architecture.md)) and across distributed clusters (`SETNX` + Lua atomic release in Redis, established in [ADR-003](./adr-003-distributed-stampede-protection.md)), preventing the thundering herd problem against PostgreSQL under high concurrency.
2. **Railway-Oriented Functional Control**: All caching contracts return `Result<T>` and `Result<bool>` from `EricksonLopez.Result`, eliminating exception-based control flow (`RedisException`, `TimeoutException`) during transient network faults.
3. **Multi-Tenant Keyspace & Prefix Invalidation**: `ICacheInvalidator` and `RemoveByPrefixAsync` provide deterministic, tenant-isolated cache eviction without coupling application handlers to Redis physical keys.
4. **Resilient Multi-Tier Coordination & Graceful Degradation**: `HybridCacheProvider` coordinates L1 in-memory and L2 distributed caches with independent TTLs (`HybridCacheEntryOptions`, established in [ADR-004](./adr-004-l1-l2-ttl-separation.md)) and graceful degradation: if L2 Redis fails or times out, L1 continues serving cached entries or coordinates factory calculation without failing user requests.
5. **Strict Native AOT Compliance (Conditional on Serializer)**:
   - The core package (`EricksonLopez.Caching`) is 100% trim-safe, reflection-free, and verified with Native AOT analyzer gates enabled.
   - The distributed package (`EricksonLopez.Caching.Redis`) supports pluggable serialization via `ICacheSerializer`, enabling consumers to pass source-generated `JsonSerializerContext` instances for zero-reflection Native AOT compilation.
   - **Important**: `SystemTextJsonCacheSerializer.Default` uses `System.Text.Json` with dynamic reflection (suppressed via `[UnconditionalSuppressMessage]`) and is **not** AOT-safe by default. Native AOT consumers must replace it with a custom `SystemTextJsonCacheSerializer` instance configured with a `JsonSerializerContext`.
6. **Multi-Key Tagged Invalidation**: `ITaggedCacheProvider` provides multi-projection entity invalidation using non-blocking Lua scripts on Redis sets (`SMEMBERS` + `UNLINK`) and thread-safe concurrent sets in memory (established in [ADR-005](./adr-005-tag-based-invalidation.md)).
7. **In-Box Fault Tolerance (Self-Contained Retries)**: `RedisCacheProvider` executes internal retry loops governed by `MaxRetries` and `RetryDelay` for transient socket and network blips, avoiding heavyweight external resilience dependencies (e.g. Polly).

## Consequences

### Positive
- Prevents database connection pool exhaustion during traffic spikes across distributed nodes.
- Eliminates cross-tenant cache contamination risks with isolated prefixes and tag stores.
- Delivers exceptional high-throughput performance with zero-allocation logging and Native AOT runtime binaries.
- Provides a unified, exception-free programming model for all reading pipelines (Dapper queries, MediatR handlers, background workers).

### Negative
- Requires maintaining the adapter library (`EricksonLopez.Caching.Redis`) alongside the core abstractions.

---
*Navigation: ← [ADR-001: Cache Architecture](./adr-001-cache-architecture.md) | [ADR Index](./README.md) | Next: [ADR-003: Distributed Stampede Protection](./adr-003-distributed-stampede-protection.md) →*
