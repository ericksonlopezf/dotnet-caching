# PHASE 1 — Comprehensive Public API Inventory

> **Source of Truth:** `EricksonLopez.Caching` + `EricksonLopez.Caching.Redis`  
> Extracted directly from production source code contracts.

---

## Core Library — `EricksonLopez.Caching`

### Interfaces

| Name | Namespace | Responsibility | Dependencies | Use Cases | Complexity | In Showcase |
|---|---|---|---|---|---|---|
| `ICacheProvider` | `EricksonLopez.Caching` | Core cache contract with 11 methods: Get, GetOrCreate, Set, Remove, RemoveByPrefix, Exists, GetMany, SetMany, RemoveMany, Expire, Refresh | `EricksonLopez.Result` | Primary cache interface across applications | Basic | ✅ L1, L2 |
| `ITaggedCacheProvider` | `EricksonLopez.Caching` | Extends `ICacheProvider` with `SetWithTagsAsync`, `RemoveByTagAsync`, `RemoveByTagsAsync` for semantic tag invalidation | `ICacheProvider` | Projection invalidation, CQRS models, domain clusters | Intermediate | ✅ L2, L3, L4 |
| `ICacheInvalidator` | `EricksonLopez.Caching` | Dedicated invalidation contract: 6 methods (by key, multiple keys, prefix, multiple prefixes, tag, multiple tags) | `ICacheProvider` | CQRS Command handlers, domain event listeners | Intermediate | ✅ L3, L6 |
| `ICacheSerializer` | `EricksonLopez.Caching` | Serialization contract with 4 methods: Serialize/Deserialize strings and UTF-8 byte spans | None | Redis serialization, custom encryption/compression | Intermediate | ✅ L8 |

---

### Concrete Classes — Providers

| Name | Namespace | Responsibility | Dependencies | Use Cases | Complexity | In Showcase |
|---|---|---|---|---|---|---|
| `MemoryCacheProvider` | `EricksonLopez.Caching` | L1 in-process provider. Single-flight stampede protection via `ConcurrentDictionary` + keyed `SemaphoreSlim`. Sliding/absolute TTL, capacity compaction, background sweep | `MemoryCacheOptions`, `TimeProvider` | In-memory cache, single-instance services | Basic | ✅ L1–L7 |
| `HybridCacheProvider` | `EricksonLopez.Caching` | L1+L2 coordinator. Reads L1 first, falls back to L2. Writes to both tiers. Graceful degradation if L2 fails | `ICacheProvider` (L1), `ICacheProvider` (L2) | Multi-tier, high-concurrency distributed apps | Advanced | ✅ L4 |
| `TenantPartitionedCacheProvider` | `EricksonLopez.Caching` | Decorator enforcing automatic key and tag partitioning per tenant | `ICacheProvider`, `Func<string?>` tenantIdResolver | Multi-tenant SaaS, tenant data isolation | Advanced | ✅ L4 |
| `CacheInvalidator` | `EricksonLopez.Caching` | Canonical implementation of `ICacheInvalidator` | `ICacheProvider` | Command handlers, event handlers | Intermediate | ✅ L3, L6 |
| `SystemTextJsonCacheSerializer` | `EricksonLopez.Caching` | Default serializer based on `System.Text.Json`. Provides static `Default` instance. Compatible with Native AOT via `JsonSerializerOptions` overloads | `JsonSerializerOptions` (optional) | Automated JSON payload serialization in Redis | Basic | ✅ L8 |

---

### MemoryCacheProvider Constructors

| Overload | Parameters | In Showcase |
|---|---|---|
| `MemoryCacheProvider()` | Parameterless (uses System clock & default options) | ✅ L5 |
| `MemoryCacheProvider(TimeProvider)` | `TimeProvider timeProvider` | ✅ L5 |
| `MemoryCacheProvider(MemoryCacheOptions, TimeProvider?)` | `MemoryCacheOptions options, TimeProvider? timeProvider = null` | ✅ L5 |
| `MemoryCacheProvider(IOptions<MemoryCacheOptions>, TimeProvider?)` | `IOptions<MemoryCacheOptions> options, TimeProvider? timeProvider = null` | ✅ L5 |

---

### Methods of ICacheProvider

