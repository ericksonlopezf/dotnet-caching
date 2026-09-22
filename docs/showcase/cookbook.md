# Cookbook: EricksonLopez.Caching

> Practical recipes derived from the public API. Each recipe solves a concrete architectural or operational requirement.

---

## Recipe 1: Simple Caching with Absolute Expiration

**Problem:** Cache the result of an expensive calculation and have it automatically expire after a fixed duration.

**Solution:**
```csharp
using var cache = new MemoryCacheProvider();
var options = CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(10));
await cache.SetAsync("report:monthly", expensiveReport, options);

var result = await cache.GetAsync<MonthlyReport>("report:monthly");
if (result.IsSuccess && result.Value is not null)
{
    // Cache hit
    return result.Value;
}
```

**Explanation:** `FromAbsolute` configures an absolute TTL; the entry expires exactly 10 minutes after write. `GetAsync` returns `Result.Success` with `null` on cache misses, never throwing exceptions.

**Best Practices:**
- Prefer `GetOrCreateAsync` over standalone Get + Set to prevent race conditions.
- Base TTL on business data volatility rather than arbitrary latency targets.

**Common Pitfalls:**
- Supplying `TimeSpan.Zero` throws `ArgumentOutOfRangeException` (enforced by `ValidateDuration`).
- Assuming `GetAsync` returns failure on misses; it returns `Result.Success(null)`.

---

## Recipe 2: Read-Through with Stampede Protection (Single-Flight)

**Problem:** Thousands of concurrent callers hit a cold key when it expires, threatening to overwhelm the backend database.

**Solution:**
```csharp
using var cache = new MemoryCacheProvider();
var product = await cache.GetOrCreateAsync<Product>(
    key: $"product:{productId}",
    factory: async ct =>
    {
        // Only ONE caller executes this query concurrently
        return await db.FindProductAsync(productId, ct);
    },
    options: CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(30)));

if (product.IsSuccess)
    return product.Value;
```

**Explanation:** `GetOrCreateAsync` implements the single-flight pattern: a keyed synchronization primitive guarantees the factory delegate is invoked exactly once. Concurrent callers wait asynchronously and receive the computed value.

**Best Practices:**
- Forward `CancellationToken` to the factory delegate.
- Configure realistic TTLs to prevent frequent cold-start recomputations.

---

## Recipe 3: Multi-Tier Hybrid Caching (L1 In-Memory + L2 Redis)

**Problem:** Accelerate read operations with microsecond local L1 memory, while maintaining distributed cluster synchronization via Redis L2.

**Solution:**
```csharp
using var l1 = new MemoryCacheProvider();
using var l2 = new RedisCacheProvider(connectionMultiplexer);
using var hybrid = new HybridCacheProvider(l1, l2);

var options = HybridCacheEntryOptions.FromDurations(
    localCacheDuration: TimeSpan.FromMinutes(5),
    distributedCacheDuration: TimeSpan.FromHours(1));

var result = await hybrid.GetOrCreateAsync(
    "catalog:item:100",
    ct => db.GetCatalogItemAsync(100, ct),
    options);
```

**Explanation:** L1 serves requests with sub-microsecond latency. When L1 misses, L2 is inspected. If both miss, the factory executes, populating both L1 and L2 simultaneously.

**Best Practices:**
- Keep L1 TTL shorter than L2 TTL (`LocalCacheDuration <= DistributedCacheDuration`).
- Enable `FailOpen = true` on Redis options to tolerate distributed tier outages.

---

## Recipe 4: Semantic Tag-Based Invalidation

**Problem:** Invalidate multiple related projections (e.g., product card, inventory summary, detail view) when a domain aggregate is updated.

