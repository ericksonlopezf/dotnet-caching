# System Functional Map

## Overall Architecture

```mermaid
graph TD
    subgraph "Contracts (Abstractions)"
        ICP["ICacheProvider<br/>(11 methods)"]
        ITCP["ITaggedCacheProvider<br/>(+3 tag methods)"]
        ICI["ICacheInvalidator<br/>(6 methods)"]
        ICS["ICacheSerializer<br/>(4 methods)"]
        ITCP -->|extends| ICP
    end

    subgraph "Core Implementations"
        MCP["MemoryCacheProvider<br/>(L1 - In-Process)"]
        RCP["RedisCacheProvider<br/>(L2 - Distributed)"]
        HCP["HybridCacheProvider<br/>(L1 + L2 coordinator)"]
        TCP["TenantPartitionedCacheProvider<br/>(Decorator - multi-tenant)"]
        CI["CacheInvalidator<br/>(ICacheInvalidator impl)"]
        SER["SystemTextJsonCacheSerializer<br/>(ICacheSerializer impl)"]
    end

    subgraph "Configuration"
        CEO["CacheEntryOptions<br/>(TTL, FailSafe, Tags)"]
        HCEO["HybridCacheEntryOptions<br/>(LocalTTL, DistributedTTL)"]
        MCO["MemoryCacheOptions<br/>(MaxCapacity, Compaction)"]
        RCO["RedisCacheOptions<br/>(Connection, Locks, Retry)"]
    end

    subgraph "Diagnostics"
        CD["CacheDiagnostics<br/>(OTel Meter + ActivitySource)"]
    end

    subgraph "DI Registration"
        ME["AddMemoryCacheProvider()"]
        RE["AddRedisCaching()"]
        HE["AddHybridCache()"]
        TE["AddTenantPartitionedCache()"]
        IE["AddCacheInvalidator()"]
    end

    MCP -->|implements| ICP
    MCP -->|implements| ITCP
    RCP -->|implements| ICP
    RCP -->|implements| ITCP
    HCP -->|implements| ICP
    HCP -->|implements| ITCP
    TCP -->|implements| ICP
    TCP -->|implements| ITCP
    CI -->|implements| ICI
    SER -->|implements| ICS
    HCP -->|L1 uses| MCP
    HCP -->|L2 uses| RCP
    TCP -->|decorates| ICP
    RCP -->|uses| SER
    MCP -->|uses| MCO
    RCP -->|uses| RCO
    CEO -->|configures| MCP
    CEO -->|configures| RCP
    HCEO -->|configures| HCP
    CD -->|instruments| MCP
    CD -->|instruments| RCP
    CD -->|instruments| HCP
    ME -->|registers| MCP
    RE -->|registers| RCP
    HE -->|registers| HCP
    TE -->|registers| TCP
    IE -->|registers| CI
```

---

## Primary Flow: GetOrCreateAsync (Stampede Protection)

