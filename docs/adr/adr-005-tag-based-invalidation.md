# ADR-005: Tag-Based Invalidation Strategy

> **Sequence:** ← [ADR-004: L1/L2 TTL Separation & Graceful Degradation](./adr-004-l1-l2-ttl-separation.md) | **Next:** [ADR-006: Exclusion of Redis Pub/Sub Backplane from Core](./adr-006-backplane-excluded-from-core.md) →

## Status
Accepted (2026-09-03)

## Date
2026-09-03

## Context
In Domain-Driven Design (DDD) and CQRS architectures, an aggregate change typically invalidates multiple distinct cache projections. For example, updating `Product 100` might affect:
- `products:details:100`
- `catalog:featured:category:electronics`
- `tenant:acme:pricing:100`

Using prefix-based invalidation (`RemoveByPrefixAsync`, established in [ADR-001](./adr-001-cache-architecture.md)) requires either:
1. Artificial and rigid key structures that artificially force common prefixes.
2. The command handler knowing all possible read-model key conventions across different microservices or query handlers.

Both approaches tightly couple write models to read projections and violate domain boundaries. A multi-dimensional grouping mechanism is required.

## Decision
We establish tag-based invalidation across the caching ecosystem through the `ITaggedCacheProvider` interface and update `ICacheInvalidator`.

### 1. The `ITaggedCacheProvider` Contract
```csharp
public interface ITaggedCacheProvider : ICacheProvider
{
    Task<Result<bool>> SetWithTagsAsync<T>(string key, T value, IEnumerable<string> tags, CacheEntryOptions? options = null, CancellationToken cancellationToken = default);
    Task<Result<bool>> RemoveByTagAsync(string tag, CancellationToken cancellationToken = default);
    Task<Result<bool>> RemoveByTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default);
}
```

### 2. Implementation in `MemoryCacheProvider`
In-memory caching implements tag associations using a concurrent inverted index:
- A `ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>` maps each tag to the set of cache keys tagged with it.
- When `RemoveByTagAsync` is invoked, the tag set is removed atomically, and each corresponding entry is evicted from the cache entries and locks dictionary.

### 3. Implementation in `RedisCacheProvider` (Redis `SET` + Non-Blocking Lua Script)
Distributed caching indexes tags using native Redis sets:
- When a key is cached with tags, each tag generates a Redis Set: `tag:{tag}` containing the physical cache keys via `SADD`.
- Tag invalidation executes an atomic, non-blocking Lua script:
```lua
local members = redis.call('SMEMBERS', KEYS[1])
local deleted = 0
if #members > 0 then
    deleted = redis.call('UNLINK', unpack(members))
end
redis.call('DEL', KEYS[1])
return deleted
```
Using `UNLINK` instead of `DEL` ensures that memory reclamation happens asynchronously in background threads, avoiding blocking the Redis event loop even if thousands of keys are tagged.

### 4. Integration with `ICacheInvalidator`
`ICacheInvalidator` includes `InvalidateByTagAsync` and `InvalidateByTagsAsync`, allowing application command handlers to trigger tag invalidation via dependency injection without depending directly on the underlying storage provider.

## Consequences

### Positive
- **Domain Decoupling**: Command handlers tag entities by identifier (e.g. `tag:product:100`) without needing to know downstream cache key formats.
- **Atomic Multi-Key Eviction**: All projections associated with an entity are evicted in a single atomic operation.
- **Non-Blocking Redis Performance**: Utilizing Redis `UNLINK` guarantees sub-millisecond execution times without stalling Redis threads.

### Negative
- Tag indices require small auxiliary memory storage (a Redis set per active tag). Tag sets are set to expire at `entry TTL + 1 hour` when a cache entry has a TTL. **Important**: if an entry is stored without an absolute or sliding TTL (and `RedisCacheOptions.DefaultAbsoluteExpiration` is zero or not configured), the tag set in Redis will not receive an expiry and will persist indefinitely. In standard configurations, `DefaultAbsoluteExpiration` defaults to 5 minutes, so tag sets always expire automatically.

---
*Navigation: ← [ADR-004: L1/L2 TTL Separation & Graceful Degradation](./adr-004-l1-l2-ttl-separation.md) | [ADR Index](./README.md) | Next: [ADR-006: Exclusion of Redis Pub/Sub Backplane from Core](./adr-006-backplane-excluded-from-core.md) →*
