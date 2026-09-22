# Architectural Justification: EricksonLopez.Caching

## 1. Executive Summary & Context

Within an enterprise distributed architecture, caching is frequently treated as an opportunistic performance tweak rather than a mission-critical infrastructure subsystem. Developers routinely scatter ad-hoc calls to `IMemoryCache` or `IDistributedCache` across controllers, services, and repositories.

`EricksonLopez.Caching` exists to elevate caching into a **first-class architectural component governed by strict domain invariants**.

This document formally justifies the existence of `EricksonLopez.Caching` as an autonomous Tier 0 foundational package in accordance with **EricksonLopez Design Principles**:
- **Principle 14**: *Every abstraction must justify its existence.*
- **Principle 15**: *Complexity must be paid for deliberately.*
- **Invariant-First Doctrine**: A package must not be conceptualized around features ("we have a caching library"), but around invariants: *"A cache lookup must protect backend persistence from stampedes, isolate multi-tenant keyspaces, degrade gracefully without throwing exceptions, and support deterministic event-driven invalidation."*

---

## 2. Core Problem Space & Failure Modes of Standard Caching

Standard .NET caching abstractions (`Microsoft.Extensions.Caching.Memory`, `Microsoft.Extensions.Caching.Distributed`, and direct `StackExchange.Redis` usage) introduce severe architectural and operational risks in enterprise multi-tenant systems:

```
┌────────────────────────────────────────────────────────────────────────┐
│                        STANDARD CACHING VULNERABILITIES                │
├───────────────────────────────┬────────────────────────────────────────┤
│ Failure Mode                  │ Production Consequence                 │
├───────────────────────────────┼────────────────────────────────────────┤
│ 1. Cache Stampede             │ When a popular key expires, hundreds of│
│    (Thundering Herd)          │ concurrent requests miss the cache and │
│                               │ hammer PostgreSQL simultaneously,      │
│                               │ causing pool exhaustion and outages.   │
├───────────────────────────────┼────────────────────────────────────────┤
│ 2. Exception-Driven Flow      │ Redis network blips or timeouts throw  │
│    (Brittle Operations)       │ unhandled exceptions, breaking user    │
│                               │ requests rather than degrading to DB.  │
├───────────────────────────────┼────────────────────────────────────────┤
│ 3. Cross-Tenant Leakage       │ Lack of mandatory tenant keyspace      │
│    (Security Violation)       │ scoping allows key collision or leakage│
│                               │ between isolated business tenants.     │
├───────────────────────────────┼────────────────────────────────────────┤
│ 4. Ad-Hoc Invalidation        │ Write handlers cannot invalidate sets  │
│    (Stale / Corrupted Data)   │ of related keys without manual string  │
│                               │ manipulation and leaked Redis details. │
├───────────────────────────────┼────────────────────────────────────────┤
│ 5. Memory Pressure & Boxing   │ Unoptimized in-memory caches hold onto │
│    (GC Allocation Spikes)     │ expired objects or force allocations   │
│                               │ through boxing and dynamic reflection. │
└───────────────────────────────┴────────────────────────────────────────┘
```

---

## 3. Fundamental Architectural Invariants

`EricksonLopez.Caching` enforces five non-negotiable architectural invariants:

### Invariant 1: Stampede-Proof Atomic Execution (`GetOrCreateAsync`)
> *Under concurrent cache misses for the same key, the underlying factory evaluation MUST execute exactly once.*

Standard caches allow every incoming thread to execute the database query during a cache miss. `EricksonLopez.Caching` coordinates execution via fine-grained per-key synchronization (`SemaphoreSlim` in memory, or distributed coordination), ensuring that:
1. Thread A acquires the single-flight gate and executes the factory.
2. Threads B, C, and D await the gate and read the value populated by Thread A.
3. PostgreSQL connection pools are shielded from instantaneous saturation spikes.

### Invariant 2: Railway-Oriented Resilience via `Result<T>`
> *Cache unavailability or backend network partitions MUST NOT throw exceptions or interrupt business transaction flows.*

All methods on `ICacheProvider` return `Task<Result<T>>` or `Task<Result<bool>>` using `EricksonLopez.Result`. 
- A cache miss returns `Result<T?>.Success(null)`.
- A Redis timeout or connection failure returns an explicit `Error.Failure` or gracefully falls back to L1 local memory cache.
- Callers handle cache status with functional predictability, eliminating `try/catch (RedisException)` boilerplate.

### Invariant 3: Multi-Tenant Keyspace Isolation
> *Cache keys MUST support deterministic tenant prefixing to prevent cross-tenant cache pollution or data disclosure.*

In a multi-company, multi-tenant platform (such as OpusHydra), cached entities must never share un-scoped keys. `EricksonLopez.Caching` provides prefix-based keyspacing and batch invalidation primitives (`RemoveByPrefixAsync`, `InvalidateByPrefixAsync`) so that tenant data eviction is instantaneous, deterministic, and isolated.

### Invariant 4: Decoupled Event-Driven Invalidation (`ICacheInvalidator`)
> *Write operations and command handlers MUST NOT couple directly to physical cache key formats or storage engines.*

Domain commands mutate state (e.g., `UpdateProductPriceCommand`). Handlers publish domain events or integration events. `ICacheInvalidator` acts as the clean port through which event consumers evict cached projection sets (e.g., `catalog:products:tenant_123:*`) without knowing whether the cache is stored in memory, Redis, or a multi-tier hybrid substrate.

### Invariant 5: Zero-Allocation & Native AOT Compatibility
> *Cache serialization and provider interfaces MUST NOT rely on dynamic reflection or unconstrained runtime type emitting.*