```mermaid
sequenceDiagram
    participant App as Application
    participant MCP as MemoryCacheProvider
    participant Lock as SemaphoreSlim (per-key)
    participant Store as Internal ConcurrentDictionary
    participant Factory as User Factory Func
    participant Diag as CacheDiagnostics

    App->>MCP: GetOrCreateAsync("product:1", factory, opts)
    MCP->>Store: TryGetValue("product:1")
    alt Cache HIT (valid, not expired)
        Store-->>MCP: CacheEntry (value)
        MCP->>Diag: RecordHit("memory")
        MCP-->>App: Result.Success(value)
    else Cache MISS
        MCP->>Diag: RecordMiss("memory")
        MCP->>Lock: WaitAsync() [single-flight begins]
        alt Lock acquired (first caller)
            Lock-->>MCP: acquired
            MCP->>Store: TryGetValue again (double-check)
            alt Already populated by another caller
                Store-->>MCP: CacheEntry (value)
                MCP->>Diag: RecordStampedePrevented("memory")
                MCP-->>App: Result.Success(value)
            else Still empty — this caller populates
                MCP->>Factory: invoke(cancellationToken)
                alt Factory succeeds
                    Factory-->>MCP: T value
                    MCP->>Store: TryAdd(key, entry)
                    MCP-->>App: Result.Success(value)
                else Factory throws (no fail-safe)
                    Factory-->>MCP: throws Exception
                    MCP->>Diag: RecordError("memory", "FACTORY_FAILED")
                    MCP-->>App: Result.Failure("Cache.Memory.FactoryFailed")
                else Factory throws (fail-safe available)
                    Factory-->>MCP: throws Exception
                    MCP->>Store: TryGetStaleValue("product:1")
                    Store-->>MCP: stale CacheEntry (within MaxStale window)
                    MCP-->>App: Result.Success(staleValue) [degraded mode]
                end
            end
        else Lock NOT acquired (N-1 concurrent callers wait)
            MCP->>Diag: RecordStampedePrevented("memory")
            MCP->>Lock: WaitAsync() [blocked until first caller finishes]
            Lock-->>MCP: acquired (first caller done)
            MCP->>Store: TryGetValue("product:1")
            Store-->>MCP: CacheEntry (populated by first caller)
            MCP-->>App: Result.Success(value)
        end
        MCP->>Lock: Release()
    end
```

---

## Flow: HybridCacheProvider — GetAsync

```mermaid
sequenceDiagram
    participant App as Application
    participant HCP as HybridCacheProvider
    participant L1 as MemoryCacheProvider (L1)
    participant L2 as RedisCacheProvider (L2)

    App->>HCP: GetAsync("key")
    HCP->>L1: GetAsync("key")
    alt L1 HIT
        L1-->>HCP: Result.Success(value)
        HCP-->>App: Result.Success(value)
    else L1 MISS
        L1-->>HCP: Result.Success(null)
        HCP->>L2: GetAsync("key")
        alt L2 HIT
            L2-->>HCP: Result.Success(value)
            HCP->>L1: SetAsync("key", value, localTTL)
            HCP-->>App: Result.Success(value)
        else L2 MISS
            L2-->>HCP: Result.Success(null)
            HCP-->>App: Result.Success(null)
        else L2 Failure (failOpenOnRemoval=true)
            L2-->>HCP: Result.Failure(...)
            HCP-->>App: Result.Success(null) [fail-open]
        end
    end
```

---

## Flow: TenantPartitionedCacheProvider — Key Resolution

```mermaid
sequenceDiagram
    participant App as Application (Tenant A)
    participant TCP as TenantPartitionedCacheProvider
    participant Resolver as Func<string?> tenantResolver
    participant Inner as ICacheProvider

    App->>TCP: SetAsync("config", value)
    TCP->>Resolver: invoke()
    Resolver-->>TCP: "tenant-a"
    TCP->>Inner: SetAsync("tenant-a::config", value)
    Inner-->>TCP: Result.Success(true)
    TCP-->>App: Result.Success(true)

    Note over App,Inner: Tenant B calls same logical key

    App->>TCP: GetAsync("config") [Tenant B context]
    TCP->>Resolver: invoke()
    Resolver-->>TCP: "tenant-b"
    TCP->>Inner: GetAsync("tenant-b::config")
    Inner-->>TCP: Result.Success(null) [isolated!]
    TCP-->>App: Result.Success(null)
```

---

## Flow: InvalidateByTagAsync

```mermaid
sequenceDiagram
    participant App as Application
    participant CI as CacheInvalidator
    participant ICP as ICacheProvider
    participant ITCP as ITaggedCacheProvider

    App->>CI: InvalidateByTagAsync("catalog")
    CI->>ICP: is ITaggedCacheProvider?
    alt Supports tags
        ICP-->>CI: yes
        CI->>ITCP: RemoveByTagAsync("catalog")
        ITCP-->>CI: Result.Success(keysRemoved)
        CI-->>App: Result.Success(keysRemoved)
    else Does not support tags
        ICP-->>CI: no
        CI-->>App: Result.Failure("Cache.TagsNotSupported")
    end
```