| Method | Signature | Return | In Showcase |
|---|---|---|---|
| `GetAsync<T>` | `(string key, CancellationToken ct = default)` | `Task<Result<T?>>` | ✅ L1, L2 |
| `GetOrCreateAsync<T>` | `(string key, Func<CancellationToken, Task<T>> factory, CacheEntryOptions? options, CancellationToken ct)` | `Task<Result<T>>` | ✅ L1–L5 |
| `SetAsync<T>` | `(string key, T value, CacheEntryOptions? options, CancellationToken ct)` | `Task<Result<bool>>` | ✅ L1, L2 |
| `RemoveAsync` | `(string key, CancellationToken ct)` | `Task<Result<bool>>` | ✅ L1, L2 |
| `RemoveByPrefixAsync` | `(string prefix, CancellationToken ct)` | `Task<Result<long>>` | ✅ L2, L3 |
| `ExistsAsync` | `(string key, CancellationToken ct)` | `Task<Result<bool>>` | ✅ L2, L4 |
| `GetManyAsync<T>` | `(IEnumerable<string> keys, CancellationToken ct)` | `Task<Result<IReadOnlyDictionary<string, T>>>` | ✅ L2, L7 |
| `SetManyAsync<T>` | `(IEnumerable<KeyValuePair<string, T>> items, CacheEntryOptions? options, CancellationToken ct)` | `Task<Result<bool>>` | ✅ L2, L7 |
| `RemoveManyAsync` | `(IEnumerable<string> keys, CancellationToken ct)` | `Task<Result<long>>` | ✅ L2, L7 |
| `ExpireAsync` | `(string key, TimeSpan timeToLive, CancellationToken ct)` | `Task<Result<bool>>` | ✅ L2, L4 |
| `RefreshAsync` | `(string key, CancellationToken ct)` | `Task<Result<bool>>` | ✅ L2, L4 |

---

### Additional Methods — ITaggedCacheProvider

| Method | Signature | Return | In Showcase |
|---|---|---|---|
| `SetWithTagsAsync<T>` | `(string key, T value, IEnumerable<string> tags, CacheEntryOptions? options, CancellationToken ct)` | `Task<Result<bool>>` | ✅ L2, L3, L4 |
| `RemoveByTagAsync` | `(string tag, CancellationToken ct)` | `Task<Result<long>>` | ✅ L2, L3, L4 |
| `RemoveByTagsAsync` | `(IEnumerable<string> tags, CancellationToken ct)` | `Task<Result<long>>` | ✅ L2, L3, L4 |

---

### Methods — ICacheInvalidator

| Method | Signature | Return | In Showcase |
|---|---|---|---|
| `InvalidateAsync` | `(string key, CancellationToken ct)` | `Task<Result<bool>>` | ✅ L3 |
| `InvalidateManyAsync` | `(IEnumerable<string> keys, CancellationToken ct)` | `Task<Result<long>>` | ✅ L3 |
| `InvalidateByPrefixAsync` | `(string prefix, CancellationToken ct)` | `Task<Result<long>>` | ✅ L3 |
| `InvalidateByPrefixesAsync` | `(IEnumerable<string> prefixes, CancellationToken ct)` | `Task<Result<long>>` | ✅ L3 |
| `InvalidateByTagAsync` | `(string tag, CancellationToken ct)` | `Task<Result<long>>` | ✅ L3 |
| `InvalidateByTagsAsync` | `(IEnumerable<string> tags, CancellationToken ct)` | `Task<Result<long>>` | ✅ L3 |

---

### Methods — ICacheSerializer

| Method | Signature | Return | In Showcase |
|---|---|---|---|
| `Serialize<T>` | `(T value)` | `string` | ✅ L8 |
| `Deserialize<T>` | `(string value)` | `T?` | ✅ L8 |
| `SerializeToUtf8Bytes<T>` | `(T value)` | `byte[]` | ✅ L8 |
| `DeserializeFromUtf8Bytes<T>` | `(byte[] bytes)` | `T?` | ✅ L8 |

---

### Configuration Classes

| Name | Namespace | Responsibility | Properties | Complexity | In Showcase |
|---|---|---|---|---|---|
| `CacheEntryOptions` | `EricksonLopez.Caching` | Cache entry options | `AbsoluteExpirationRelativeToNow`, `SlidingExpiration`, `FailSafeMaxStale`, `Tags`, `MaxAllowedDuration` (const) | Basic | ✅ L1, L2 |
| `HybridCacheEntryOptions` | `EricksonLopez.Caching` | Extends `CacheEntryOptions` with independent TTLs per tier | `LocalCacheDuration`, `DistributedCacheDuration` (+ inherited) | Intermediate | ✅ L2, L4 |
| `MemoryCacheOptions` | `EricksonLopez.Caching` | L1 provider configuration | `MaxCapacity`, `ExpirationScanFrequency`, `CompactPercentage` | Basic | ✅ L2, L5 |

---

### Diagnostics — CacheDiagnostics

| Member | Type | In Showcase |
|---|---|---|
| `MeterName` | `const string = "EricksonLopez.Caching"` | ✅ L8 |
| `ActivitySourceName` | `const string = "EricksonLopez.Caching"` | ✅ L8 |
| `ActivitySource` | `ActivitySource` (property) | ✅ L8 |
| `StartActivity(string op, string provider, string key)` | `Activity?` | ✅ L8 |
| `RecordHit(string provider, string? operation)` | `void` | ✅ L8 |
| `RecordMiss(string provider, string? operation)` | `void` | ✅ L8 |
| `RecordStampedePrevented(string provider)` | `void` | ✅ L8 |
| `RecordEviction(string provider, string reason, long count)` | `void` | ✅ L8 |
| `RecordError(string provider, string errorCode)` | `void` | ✅ L8 |

---

### Extension Methods — `CachingServiceCollectionExtensions`