**Solution:**
```csharp
// 1. Store cache entries annotated with domain tags
await cache.SetWithTagsAsync("product:42:detail", detailDto,
    tags: ["products", "catalog", "product:42"],
    options: CacheEntryOptions.FromAbsolute(TimeSpan.FromHours(1)));

await cache.SetWithTagsAsync("product:42:card", cardDto,
    tags: ["products", "product:42"],
    options: CacheEntryOptions.FromAbsolute(TimeSpan.FromHours(1)));

// 2. Invalidate all entries tagged with 'product:42' in one call
await invalidator.InvalidateByTagAsync("product:42");
```

**Explanation:** Tags create semantic groupings across unrelated key spaces. `InvalidateByTagAsync` purges all entries associated with that tag without needing individual key lookups.

---

## Recipe 5: Scoped Prefix Eviction

**Problem:** Invalidate all cached data belonging to an entire customer or subsystem (e.g. tenant eviction or catalog reload).

**Solution:**
```csharp
// Evict all keys prefixed with 'catalog:'
var result = await cache.RemoveByPrefixAsync("catalog:");
if (result.IsSuccess)
{
    Console.WriteLine("All catalog entries evicted.");
}
```

**Explanation:** `RemoveByPrefixAsync` searches and removes keys matching the prefix. In Redis, this executes atomic SCAN + UNLINK operations.

**Guards:** Empty or whitespace prefixes throw `ArgumentException` to prevent accidental total cache purges.

---

## Recipe 6: Resilient Stale Serving (Fail-Safe Pattern)

**Problem:** Serve stale cached data when the backend database is down, ensuring high availability.

**Solution:**
```csharp
var options = CacheEntryOptions.FromAbsoluteWithFailSafe(
    duration: TimeSpan.FromMinutes(5),
    maxStale: TimeSpan.FromHours(4)); // Serve stale data up to 4 hours if factory fails

var result = await cache.GetOrCreateAsync("exchange:rates", async ct =>
{
    return await api.FetchLiveRatesAsync(ct);
}, options);

if (result.IsSuccess)
    return result.Value;
```

**Explanation:** When the primary 5-minute TTL expires, the entry enters the fail-safe window. If the factory succeeds, fresh data is stored. If the factory throws, the stale value is returned and an OpenTelemetry error is recorded.

---

## Recipe 7: Multi-Tenant Key Isolation

**Problem:** Enforce strict key isolation in multi-tenant SaaS applications without modifying application handlers.

**Solution:**
```csharp
var tenantCache = new TenantPartitionedCacheProvider(
    innerCache: cache,
    tenantIdResolver: () => httpContextAccessor.HttpContext?.User.FindFirst("tenant_id")?.Value);

// Stores internally as "tenant:alpha:profile"
await tenantCache.SetAsync("profile", userProfile);

// Automatically resolves only within the active tenant's partition
var profile = await tenantCache.GetAsync<UserProfile>("profile");
```

**Explanation:** `TenantPartitionedCacheProvider` automatically prefixes keys with the active tenant ID, ensuring zero data bleed between tenants.

---

## Recipe 8: Native AOT-Compliant Source-Generated Serialization

**Problem:** Serialize custom DTOs in Redis without triggering trimmer or Native AOT runtime reflection warnings (`IL2026`, `IL3050`).

**Solution:**
```csharp
[JsonSerializable(typeof(OrderDto))]
internal partial class OrderJsonContext : JsonSerializerContext { }

// Register custom serializer
var serializer = new SystemTextJsonCacheSerializer(OrderJsonContext.Default.Options);
services.Configure<RedisCacheOptions>(opt => opt.Serializer = serializer);
```

**Explanation:** By supplying compile-time source-generated `JsonSerializerOptions`, the cache serializer operates completely reflection-free, ensuring 100% Native AOT trimming safety.

---

## Recipe 9: Batch Operations (GetMany, SetMany, RemoveMany)

**Problem:** Minimize roundtrips when querying or writing multiple cache keys simultaneously.

**Solution:**
```csharp
var keys = new[] { "item:1", "item:2", "item:3" };
var results = await cache.GetManyAsync<ItemDto>(keys);

foreach (var (key, result) in results)
{
    if (result.IsSuccess && result.Value is not null)
        Console.WriteLine($"Found {key}: {result.Value.Name}");
}
```

