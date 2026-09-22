# FAQ — EricksonLopez.Caching

## Frequently Asked Questions

---

### Does a cache miss return `Result.Failure`?

**No.** A cache miss always returns `Result.Success(null)`. `Result.Failure` is only produced when there is an infrastructure failure or when the factory delegate throws an exception and no fail-safe stale value is available. Always check `result.IsSuccess && result.Value is not null` to detect cache hits.

---

### When should I use `GetAsync<T>` vs. `GetOrCreateAsync<T>`?

- `GetAsync<T>`: Use when you need to inspect whether data exists in the cache without automatically computing or generating it.
- `GetOrCreateAsync<T>`: The complete cache-aside pattern. Prefer this in high-concurrency hot paths; it provides automated single-flight stampede protection.

---

### What happens if `GetOrCreateAsync` is invoked with 1,000 concurrent requests?

Only **1 request** executes the factory delegate. The remaining 999 requests wait asynchronously on the keyed `SemaphoreSlim` (in L1) or atomic Redis lock lease (in L2), receiving the generated value once the first caller completes. This is the single-flight pattern and represents core behavior by design.

---

### Is `MemoryCacheProvider` thread-safe?

Yes. It uses a `ConcurrentDictionary` internally and keyed `SemaphoreSlim` instances for single-flight execution. It is fully thread-safe for concurrent read and write access across multiple threads.

---

### What is the difference between `CacheEntryOptions.Tags` and `SetWithTagsAsync`?

- `CacheEntryOptions.Tags`: Assigns semantic tags directly when calling `SetAsync`.
- `SetWithTagsAsync` in `ITaggedCacheProvider`: A convenience helper method equivalent to calling `SetAsync` with `options.Tags` pre-configured.

---

### Why does `TenantPartitionedCacheProvider` use a `Func<string?>` instead of a static `string`?

Because the active tenant ID can change per request (e.g., resolved from `HttpContext`, claims, or JWT). The `Func<string?>` accessor is dynamically evaluated on each cache operation to resolve the active tenant partition.

---

### What does `FailOpen = true` in `RedisCacheOptions` do?

When `FailOpen = true`, failed Redis operations return `Result.Success(null)` rather than `Result.Failure(...)`. This enables graceful degradation in production when Redis is unreachable, allowing the factory delegate or fallback L1 cache to serve requests uninterrupted.

---

### Can I use `HybridCacheProvider` without Redis?

Yes. You can supply two instances of `MemoryCacheProvider` as L1 and L2. This is useful for unit testing and local development, although production deployments typically configure Redis as L2 to synchronize cache state across nodes.

---

### What happens when `MaxCapacity` is reached in `MemoryCacheProvider`?

`MemoryCacheProvider` triggers automated capacity compaction. It evicts a configurable percentage of entries defined by `CompactPercentage` (defaulting to 10%). Expired, older, or least-recently-accessed entries are removed first. This eviction event is recorded to OpenTelemetry metrics via `CacheDiagnostics.RecordEviction("memory", "capacity_compaction", n)`.

---

### How do I integrate OpenTelemetry metrics and tracing?

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter(CacheDiagnostics.MeterName))
    .WithTracing(t => t.AddSource(CacheDiagnostics.ActivitySourceName));
```

All cache providers automatically instrument hits, misses, evictions, stampede lock acquisitions, and infrastructure errors.

---

### Can `ExpireAsync` increase an existing TTL?

Yes. `ExpireAsync` resets the active TTL to the new duration provided, whether it is longer or shorter than the previous TTL. It only fails if the requested duration is non-positive (`<= TimeSpan.Zero`).

---

### Can I implement a custom `ICacheProvider`?

Yes. Implement `ICacheProvider` (and optionally `ITaggedCacheProvider`). The core required primitives are `GetAsync`, `GetOrCreateAsync`, `SetAsync`, `RemoveAsync`, and `RemoveByPrefixAsync`. The batch and utility methods (`ExistsAsync`, `ExpireAsync`, `RefreshAsync`, `GetManyAsync`, `SetManyAsync`, `RemoveManyAsync`) have default interface implementations that can be overridden for backend-specific optimizations.

---

### Does the Showcase require a live Redis instance?

No. The showcase demonstrates **dependency injection configuration and options validation** using an in-memory `FakeConnectionMultiplexer`. It does not require an active Redis daemon to execute. For real integration tests against Redis, see `tests/EricksonLopez.Caching.Redis.Tests/`.

---

### When should I avoid using `RemoveByPrefixAsync`?

Avoid prefix eviction when your key naming convention does not guarantee strict prefix exclusivity. Overly broad prefixes (such as `"app:"` or `"data:"`) risk purging unrelated domain keys. For multi-projection invalidation across diverse keyspaces, prefer tag-based invalidation via `ITaggedCacheProvider.RemoveByTagsAsync`.
