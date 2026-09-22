# API Reference — EricksonLopez.Caching

> Comprehensive technical documentation for all public types, interfaces, and methods in the EricksonLopez.Caching ecosystem.

---

## ICacheProvider.GetAsync&lt;T&gt;

**Signature:**
```csharp
Task<Result<T?>> GetAsync<T>(string key, CancellationToken cancellationToken = default)
```

**Parameters:**
| Parameter | Type | Description |
|---|---|---|
| `key` | `string` | Cache key. Cannot be null, empty, or whitespace. |
| `cancellationToken` | `CancellationToken` | Cancellation token. |

**Returns:** `Task<Result<T?>>` — Always `Result.Success`. Value is `null` if the key does not exist or has expired.

**Exceptions:**
- `ArgumentException` — `key` is null, empty, or whitespace.
- `OperationCanceledException` — `cancellationToken` is cancelled before or during execution.

**Remarks:** A cache miss does NOT return `Result.Failure`. It returns `Result.Success(null)`. Inspect `result.IsSuccess && result.Value is not null` to detect hits.

**Example:**
```csharp
var result = await cache.GetAsync<string>("my:key");
if (result.IsSuccess && result.Value is not null)
    Console.WriteLine(result.Value);
```

**When NOT to use:** Do not use solely to check existence. Prefer `ExistsAsync` when payload deserialization is not required.

---

## ICacheProvider.GetOrCreateAsync&lt;T&gt;

**Signature:**
```csharp
Task<Result<T>> GetOrCreateAsync<T>(
    string key,
    Func<CancellationToken, Task<T>> factory,
    CacheEntryOptions? options = null,
    CancellationToken cancellationToken = default)
```

**Parameters:**
| Parameter | Type | Description |
|---|---|---|
| `key` | `string` | Cache key. |
| `factory` | `Func<CancellationToken, Task<T>>` | Delegate invoked to generate the value on cache miss. Executed exactly once under stampede concurrency. |
| `options` | `CacheEntryOptions?` | TTL, fail-safe window, and semantic tags. Null uses default options. |
| `cancellationToken` | `CancellationToken` | Cancellation token. |

**Returns:** `Result.Success(value)` if cached or factory succeeds. `Result.Failure(...)` if factory throws and no fail-safe stale value is available.

**Exceptions:**
- `ArgumentNullException` — `key` or `factory` is null.
- `OperationCanceledException` — Token cancelled.

**Remarks:** Automatically guarantees single-flight concurrency: only one thread executes the factory for a given key, while concurrent callers wait and receive the same value.

---

## ICacheProvider.SetAsync&lt;T&gt;

**Signature:**
```csharp
Task<Result<bool>> SetAsync<T>(
    string key,
    T value,
    CacheEntryOptions? options = null,
    CancellationToken cancellationToken = default)
```

**Parameters:**
| Parameter | Type | Description |
|---|---|---|
| `key` | `string` | Cache key. |
| `value` | `T` | Value to store. |
| `options` | `CacheEntryOptions?` | Expiration and tag settings. |
| `cancellationToken` | `CancellationToken` | Cancellation token. |

**Returns:** `Result.Success(true)` upon successful storage.

---

## ICacheProvider.RemoveAsync

**Signature:**
```csharp
Task<Result<bool>> RemoveAsync(string key, CancellationToken cancellationToken = default)
```

**Parameters:**
| Parameter | Type | Description |
|---|---|---|
| `key` | `string` | Cache key to remove. |
| `cancellationToken` | `CancellationToken` | Cancellation token. |

**Returns:** `Result.Success(true)` if the entry existed and was removed; `Result.Success(false)` if the entry was not present.

---

## ICacheProvider.RemoveByPrefixAsync

**Signature:**
```csharp
Task<Result<long>> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
```

**Parameters:**
| Parameter | Type | Description |
|---|---|---|
| `prefix` | `string` | Key prefix to search and purge. |
| `cancellationToken` | `CancellationToken` | Cancellation token. |

