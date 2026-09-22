# Best Practices — EricksonLopez.Caching

## 1. Key Naming Conventions

Use `:` as the namespace separator. The recommended architectural structure is:

```text
{domain}:{aggregate}:{id}[:{projection}]
```

Examples:
- `catalog:product:42`
- `catalog:product:42:summary`
- `orders:order:1001:items`
- `users:user:99:preferences`

**Avoid:** Generic names like `"data"`, `"cache"`, or `"result"` that lead to namespace collisions between modules.

---

## 2. Recommended TTL by Data Category

| Data Category | Recommended TTL | Invalidation Strategy |
|---|---|---|
| Configuration / Feature flags | 5–15 minutes | Prefix / Tag or Event-driven |
| Product Catalog | 30 minutes – 2 hours | Tag-based (`category`, `product:id`) |
| Real-time Pricing | 30 seconds – 2 minutes | Short TTL with stampede protection |
| Search Results | 5–15 minutes | Query hash key with sliding TTL |
| User Sessions | Sliding 30 minutes | Absolute max ceiling (e.g. 8 hours) |
| Reference Data (currencies, countries) | 24 hours | Long TTL |
| Expensive Aggregations / Reports | 1–4 hours | Pre-warmed with fail-safe stale serving |

---

## 3. Always Prefer GetOrCreateAsync on Hot Paths

```csharp
// ✅ Recommended: Automatic single-flight stampede protection
var result = await cache.GetOrCreateAsync("product:42", ct => db.FindAsync(42, ct));

// ❌ Discouraged: Prone to race conditions and stampedes between Get and Set
var cached = await cache.GetAsync<Product>("product:42");
if (cached.Value is null)
    await cache.SetAsync("product:42", await db.FindAsync(42));
```

---

## 4. Always Forward the CancellationToken

```csharp
// ✅ Correct: CancellationToken propagated through the pipeline
await cache.GetOrCreateAsync("key", async ct =>
{
    return await db.QueryAsync(ct);
}, cancellationToken: ct);

// ❌ Incorrect: CancellationToken ignored, preventing cancellation
await cache.GetOrCreateAsync("key", async ct =>
{
    return await db.QueryAsync(CancellationToken.None);
});
```

---

## 5. Verify Result Control Flow Predictably

```csharp
var result = await cache.GetOrCreateAsync<Product>("product:42", factory);

// ✅ Correct: Checks both status and payload existence
if (result.IsSuccess && result.Value is not null)
    return result.Value;

// ❌ Incorrect: Blind unwrap fails to distinguish between cache miss and infrastructure failure
return result.Value!;
```

---

## 6. Use Semantic Tags for Projections

```csharp
// When storing: annotate with all relevant semantic tags
await tagged.SetWithTagsAsync("product:42:list-view", dto,
    tags: new[] { "products", "category:electronics", "product:42" });

// When mutating product 42: invalidate only related entries
await invalidator.InvalidateByTagAsync("product:42");

// When reorganizing categories: invalidate entire category cluster
await invalidator.InvalidateByTagAsync("category:electronics");
```

---

## 7. Configure MemoryCacheOptions Appropriately

```csharp
services.AddMemoryCacheProvider(opt =>
{
    // MaxCapacity: estimate peak working set in production
    opt.MaxCapacity = Environment.IsProduction ? 100_000 : 1_000;

    // CompactPercentage: 10-20% provides steady compaction headroom
    opt.CompactPercentage = 0.15;

    // ExpirationScanFrequency: tune based on density of short-lived entries
    opt.ExpirationScanFrequency = TimeSpan.FromMinutes(5);
});
```

---

## 8. Fail-Safe Stale Serving: Use for High-Availability Data

```csharp
// ✅ Correct: Critical read paths with controlled degradation
var opts = CacheEntryOptions.FromAbsoluteWithFailSafe(
    duration: TimeSpan.FromMinutes(5),
    maxStale: TimeSpan.FromHours(2)); // Up to 2 hours of stale data if backend fails

// ❌ Incorrect: Do NOT use fail-safe for financial balances, audit logs, or live security tokens
// For sensitive transactional state, fail explicitly rather than serving stale data
```

---

## 9. Redis: Enable FailOpen in Production

```csharp
services.AddRedisCaching(opt =>
{
    opt.FailOpen = true; // Degrade gracefully to L1/factory if Redis cluster is degraded
    opt.MaxRetries = 3;
    opt.RetryDelay = TimeSpan.FromMilliseconds(100);
});
```

---

## 10. Observability: Instrument OpenTelemetry Upfront

```csharp
// Always register OpenTelemetry meters and activity sources in production
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter(CacheDiagnostics.MeterName))
    .WithTracing(t => t.AddSource(CacheDiagnostics.ActivitySourceName));
```

Core Telemetry Signals to Monitor:
- **Hit Rate** = `hits / (hits + misses)` — target > 85% in steady-state
- **Stampedes Prevented** — spikes indicate high key contention; evaluate TTL
- **Capacity Compactions** — frequent compactions indicate `MaxCapacity` is sized too small
- **Connection Errors** — alerts indicate network latency or Redis cluster degradation

---

## 11. Multi-Tenant Key Isolation: Validate the Resolver

```csharp
services.AddTenantPartitionedCache(sp =>
{
    var tenantId = ResolveTenantId(sp);
    if (string.IsNullOrWhiteSpace(tenantId))
        throw new InvalidOperationException("Tenant ID cannot be null or empty in production.");
    return tenantId;
});
```

---

## 12. Singleton DI Lifetime

Both `MemoryCacheProvider` and `HybridCacheProvider` must be registered as **Singletons**. The provided DI extension methods configure this automatically. When manually instantiating providers outside DI, ensure they are held as singletons or disposed via `using`.

```csharp
// ✅ Correct: Singleton via DI
services.AddMemoryCacheProvider();

// ✅ Correct: Scoped lifecycle with using in standalone tools or tests
using var cache = new MemoryCacheProvider();

// ❌ Incorrect: Transient instantiation per request destroys in-memory cache and stampede locks
var cache = new MemoryCacheProvider(); // Inside controller or request handler
```