| Method | Namespace | Overload | In Showcase |
|---|---|---|---|
| `AddMemoryCacheProvider()` | `EricksonLopez.Caching` | Parameterless | ✅ L9 |
| `AddMemoryCacheProvider(Action<MemoryCacheOptions>)` | `EricksonLopez.Caching` | With options configurator | ✅ L9 |
| `AddCacheInvalidator()` | `EricksonLopez.Caching` | Registers default `CacheInvalidator` | ✅ L9 |
| `AddCacheInvalidator<TInvalidator>()` | `EricksonLopez.Caching` | Registers custom invalidator | ✅ L9 |
| `AddHybridCache(...)` | `EricksonLopez.Caching` | Factory L1 + Factory L2 | ✅ L9 |
| `AddTenantPartitionedCache(Func<IServiceProvider, string?>)` | `EricksonLopez.Caching` | Dynamic tenant resolver delegate | ✅ L9 |

---

## Infrastructure Library — `EricksonLopez.Caching.Redis`

### Concrete Provider

| Name | Namespace | Responsibility | Dependencies | Use Cases | Complexity | In Showcase |
|---|---|---|---|---|---|---|
| `RedisCacheProvider` | `EricksonLopez.Caching.Redis` | L2 Redis distributed provider. Atomic Lua scripts for distributed stampede lock leases, exponential backoff retries, prefix eviction, tag eviction | `IConnectionMultiplexer`, `RedisCacheOptions`, `ILogger`, `ICacheSerializer` | Multi-instance applications, distributed caching | Advanced | ✅ L9 (DI) |

---

### Redis Options

| Name | Namespace | Properties | Complexity | In Showcase |
|---|---|---|---|---|
| `RedisCacheOptions` | `EricksonLopez.Caching.Redis` | `Configuration`, `InstanceName`, `DefaultAbsoluteExpiration`, `Database`, `MaxRetries`, `RetryDelay`, `LockTimeout`, `LockRetryInterval`, `MaxLockWaitTime`, `Serializer`, `FailOpen` | Intermediate | ✅ L2, L9 |

---

### Redis DI Extension Methods

| Method | Overload | In Showcase |
|---|---|---|
| `AddRedisCaching(Action<RedisCacheOptions>)` | Overload 1: Connection string configured in options | ✅ L9 |
| `AddRedisCaching(IConnectionMultiplexer, Action<RedisCacheOptions>?)` | Overload 2: External multiplexer instance | ✅ L9 |
| `AddRedisCaching(Func<IServiceProvider, IConnectionMultiplexer>, Action<RedisCacheOptions>?)` | Overload 3: Factory delegate resolving multiplexer | ✅ L9 |

---

## Recognized Error Codes

| Code | Description | Provider | In Showcase |
|---|---|---|---|
| `Cache.Memory.FactoryFailed` | Factory delegate threw an exception without fail-safe stale value | Memory | ✅ L6 |
| `Cache.InvalidTimeToLive` | TTL <= 0 supplied to `ExpireAsync` | Memory | ✅ L6 |
| `Cache.InvalidKey` | Key is null, empty, or whitespace in `ICacheInvalidator` | Memory | ✅ L6 |
| `Cache.InvalidPrefix` | Prefix is null, empty, or whitespace in `ICacheInvalidator` | Memory | ✅ L6 |
| `Cache.InvalidTag` | Tag is null, empty, or whitespace in `ICacheInvalidator` | Memory | ✅ L6 |
| `Cache.TagsNotSupported` | Underlying cache provider does not implement `ITaggedCacheProvider` | Any | ✅ L6 |
| `Cache.Redis.ConnectionFailed` | Redis connection or socket failed | Redis | ✅ L8 |
| `Cache.Redis.FactoryFailed` | Factory failed during `GetOrCreateAsync` in Redis | Redis | ✅ L8 |
| `Cache.Redis.DeserializationFailed` | Failed deserializing Redis payload | Redis | ✅ L8 |
| `Cache.Redis.SerializationFailed` | Failed serializing payload for Redis | Redis | ✅ L8 |

---

## Coverage Summary

| Category | Total Elements | Covered in Showcase | % |
|---|---|---|---|
| Interfaces | 4 | 4 | 100% |
| Providers | 5 | 5 | 100% |
| `ICacheProvider` Methods | 11 | 11 | 100% |
| `ITaggedCacheProvider` Methods | 3 | 3 | 100% |
| `ICacheInvalidator` Methods | 6 | 6 | 100% |
| `ICacheSerializer` Methods | 4 | 4 | 100% |
| Configuration Models | 3 | 3 | 100% |
| `CacheEntryOptions` Factory Methods | 3 | 3 | 100% |
| `MemoryCacheOptions` Guards | 4 | 4 | 100% |
| `CacheDiagnostics` Members | 9 | 9 | 100% |
| Core DI Extension Methods | 6 | 6 | 100% |
| Redis DI Extension Methods | 3 | 3 | 100% |
| Error Codes | 10 | 10 | 100% |
| **TOTAL** | **71** | **71** | **100%** |