**Explanation:** In distributed providers like Redis, batch operations utilize pipelining or multi-key commands, significantly reducing network latency.

---

## Recipe 10: Sliding Expiration with Absolute Ceilings

**Problem:** Keep frequently accessed data in memory, but enforce an absolute maximum lifespan to avoid stale state.

**Solution:**
```csharp
var options = new CacheEntryOptions
{
    SlidingExpiration = TimeSpan.FromMinutes(15),
    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(4)
};

await cache.SetAsync("session:12345", sessionData, options);
```

**Explanation:** Each access resets the 15-minute sliding timer, but the entry unconditionally expires 4 hours after initial creation.

---

## Recipe 11: OpenTelemetry Metrics & Distributed Tracing

**Problem:** Monitor cache performance, hit ratios, and stampede events in Grafana or Datadog.

**Solution:**
```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(CacheDiagnostics.MeterName))
    .WithTracing(tracing => tracing.AddSource(CacheDiagnostics.ActivitySourceName));
```

**Key Metrics:**
- `cache.hits` — Count of successful lookups.
- `cache.misses` — Count of missed lookups.
- `cache.stampedes.prevented` — Count of concurrent callers saved by single-flight synchronization.
- `cache.evictions` — Count of evicted entries categorized by reason.

---

## Recipe 12: Redis Connection Resilience & FailOpen

**Problem:** Prevent Redis cluster connection failures from bringing down the web application.

**Solution:**
```csharp
builder.Services.AddRedisCaching(options =>
{
    options.Configuration = "redis.internal:6379";
    options.FailOpen = true; // Return Success(null) instead of Failure on network faults
    options.MaxRetries = 3;
    options.RetryDelay = TimeSpan.FromMilliseconds(50);
});
```

**Explanation:** With `FailOpen = true`, network dropouts degrade gracefully, allowing downstream database queries to proceed rather than throwing 500 errors to callers.

---

## Recipe 13: Memory Capacity Compaction

**Problem:** Prevent out-of-memory errors on high-throughput microservices with unbounded keyspaces.

**Solution:**
```csharp
builder.Services.AddMemoryCacheProvider(options =>
{
    options.MaxCapacity = 50_000;         // Maximum tracked keys
    options.CompactPercentage = 0.10;     // Evict 10% when full
    options.ExpirationScanFrequency = TimeSpan.FromMinutes(2);
});
```

**Explanation:** When `MaxCapacity` is breached, the cache automatically compacts itself, evicting expired and LRU entries while recording metrics.

---

## Recipe 14: Custom Invalidator Integration in Event Handlers

**Problem:** Trigger invalidation upon receiving domain events from MassTransit or MediatR.

**Solution:**
```csharp
public class OrderCreatedHandler(ICacheInvalidator invalidator)
{
    public async Task Handle(OrderCreatedEvent notification, CancellationToken ct)
    {
        await invalidator.InvalidateByTagAsync($"customer:{notification.CustomerId}", ct);
        await invalidator.InvalidateByPrefixAsync("summary:daily:", ct);
    }
}
```

**Explanation:** Inject `ICacheInvalidator` into domain command handlers or consumer pipelines to decouple cache management from business logic.

---

## Recipe 15: Dependency Injection Service Registration

**Problem:** Register the entire caching ecosystem cleanly in `Program.cs`.

**Solution:**
```csharp
// Register Memory Cache L1
builder.Services.AddMemoryCacheProvider();

// Register Invalidator
builder.Services.AddCacheInvalidator();

// Register Redis L2 (optional)
builder.Services.AddRedisCaching(opt => opt.Configuration = "localhost:6379");

// Register Multi-Tier Hybrid Cache Coordinator
builder.Services.AddHybridCache();
```

**Explanation:** Extension methods handle service lifetimes, options configuration, and interface mappings as singletons.
