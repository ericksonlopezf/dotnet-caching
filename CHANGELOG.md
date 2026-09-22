# Changelog

All notable changes to the `EricksonLopez.Caching` ecosystem will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.0] - 2026-09-21

### Breaking Changes

- **BC-001: Strict Precondition Enforcement on Prefix and Tag Invalidation (`RemoveByPrefixAsync`, `RemoveByTagAsync`)**
  - **What Changed**: `RemoveByPrefixAsync` and `RemoveByTagAsync` now throw an `ArgumentException` if the provided key prefix or tag is `null`, empty string (`""`), or composed solely of whitespace (`"   "`).
  - **Previous State**: In initial prototype builds, `RemoveByPrefixAsync` executed `ArgumentNullException.ThrowIfNull(prefix)`. Passing `""` or whitespace was accepted without exception and matched all keys via `string.StartsWith("", StringComparison.Ordinal)`, inadvertently wiping out the entire keyspace.
  - **Current State**: `ArgumentException.ThrowIfNullOrWhiteSpace(prefix)` and `ArgumentException.ThrowIfNullOrWhiteSpace(tag)` are enforced across `MemoryCacheProvider`, `HybridCacheProvider`, and `RedisCacheProvider`.
  - **Affected Consumers**: Callers who previously passed empty or whitespace strings to clear the cache or who relied on lenient parameter validation.
  - **Migration Guidance**: Do not pass empty strings to `RemoveByPrefixAsync`. To perform scoped invalidation, supply an explicit, non-whitespace domain prefix (e.g. `"catalog:"`, `"tenant:42:"`). To invalidate groups of entries across keyspaces, use `RemoveByTagsAsync` via `ITaggedCacheProvider`.

- **BC-002: Fault-Tolerant Degraded Consistency Semantics in `HybridCacheProvider.SetAsync`**
  - **What Changed**: `HybridCacheProvider.SetAsync` now returns `Result<bool>.Success(true)` if either the L1 local in-memory cache OR the L2 distributed Redis cache write succeeds. Non-cancellation exceptions from L2 (e.g., Redis timeouts, socket disconnects) are swallowed and recorded to telemetry rather than propagating as uncaught exceptions.
  - **Previous State**: In initial prototype builds, `HybridCacheProvider.SetAsync` required both operations to succeed (`l1Set.IsSuccess && l2Set.IsSuccess`). An exception during L2 distributed write propagated directly to the caller, failing the operation.
  - **Current State**: Under ADR-004, `SetAsync` implements graceful degradation (`Result<bool>.Success(l1Set.IsSuccess || l2Set.IsSuccess)`). L2 failures increment the `cache.errors` metric with tag `"L2_SET_FAILED"`, allowing the application node to continue operating with local L1 cache during remote cache outages.
  - **Affected Consumers**: Callers requiring guaranteed cluster-wide consistency confirmation on every write.
  - **Migration Guidance**: If an application flow strictly requires verification that distributed L2 persistence succeeded, resolve `ICacheProvider` directly from the Redis provider (`RedisCacheProvider`) or monitor the `cache.errors` OpenTelemetry metric for degraded execution events.

- **BC-003: Preservation of Binary Compatibility for `RedisCacheProvider` Constructors**
  - **What Changed**: Added an explicit 4-parameter constructor overload to `RedisCacheProvider` to preserve binary compatibility alongside the new 5-parameter constructor accepting a `TimeProvider`.
  - **Previous State**: The constructor accepted 4 parameters: `(IConnectionMultiplexer, IOptions<RedisCacheOptions>, ILogger<RedisCacheProvider>, ICacheSerializer? = null)`.
  - **Current State**: The 4-parameter constructor is preserved as an overload delegating to the new 5-parameter constructor `(IConnectionMultiplexer, IOptions<RedisCacheOptions>, ILogger<RedisCacheProvider>, ICacheSerializer?, TimeProvider?)`.
  - **Affected Consumers**: Pre-compiled assemblies referencing the 4-parameter constructor across assembly boundaries without recompilation.
  - **Migration Guidance**: No code changes or re-compilations are required for existing consumers. To supply a custom or simulated clock for unit testing, consumers can now invoke the 5-parameter constructor overload passing a `TimeProvider` (such as `FakeTimeProvider`).