**Returns:** `Result.Success(count)` indicating the total number of purged entries.

**Guards:** Throws `ArgumentException` if `prefix` is null, empty, or whitespace.

---

## ICacheProvider.ExistsAsync

**Signature:**
```csharp
Task<Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default)
```

**Remarks:** Validates whether an unexpired key exists in the cache without loading or deserializing the payload into memory.

---

## ICacheProvider.ExpireAsync

**Signature:**
```csharp
Task<Result<bool>> ExpireAsync(string key, TimeSpan timeToLive, CancellationToken cancellationToken = default)
```

**Remarks:** Resets the remaining TTL of an existing entry to the specified duration.

---

## ICacheProvider.RefreshAsync

**Signature:**
```csharp
Task<Result<bool>> RefreshAsync(string key, CancellationToken cancellationToken = default)
```

**Remarks:** Resets the sliding expiration clock for entries configured with `SlidingExpiration`.

---

## ITaggedCacheProvider

Extends `ICacheProvider` with semantic multi-key tag associations.

### SetWithTagsAsync
```csharp
Task<Result<bool>> SetWithTagsAsync<T>(
    string key,
    T value,
    IEnumerable<string> tags,
    CacheEntryOptions? options = null,
    CancellationToken cancellationToken = default)
```

### RemoveByTagAsync
```csharp
Task<Result<long>> RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
```

### RemoveByTagsAsync
```csharp
Task<Result<long>> RemoveByTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
```

---

## ICacheInvalidator

Defines high-level domain invalidation commands.

- `InvalidateAsync(string key, CancellationToken ct)`
- `InvalidateByPrefixAsync(string prefix, CancellationToken ct)`
- `InvalidateByTagAsync(string tag, CancellationToken ct)`
- `InvalidateManyAsync(IEnumerable<string> keys, CancellationToken ct)`
- `InvalidateByPrefixesAsync(IEnumerable<string> prefixes, CancellationToken ct)`
- `InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken ct)`

---

## Configuration Models

### CacheEntryOptions
- `AbsoluteExpirationRelativeToNow` (`TimeSpan?`) — Absolute TTL relative to creation.
- `SlidingExpiration` (`TimeSpan?`) — Sliding TTL refreshed on access.
- `FailSafeMaxStale` (`TimeSpan?`) — Maximum window to serve stale data on factory failure.
- `Tags` (`IReadOnlyCollection<string>`) — Domain tags assigned to the entry.

### HybridCacheEntryOptions
- `LocalCacheDuration` (`TimeSpan`) — L1 memory duration.
- `DistributedCacheDuration` (`TimeSpan`) — L2 Redis duration.

### MemoryCacheOptions
- `MaxCapacity` (`int?`) — Maximum entries before capacity compaction.
- `CompactPercentage` (`double`) — Ratio of entries purged on compaction (default 0.10).
- `ExpirationScanFrequency` (`TimeSpan`) — Periodic timer interval for cleaning expired items.

### RedisCacheOptions
- `Configuration` (`string?`) — Redis connection string.
- `InstanceName` (`string?`) — Keyspace prefix.
- `FailOpen` (`bool`) — When true, suppresses Redis infrastructure failures to allow L1/factory fallback.
- `MaxRetries` (`int`) — Retry attempts for transient failures.
- `RetryDelay` (`TimeSpan`) — Delay between retries.
- `Serializer` (`ICacheSerializer?`) — Custom serializer (e.g. source-generated Native AOT).

---

## Observability & Diagnostics

### CacheDiagnostics
- `MeterName` = `"EricksonLopez.Caching"`
- `ActivitySourceName` = `"EricksonLopez.Caching"`
- `RecordHit(string provider, string tier)`
- `RecordMiss(string provider, string tier)`
- `RecordStampedePrevented(string provider)`
- `RecordEviction(string provider, string reason, long count)`
- `RecordError(string provider, string errorType)`