All serialization pathways are compatible with `System.Text.Json` source generation, Native AOT compilation, and strict trimming (`EnableTrimAnalyzer=true`, `TreatWarningsAsErrors=true`). When using `EricksonLopez.Caching.Redis`, consumers targeting Native AOT must configure a `JsonSerializerContext`-backed `ICacheSerializer`; the built-in `SystemTextJsonCacheSerializer.Default` uses dynamic reflection (suppressed via `[UnconditionalSuppressMessage]`) and is not AOT-safe by default.

---

## 4. Component Topology & Layer Allocation

`EricksonLopez.Caching` is strictly layered across the ecosystem:

```
┌─────────────────────────────────────────────────────────────┐
│                   Consumer Application                      │
│         (OpusHydra / Minimal APIs / Dapper Repos)           │
└──────────────────────────────┬──────────────────────────────┘
                               │ depends on
                               ▼
┌─────────────────────────────────────────────────────────────┐
│                 EricksonLopez.Caching (Core)                │
│  Contracts:                                                 │
│    - ICacheProvider                                         │
│    - ICacheInvalidator                                      │
│  Implementations:                                           │
│    - MemoryCacheProvider (L1 fast in-memory)                │
│    - HybridCacheProvider (L1 + L2 coordination)             │
│    - CacheEntryOptions, CacheInvalidator                    │
└──────────────────────────────┬──────────────────────────────┘
                               │ implements L2 adapter
                               ▼
┌─────────────────────────────────────────────────────────────┐
│              EricksonLopez.Caching.Redis (Adapter)          │
│  Implementations:                                           │
│    - RedisCacheProvider (StackExchange.Redis integration)   │
│    - RedisCacheOptions, CacheErrors                         │
└─────────────────────────────────────────────────────────────┘
```

### Layer Permitted Matrix

| Layer / Assembly | Permitted Responsibilities | Strictly Prohibited |
|---|---|---|
| **`EricksonLopez.Caching`** | Core interfaces (`ICacheProvider`, `ICacheInvalidator`), in-memory provider, hybrid coordination, options, `Result<T>` integration | References to `StackExchange.Redis`, Npgsql, Dapper, ASP.NET Core HTTP context |
| **`EricksonLopez.Caching.Redis`** | Redis connection multiplexing, Redis string/hash operations, Redis-specific error mapping | Business domain rules, direct HTTP dependencies, direct SQL persistence |

---

## 5. Architectural Comparison & Decision Matrix

| Dimension | Raw `IMemoryCache` / `IDistributedCache` | .NET 9 `HybridCache` | **`EricksonLopez.Caching`** |
|---|---|---|---|
| **Return Type Paradigm** | Nullable references / throws exceptions | Throws exceptions / Nullables | **`Result<T>` Functional Flow** |
| **Stampede Protection** | None (manual locking required) | Built-in (keyed `TaskCompletionSource`) | **Built-in (`SemaphoreSlim` / single-flight)** |
| **Prefix & Tag Invalidation** | Not supported natively | Tag-based (limited prefix eviction) | **Native `RemoveByPrefixAsync` & `ICacheInvalidator`** |
| **Multi-Tenant Keyspacing** | Manual ad-hoc concatenation | Manual string formatting | **First-class prefix contract & tenant scoping** |
| **L1/L2 Tier Coordination** | None (requires third-party library) | Built-in | **`HybridCacheProvider` with explicit Result fallback** |
| **Native AOT & Trimming** | Partial | Yes | **Core 100% AOT; Redis serialization requires `JsonSerializerContext` for strict AOT** |
| **Ecosystem Alignment** | No integration with `EricksonLopez.*` | Isolated Microsoft abstraction | **Direct integration with `Result` & SharedKernel** |

---

## 6. Threat Model & Security Implications

1. **Cross-Tenant Information Disclosure**:
   - *Threat*: A query for Tenant A retrieves a cached object belonging to Tenant B due to ambiguous key generation (`key = $"product:{id}"`).
   - *Mitigation*: Mandatory tenant prefixing (`tenant:{tenantId}:product:{id}`) and batch invalidation by prefix.
2. **Denial of Service via Cache Stampede**:
   - *Threat*: High-frequency endpoint experiences key expiration during peak traffic, triggering hundreds of simultaneous database queries that exhaust connection pools.
   - *Mitigation*: Single-flight locking inside `GetOrCreateAsync` guarantees that only 1 thread evaluates the factory while others await the result.
3. **Cascading Failure from Redis Outage**:
   - *Threat*: Network split between app server and Redis cluster throws `RedisTimeoutException`, bringing down entire web application.
   - *Mitigation*: `HybridCacheProvider` continues serving L1 cached data; distributed cache failures return functional errors allowing graceful database fallback without application crashes.

---

## 7. Conclusion & Decision Records

`EricksonLopez.Caching` satisfies all criteria of Principles 14 and 15. It replaces uncoordinated, exception-prone caching code with an explicit, resilient, stampede-protected, and multi-tenant-safe subsystem.

### Related Architecture Decision Records
- [ADR-001: Cache Architecture & Functional Invariants](./adr/adr-001-cache-architecture.md)
- [ADR-002: Package Existence & Invariant Justification](./adr/adr-002-package-existence-justification.md)
- [ADR-003: Distributed Stampede Protection Strategy](./adr/adr-003-distributed-stampede-protection.md)
- [ADR-004: L1/L2 TTL Separation and Graceful Degradation](./adr/adr-004-l1-l2-ttl-separation.md)
- [ADR-005: Tag-Based Invalidation Strategy](./adr/adr-005-tag-based-invalidation.md)
- [ADR-006: Exclusion of Redis Pub/Sub Backplane from Core](./adr/adr-006-backplane-excluded-from-core.md)

---
*For the comprehensive sequential guide, refer to the [Architecture and Sequential Reading Roadmap](./README.md).*