- **BC-004: Native AOT Reflection Safety Constraint on Default JSON Serializer (`SystemTextJsonCacheSerializer.Default`)**
  - **What Changed**: `SystemTextJsonCacheSerializer.Default` utilizes reflection-based `System.Text.Json` serialization, which triggers trimmer/AOT warnings (`IL2026`, `IL3050`) suppressed via `[UnconditionalSuppressMessage]`. Under Native AOT compilation (`PublishAot=true`), using the default serializer will fail at runtime unless a source-generated context is provided.
  - **Previous State**: Unconstrained reflection serialization.
  - **Current State**: In accordance with ADR-002, `ICacheSerializer` is fully pluggable. While reflection is permitted in JIT environments via `SystemTextJsonCacheSerializer.Default`, Native AOT environments require an explicit `JsonSerializerContext`.
  - **Affected Consumers**: Consumers publishing applications with Native AOT who use default Redis cache registration.
  - **Migration Guidance**: In Native AOT projects, define a `JsonSerializerContext` source generator (e.g. `[JsonSerializable(typeof(MyDto))]`) and register a custom serializer instance via `services.Configure<RedisCacheOptions>(opt => opt.Serializer = new SystemTextJsonCacheSerializer(MyJsonContext.Default.Options))` before calling `AddRedisCaching()`.

- **BC-005: Strict Duration and Ceiling Preconditions in `CacheEntryOptions` and `HybridCacheEntryOptions`**
  - **What Changed**: Setting or constructing options with non-positive durations (`<= TimeSpan.Zero`) or durations exceeding the maximum ceiling of 365 days (`MaxAllowedDuration`) now throws an `ArgumentOutOfRangeException`.
  - **Previous State**: Property setters (`AbsoluteExpirationRelativeToNow`, `SlidingExpiration`, `FailSafeMaxStale`, `LocalCacheDuration`, `DistributedCacheDuration`) and factory helpers accepted any `TimeSpan?` without validation, including negative, zero, or multi-year values.
  - **Current State**: `ValidateDuration` is strictly enforced across property setters and factory methods (`FromAbsolute`, `FromSliding`, `FromAbsoluteWithFailSafe`, `FromDurations`).
  - **Affected Consumers**: Callers passing `TimeSpan.Zero` to indicate immediate expiration or disable caching, or specifying long-term durations exceeding 365 days.
  - **Migration Guidance**: Ensure all configured TTL durations are strictly positive (`duration > TimeSpan.Zero`) and within 365 days. To bypass caching, do not invoke cache setters with zero TTL; instead, bypass the cache or explicitly call `RemoveAsync(key)`.

- **BC-006: Preservation of Binary Compatibility for `HybridCacheProvider` Constructors**
  - **What Changed**: Retained the original 2-parameter constructor `HybridCacheProvider(ICacheProvider, ICacheProvider)` as a delegating overload alongside the newly introduced 3-parameter constructor `HybridCacheProvider(ICacheProvider, ICacheProvider, bool failOpenOnRemoval)`.
  - **Previous State**: Constructor accepted exactly 2 parameters: `(ICacheProvider localCache, ICacheProvider distributedCache)`.
  - **Current State**: The 2-parameter constructor is preserved as a dedicated overload defaulting `failOpenOnRemoval = true`, maintaining full binary compatibility and avoiding `MissingMethodException` on runtime binding.
  - **Affected Consumers**: Pre-compiled assemblies referencing the 2-parameter constructor across assembly boundaries without recompilation.
  - **Migration Guidance**: No code changes or recompilations are required for existing consumers. Callers requiring strict distributed removal error propagation can opt into the 3-parameter overload by specifying `failOpenOnRemoval: false`.

- **BC-007: Strict Pre-Execution Cancellation Contract Enforcement Across All Providers**
  - **What Changed**: Methods across `MemoryCacheProvider`, `RedisCacheProvider`, and `HybridCacheProvider` now evaluate `cancellationToken.ThrowIfCancellationRequested()` immediately upon entry before performing cache lookups, lock acquisitions, or background operations.
  - **Previous State**: Synchronous in-memory lookups in `MemoryCacheProvider.GetAsync` ignored pre-cancelled tokens and returned cached values if present in the internal dictionary.
  - **Current State**: Passing an already-cancelled `CancellationToken` unconditionally throws an `OperationCanceledException` prior to inspection or mutation of cache state.
  - **Affected Consumers**: Callers invoking cache read operations with pre-cancelled tokens expecting synchronous hits to succeed.
  - **Migration Guidance**: Ensure that active tokens are not pre-cancelled prior to calling cache APIs, or catch `OperationCanceledException` if cancellation is an expected workflow event.