---

## Flow: RedisCacheProvider — Distributed Stampede Protection

```mermaid
sequenceDiagram
    participant App as Application (Instance 1)
    participant RCP as RedisCacheProvider
    participant Redis as Redis Server
    participant Lua as Lua Script (atomic)
    participant Factory as User Factory

    App->>RCP: GetOrCreateAsync("product:1", factory)
    RCP->>Redis: GET prefix:product:1
    alt Redis HIT
        Redis-->>RCP: serialized value
        RCP-->>App: Result.Success(value)
    else Redis MISS
        RCP->>Lua: ACQUIRE_LOCK(prefix:product:1:lock, TTL=lockTimeout)
        alt Lock acquired
            Lua-->>RCP: lock token
            RCP->>Factory: invoke(ct)
            Factory-->>RCP: value
            RCP->>Redis: SET prefix:product:1 value EX ttl
            RCP->>Redis: RELEASE_LOCK(prefix:product:1:lock, token)
            RCP-->>App: Result.Success(value)
        else Lock not acquired (another instance holds it)
            Lua-->>RCP: nil
            RCP->>RCP: Wait(lockRetryInterval)
            RCP->>Redis: GET prefix:product:1 [retry read]
            alt Populated by lock holder
                Redis-->>RCP: serialized value
                RCP-->>App: Result.Success(value)
            else Still empty — retry lock
                Note over RCP: retry up to maxRetries
            end
        end
    end
```

---

## State Diagram: CacheEntry

```mermaid
stateDiagram-v2
    [*] --> Valid : SetAsync / GetOrCreateAsync
    Valid --> Sliding : RefreshAsync
    Sliding --> Valid : TTL reset
    Valid --> Stale : AbsoluteExpiration passed
    Stale --> Valid : GetOrCreateAsync (factory success)
    Stale --> Stale_Serving : GetOrCreateAsync (factory fails + FailSafe active)
    Stale_Serving --> [*] : FailSafeMaxStale expired
    Valid --> [*] : RemoveAsync / RemoveByPrefixAsync / RemoveByTagAsync
    Valid --> [*] : ExpireAsync (new shorter TTL applied)
    Valid --> [*] : RemoveExpiredEntries sweep
```

---

## Component Dependency Diagram

```mermaid
graph LR
    subgraph "EricksonLopez.Caching"
        MCP --> ICacheProvider
        MCP --> ITaggedCacheProvider
        HCP --> ICacheProvider
        TCP --> ICacheProvider
        CI --> ICacheInvalidator
        SER["SystemTextJsonCacheSerializer"] --> ICacheSerializer
        MCP --> MemoryCacheOptions
        MCP --> CacheEntryOptions
        HCP --> HybridCacheEntryOptions
    end
    subgraph "EricksonLopez.Caching.Redis"
        RCP["RedisCacheProvider"] --> ICacheProvider
        RCP --> ITaggedCacheProvider
        RCP --> ICacheSerializer
        RCP --> RedisCacheOptions
    end
    subgraph "EricksonLopez.Result"
        Result["Result&lt;T&gt;"]
    end
    ICacheProvider --> Result
    ICacheInvalidator --> Result
```

---

## Eviction Reasons (MemoryCacheProvider)

```mermaid
graph TD
    Eviction([Eviction triggered])
    Eviction --> R1[expired — GetAsync lazy check]
    Eviction --> R2[capacity_compaction — MaxCapacity reached]
    Eviction --> R3[prefix — RemoveByPrefixAsync]
    Eviction --> R4[tag — RemoveByTagAsync]
    Eviction --> R5[explicit — RemoveAsync]
    Eviction --> R6[explicit_bulk — RemoveManyAsync]
    Eviction --> R7[expired_sweep — RemoveExpiredEntries background]
    R1 --> CD[CacheDiagnostics.RecordEviction]
    R2 --> CD
    R3 --> CD
    R4 --> CD
    R5 --> CD
    R6 --> CD
    R7 --> CD
```
