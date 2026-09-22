# EricksonLopez.Caching

Enterprise-grade multi-tier caching for .NET 8, 9, and 10.

[![Build](https://img.shields.io/badge/build-passing-brightgreen)]()
[![.NET](https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010-purple)]()
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)]()

## Features

- **Single-flight stampede protection** — prevents thundering herd on cache miss; only one caller executes the factory, the rest wait
- **Functional `Result<T>` API** — no raw exceptions from infrastructure; always predictable return types
- **Multi-tier support** — Memory (L1) + Redis (L2) + Hybrid coordinator
- **Tag-based invalidation** — invalidate groups of entries by semantic tag (e.g., `"catalog"`, `"user:42"`)
- **Multi-tenant isolation** — automatic key partitioning by tenant via `TenantPartitionedCacheProvider`
- **Fail-safe stale serving** — serve stale data when the factory fails (within a configurable window)
- **OpenTelemetry native** — metrics and distributed traces out of the box via `CacheDiagnostics`
- **Custom serializers** — plug in any `ICacheSerializer` implementation (encryption, compression, etc.)

## Providers

| Provider | Description | Use Case |
|---|---|---|
| `MemoryCacheProvider` | In-process, L1 cache | Single-instance, hot data |
| `RedisCacheProvider` | Redis-backed, L2 distributed | Multi-instance apps |
| `HybridCacheProvider` | Memory (L1) + Redis (L2) coordinator | Best of both worlds |
| `TenantPartitionedCacheProvider` | Decorator for multi-tenant key isolation | SaaS applications |

## Quick Start

```csharp
// 1. Add packages
// dotnet add package EricksonLopez.Caching
// dotnet add package EricksonLopez.Caching.Redis (optional)

// 2. Register (Program.cs)
builder.Services.AddMemoryCacheProvider();
builder.Services.AddCacheInvalidator();

// 3. Inject and use
public class ProductService(ICacheProvider cache)
{
    public async Task<Product> GetProductAsync(int id, CancellationToken ct)
    {
        var result = await cache.GetOrCreateAsync(
            key: $"product:{id}",
            factory: ct => db.FindProductAsync(id, ct),
            options: CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(30)));

        return result.IsSuccess ? result.Value! : throw new Exception(result.Error.Message);
    }
}
```

## Redis Setup

```csharp
builder.Services.AddRedisCaching(options =>
{
    options.Configuration = "your-redis:6379";
    options.InstanceName = "myapp";
    options.FailOpen = true; // degrade gracefully when Redis is unavailable
});
```

## Hybrid Cache (L1 + L2)

```csharp
builder.Services.AddMemoryCacheProvider();
builder.Services.AddRedisCaching(o => o.Configuration = "redis:6379");
builder.Services.AddHybridCache(
    localCacheFactory: sp => sp.GetRequiredService<MemoryCacheProvider>(),
    distributedCacheFactory: sp => sp.GetRequiredService<RedisCacheProvider>());
```

## Multi-Tenant

```csharp
builder.Services.AddMemoryCacheProvider();
builder.Services.AddTenantPartitionedCache(sp =>
{
    var ctx = sp.GetRequiredService<IHttpContextAccessor>();
    return ctx.HttpContext?.User.FindFirstValue("tenant_id");
});
```

## Tag-Based Invalidation

```csharp
// Cache with tags
ITaggedCacheProvider tagged = cache;
await tagged.SetWithTagsAsync("product:list", list,
    tags: new[] { "catalog", "products" },
    options: CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(10)));

// Invalidate all "catalog" entries on product update
var invalidator = new CacheInvalidator(cache);
await invalidator.InvalidateByTagAsync("catalog");
```

## Documentation

| Document | Description |
|---|---|
| [Quick Start](quick-start.md) | Get up and running in 5 minutes |
| [API Inventory](api-inventory.md) | Complete public API surface |
| [Functional Map](functional-map.md) | Architecture + Mermaid diagrams |
| [Cookbook](cookbook.md) | 15 recipes for common scenarios |
| [API Reference](api-reference.md) | Method-level documentation |
| [Best Practices](best-practices.md) | Performance and design guidelines |
| [FAQ](faq.md) | Common questions and answers |
| [Architecture Guide](../architectural-justification.md) | Design decisions |

## Running the Showcase

The `samples/EricksonLopez.Caching.Sample` project is the **official executable reference** — 100% of the public API is demonstrated and validated:

```bash
dotnet run --project samples/EricksonLopez.Caching.Sample/EricksonLopez.Caching.Sample.csproj
```

Output: 11 levels (0–10), ~80 assertions, exit code 0.

## License

Copyright © Erickson Lopez. MIT License.