- **BC-008: Structured Functional Failure Propagation on Deserialization Failure in `RedisCacheProvider.GetAsync`**
  - **What Changed**: When JSON deserialization of a Redis payload fails, `RedisCacheProvider.GetAsync` catches the deserialization exception, emits telemetry metric `cache.errors` with error code `"DESERIALIZATION_FAILED"`, and returns a structured functional failure result (`Result<T?>.Failure(CacheErrors.DeserializationFailed(key, detail))`) instead of allowing raw deserialization exceptions to propagate.
  - **Previous State**: Deserialization exceptions (such as `System.Text.Json.JsonException`) escaped unhandled from `ExecuteWithRetryAsync` and crashed caller threads.
  - **Current State**: Controlled Railway-Oriented Programming return with error code `"Cache.Redis.DeserializationFailed"`.
  - **Affected Consumers**: Callers wrapping `GetAsync` in `try / catch (JsonException)` blocks rather than checking `result.IsFailure`.
  - **Migration Guidance**: Inspect the returned `Result<T?>` instance via `result.IsSuccess` or `result.IsFailure` and check `result.Error.Code == "Cache.Redis.DeserializationFailed"` to handle corrupt or schema-incompatible cache entries.

- **BC-009: Dependency Injection Service Replacement Guard via `TryAddSingleton` in `AddMemoryCacheProvider`**
  - **What Changed**: `AddMemoryCacheProvider` now registers `ICacheProvider` and `ITaggedCacheProvider` using `services.TryAddSingleton(...)` rather than `services.AddSingleton(...)`.
  - **Previous State**: Calling `AddMemoryCacheProvider()` unconditionally appended or replaced the primary resolution target for `ICacheProvider`.
  - **Current State**: If an `ICacheProvider` implementation has already been registered in the service collection prior to calling `AddMemoryCacheProvider()`, that prior registration is preserved and will not be overwritten by `MemoryCacheProvider`.
  - **Affected Consumers**: Applications registering custom cache providers or test doubles before invoking `AddMemoryCacheProvider()`.
  - **Migration Guidance**: If the application requires `MemoryCacheProvider` to take precedence over previously registered providers, register `MemoryCacheProvider` first, or register the desired provider explicitly using `services.AddSingleton<ICacheProvider, MemoryCacheProvider>()`.

- **BC-010: Configurable Distributed Removal Failure Propagation in `HybridCacheProvider` (`failOpenOnRemoval = false`)**
  - **What Changed**: Added configurable `failOpenOnRemoval` semantics in `HybridCacheProvider`. When configured with `failOpenOnRemoval: false`, failures in the distributed L2 tier during `RemoveManyAsync` or `RemoveByTagAsync` return `Result.Failure` rather than returning a swallowed `Result.Success`.
  - **Previous State**: L2 removal failures were unconditionally caught and converted to `Result<bool>.Success(true)` (fail-open).
  - **Current State**: Callers can choose between default fail-open (`true`) and strict distributed consistency (`false`).
  - **Affected Consumers**: Callers opting into strict distributed invalidation consistency.
  - **Migration Guidance**: Leave `failOpenOnRemoval` at its default value (`true`) to preserve resilient graceful degradation, or supply `failOpenOnRemoval: false` if your application requires confirmation that distributed cache keys were deleted.

- **BC-011: Public Interface Expansion with Default Interface Methods (DIM) in `ICacheProvider` and `ICacheInvalidator`**
  - **What Changed**: Expanded `ICacheProvider` with default interface methods (`GetManyAsync`, `SetManyAsync`, `RemoveManyAsync`, `ExistsAsync`, `ExpireAsync`, `RefreshAsync`) and `ICacheInvalidator` with default interface methods (`InvalidateAsync`, `InvalidateManyAsync`).
  - **Previous State**: Interfaces defined only single-key and prefix/tag operations.
  - **Current State**: New batch and lifecycle methods are available directly on the interface, implemented via default interface methods (DIM) delegating to single-item primitives.
  - **Affected Consumers**: Test mock setups using mocking frameworks that do not mock default interface methods by default, or custom providers inheriting the interface that inspect members via reflection without DIM awareness.
  - **Migration Guidance**: Update custom provider implementations to provide optimized batch overrides for `GetManyAsync`, `SetManyAsync`, and `RemoveManyAsync`. In unit tests, mock the concrete provider class or configure interface mock setups for newly added methods if invoked.

### Changed
- **`CacheEntryOptions` Unsealed for Extensibility**:
  - `CacheEntryOptions` changed from `sealed class` to `class` to allow `HybridCacheEntryOptions` to inherit common TTL properties (`AbsoluteExpirationRelativeToNow`, `SlidingExpiration`, `FailSafeMaxStale`, `Tags`). This is a non-breaking binary and source expansion.
- **Service Registration Enhancements**:
  - `AddMemoryCacheProvider()` now registers the underlying provider as both `ICacheProvider` and `ITaggedCacheProvider`, allowing consumers to resolve tagged invalidation capabilities seamlessly.
- **Distributed Retry Loop with Full Jitter**:
  - `RedisCacheProvider.ExecuteWithRetryAsync` upgraded with exponential backoff and randomized full jitter to prevent retry storms during transient network disconnects.
- **Memory Keyed Synchronization Leak Prevention**:
  - `MemoryCacheProvider` upgraded from unbounded `SemaphoreSlim` allocations to reference-counted `LockHolder` primitives, actively disposing locks when all concurrent waiters complete.

### Added
- **`EricksonLopez.Caching` Core Engine**:
  - `ICacheProvider` contract returning `Result<T>` and `Result<bool>` for robust functional control flow.
  - In-process stampede protection via keyed `SemaphoreSlim` single-flight locking in `MemoryCacheProvider`.
  - Multi-tier coordination in `HybridCacheProvider` combining fast local L1 in-memory cache with distributed L2 Redis cache.
  - Multi-dimensional tag-based invalidation via `ITaggedCacheProvider` with atomic Redis `UNLINK` set cleanup and thread-safe memory indices.
  - Independent L1/L2 time-to-live settings via `HybridCacheEntryOptions` (`LocalCacheDuration`, `DistributedCacheDuration`).
  - Strict precondition safety rejecting null, empty, or whitespace prefixes and tags (`ArgumentException.ThrowIfNullOrWhiteSpace`), preventing inadvertent full cache purges.
  - Built-in fail-safe stale cache serving window (`FailSafeMaxStale`) for resilient fallback during underlying factory delegate failures.
  - Pluggable serialization abstraction via `ICacheSerializer` with `SystemTextJsonCacheSerializer` default implementation and Native AOT source-generated `JsonSerializerContext` support.
  - Domain command invalidation port `ICacheInvalidator` and `CacheInvalidator` implementation, with `AddCacheInvalidator()` dependency injection extensions.
  - In-box OpenTelemetry metric instrumentation via `System.Diagnostics.Metrics` (`Meter`: `"EricksonLopez.Caching"`) tracking `cache.hits`, `cache.misses`, `cache.stampedes_prevented`, `cache.evictions`, and `cache.errors`.
  - OpenTelemetry distributed tracing via `System.Diagnostics.ActivitySource` (`ActivitySourceName = "EricksonLopez.Caching"`) generating standard spans for all cache operations.
  - In-memory cache options via `MemoryCacheOptions` supporting LRU capacity limits (`MaxCapacity`), compaction percentage (`CompactPercentage`), and active background sweeps (`ExpirationScanFrequency`).
  - Active background cache sweeper `MemoryCacheProvider.RemoveExpiredEntries()` for deterministic memory reclamation.
  - Tenant isolation decorator `TenantPartitionedCacheProvider` and `AddTenantPartitionedCache()` extension enforcing multi-tenant keyspace partitioning.
  - Batch operations `GetManyAsync`, `SetManyAsync`, `RemoveManyAsync`, and `InvalidateManyAsync`.
  - Key status inspection: `ExistsAsync`, `ExpireAsync`, and `RefreshAsync`.
  - Strict Native AOT compatibility (`IsAotCompatible=true`, `EnableTrimAnalyzer=true`) with zero reflection across core providers.
  - First-class multi-targeting across `.NET 8.0`, `.NET 9.0`, and `.NET 10.0`.
- **`EricksonLopez.Caching.Redis` Distributed Provider**:
  - High-performance distributed caching on `StackExchange.Redis`.
  - Distributed stampede protection in `RedisCacheProvider.GetOrCreateAsync` via atomic Redis `SETNX` lock leases, random cryptographic token validation, and non-blocking Lua release scripts.
  - Self-contained retry loop governed by `MaxRetries` and `RetryDelay` with full randomized jitter for transient network partitions.
  - Non-blocking prefix deletion using batched Redis Lua scripts (`SCAN` + `UNLINK`).
  - Non-blocking tag deletion using batched Redis Lua scripts (`SMEMBERS` + `UNLINK`).
  - Fail-open resilience mode via `RedisCacheOptions.FailOpen` enabling read fallback to cache miss during Redis cluster outages.
  - Deferred connection multiplexer factory registration via `AddRedisCaching(Func<IServiceProvider, IConnectionMultiplexer>)`.
  - Zero-allocation high-performance structured logging with compile-time `[LoggerMessage]` source generation.
  - Strongly-typed service collection extensions `AddRedisCaching()` with connection string or existing `IConnectionMultiplexer` integration.

[1.0.0]: https://github.com/ericksonlopezf/dotnet-caching/releases/tag/v1.0.0
