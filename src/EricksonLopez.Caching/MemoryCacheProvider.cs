// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Result;
using Microsoft.Extensions.Options;

namespace EricksonLopez.Caching;

/// <summary>
/// High-throughput, thread-safe in-memory cache provider with stampede protection,
/// tag-based eviction, fail-safe stale tolerance, LRU compaction, active background cleanup, and prefix invalidation.
/// </summary>
public sealed class MemoryCacheProvider : ITaggedCacheProvider, IDisposable
{
    private const string ProviderName = "memory";

    private readonly ConcurrentDictionary<string, MemoryCacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LockHolder> _locks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _tagKeys = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<string>> _keyTags = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly MemoryCacheOptions _options;
    private readonly ITimer? _cleanupTimer;

    private sealed class LockHolder : IDisposable
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int RefCount = 1;

        public void Dispose() => Semaphore.Dispose();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryCacheProvider"/> class with optional time provider.
    /// </summary>
    /// <param name="timeProvider">Optional time provider for unit testing expiration.</param>
    public MemoryCacheProvider(TimeProvider? timeProvider = null)
        : this((MemoryCacheOptions?)null, timeProvider)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryCacheProvider"/> class with Microsoft DI options.
    /// </summary>
    /// <param name="options">Configuration options wrapped in <see cref="IOptions{MemoryCacheOptions}"/>.</param>
    /// <param name="timeProvider">Optional time provider for unit testing expiration.</param>
    public MemoryCacheProvider(IOptions<MemoryCacheOptions>? options, TimeProvider? timeProvider = null)
        : this(options?.Value, timeProvider)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryCacheProvider"/> class with explicit configuration options.
    /// </summary>
    /// <param name="options">Configuration options.</param>
    /// <param name="timeProvider">Optional time provider for unit testing expiration.</param>
    public MemoryCacheProvider(MemoryCacheOptions? options, TimeProvider? timeProvider = null)
    {
        _options = options ?? new MemoryCacheOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (_options.ExpirationScanFrequency.HasValue)
        {
            _cleanupTimer = _timeProvider.CreateTimer(
                static state => ((MemoryCacheProvider)state!).RemoveExpiredEntries(),
                this,
                _options.ExpirationScanFrequency.Value,
                _options.ExpirationScanFrequency.Value);
        }
    }

    /// <inheritdoc />
    public Task<Result<T?>> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("get", ProviderName, key);

        var now = _timeProvider.GetUtcNow();

        if (!_entries.TryGetValue(key, out var entry))
        {
            CacheDiagnostics.RecordMiss(ProviderName, nameof(GetAsync));
            return Task.FromResult(Result<T?>.Success(default));
        }

        if (entry.IsExpired(now))
        {
            if (!entry.IsWithinStaleWindow(now))
            {
                _entries.TryRemove(key, out _);
                RemoveKeyFromTags(key);
                CacheDiagnostics.RecordEviction(ProviderName, "expired");
            }

            CacheDiagnostics.RecordMiss(ProviderName, nameof(GetAsync));
            return Task.FromResult(Result<T?>.Success(default));
        }

        entry.LastAccessedAt = now;

        if (entry.Value is T typedValue)
        {
            CacheDiagnostics.RecordHit(ProviderName, nameof(GetAsync));
            return Task.FromResult(Result<T?>.Success(typedValue));
        }

        if (entry.Value is null)
        {
            CacheDiagnostics.RecordHit(ProviderName, nameof(GetAsync));
            return Task.FromResult(Result<T?>.Success(default));
        }

        CacheDiagnostics.RecordMiss(ProviderName, nameof(GetAsync));
        return Task.FromResult(Result<T?>.Success(default));
    }

    /// <inheritdoc />
    public async Task<Result<T>> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("get_or_create", ProviderName, key);

        var now = _timeProvider.GetUtcNow();
        if (_entries.TryGetValue(key, out var initialEntry) && !initialEntry.IsExpired(now))
        {
            if (initialEntry.Value is T initialValue)
            {
                initialEntry.LastAccessedAt = now;
                CacheDiagnostics.RecordHit(ProviderName, nameof(GetOrCreateAsync));
                return Result<T>.Success(initialValue);
            }

            if (initialEntry.Value is null)
            {
                initialEntry.LastAccessedAt = now;
                CacheDiagnostics.RecordHit(ProviderName, nameof(GetOrCreateAsync));
                return Result<T>.Success(default!);
            }
        }

        LockHolder lockHolder;
        lock (_locks)
        {
            if (_locks.TryGetValue(key, out var existing))
            {
                existing.RefCount++;
                lockHolder = existing;
            }
            else
            {
                lockHolder = new LockHolder();
                _locks[key] = lockHolder;
            }
        }

        var lockAcquired = false;
        try
        {
            await lockHolder.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            lockAcquired = true;

            // Double-check inside synchronization lock
            now = _timeProvider.GetUtcNow();
            if (_entries.TryGetValue(key, out var doubleCheckEntry) && !doubleCheckEntry.IsExpired(now))
            {
                if (doubleCheckEntry.Value is T doubleCheckValue)
                {
                    doubleCheckEntry.LastAccessedAt = now;
                    CacheDiagnostics.RecordStampedePrevented(ProviderName);
                    return Result<T>.Success(doubleCheckValue);
                }

                if (doubleCheckEntry.Value is null)
                {
                    doubleCheckEntry.LastAccessedAt = now;
                    CacheDiagnostics.RecordStampedePrevented(ProviderName);
                    return Result<T>.Success(default!);
                }
            }

            try
            {
                var value = await factory(cancellationToken).ConfigureAwait(false);
                await SetAsync(key, value, options, cancellationToken).ConfigureAwait(false);
                return Result<T>.Success(value);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception) when (_entries.TryGetValue(key, out var staleEntry) &&
                                    staleEntry.IsWithinStaleWindow(_timeProvider.GetUtcNow()))
            {
                if (staleEntry.Value is T staleValue)
                {
                    // Fail-safe graceful degradation: serve stale value when factory fails
                    CacheDiagnostics.RecordHit(ProviderName, "fail-safe");
                    return Result<T>.Success(staleValue);
                }

                if (staleEntry.Value is null)
                {
                    CacheDiagnostics.RecordHit(ProviderName, "fail-safe");
                    return Result<T>.Success(default!);
                }

                CacheDiagnostics.RecordError(ProviderName, "FACTORY_FAILED");
                return Result<T>.Failure(new Error("Cache.Memory.FactoryFailed", $"Factory execution failed for cache key '{key}'."));
            }
            catch (Exception ex)
            {
                CacheDiagnostics.RecordError(ProviderName, "FACTORY_FAILED");
                return Result<T>.Failure(new Error("Cache.Memory.FactoryFailed", $"Factory execution failed for cache key '{key}': {ex.Message}"));
            }
        }
        finally
        {
            if (lockAcquired)
            {
                lockHolder.Semaphore.Release();
            }

            lock (_locks)
            {
                lockHolder.RefCount--;
                if (lockHolder.RefCount == 0)
                {
                    _locks.TryRemove(key, out _);
                    lockHolder.Semaphore.Dispose();
                }
            }
        }
    }

    /// <inheritdoc />
    public Task<Result<bool>> SetAsync<T>(
        string key,
        T value,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("set", ProviderName, key);

        if (_options.MaxCapacity.HasValue && !_entries.ContainsKey(key) && _entries.Count >= _options.MaxCapacity.Value)
        {
            RemoveExpiredEntries();

            if (_entries.Count >= _options.MaxCapacity.Value)
            {
                var countToEvict = Math.Max(1, (int)(_options.MaxCapacity.Value * _options.CompactPercentage));
                var victimKeys = _entries
                    .OrderBy(kvp => kvp.Value.LastAccessedAt)
                    .Take(countToEvict)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var victimKey in victimKeys)
                {
                    if (_entries.TryRemove(victimKey, out _))
                    {
                        RemoveKeyFromTags(victimKey);
                    }
                }

                if (victimKeys.Count > 0)
                {
                    CacheDiagnostics.RecordEviction(ProviderName, "capacity_compaction", victimKeys.Count);
                }
            }
        }

        var now = _timeProvider.GetUtcNow();
        DateTimeOffset? absolute = options?.AbsoluteExpirationRelativeToNow.HasValue == true
            ? now.Add(options.AbsoluteExpirationRelativeToNow.Value)
            : null;

        var entry = new MemoryCacheEntry(
            value: value,
            createdAt: now,
            absoluteExpiration: absolute,
            slidingExpiration: options?.SlidingExpiration,
            failSafeMaxStale: options?.FailSafeMaxStale);

        _entries[key] = entry;

        // Clean up any prior tag associations for this key to prevent tag leakage and phantom evictions on overwrite
        RemoveKeyFromTags(key);

        if (options?.Tags is not null)
        {
            var validTags = new List<string>();
            foreach (var tag in options.Tags)
            {
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    validTags.Add(tag);
                    var tagSet = _tagKeys.GetOrAdd(tag, static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
                    tagSet[key] = 0;
                }
            }

            if (validTags.Count > 0)
            {
                _keyTags[key] = validTags;
            }
        }

        return Task.FromResult(Result<bool>.Success(true));
    }

    /// <inheritdoc />
    public Task<Result<bool>> SetWithTagsAsync<T>(
        string key,
        T value,
        IEnumerable<string> tags,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(tags);
        cancellationToken.ThrowIfCancellationRequested();

        var tagList = tags.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();

        // Create a new options instance instead of mutating the caller's object (FIND-MED-002 fix)
        var mergedOptions = new CacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = options?.AbsoluteExpirationRelativeToNow,
            SlidingExpiration = options?.SlidingExpiration,
            FailSafeMaxStale = options?.FailSafeMaxStale,
            Tags = tagList
        };

        return SetAsync(key, value, mergedOptions, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<bool>> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("remove", ProviderName, key);

        var removed = _entries.TryRemove(key, out _);
        RemoveKeyFromTags(key);

        if (removed)
        {
            CacheDiagnostics.RecordEviction(ProviderName, "explicit");
        }

        return Task.FromResult(Result<bool>.Success(removed));
    }

    /// <inheritdoc />
    public Task<Result<bool>> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        cancellationToken.ThrowIfCancellationRequested();

        var keysToRemove = _entries.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

        foreach (var key in keysToRemove)
        {
            _entries.TryRemove(key, out _);
            RemoveKeyFromTags(key);
        }

        if (keysToRemove.Count > 0)
        {
            CacheDiagnostics.RecordEviction(ProviderName, "prefix", keysToRemove.Count);
        }

        return Task.FromResult(Result<bool>.Success(true));
    }

    /// <inheritdoc />
    public Task<Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("exists", ProviderName, key);

        var now = _timeProvider.GetUtcNow();
        var exists = _entries.TryGetValue(key, out var entry) && !entry.IsExpired(now);
        return Task.FromResult(Result<bool>.Success(exists));
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyDictionary<string, T>>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("get_many", ProviderName, "multiple");

        var now = _timeProvider.GetUtcNow();
        var result = new Dictionary<string, T>(StringComparer.Ordinal);

        foreach (var key in keys)
        {
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            if (_entries.TryGetValue(key, out var entry))
            {
                if (entry.IsExpired(now))
                {
                    if (!entry.IsWithinStaleWindow(now))
                    {
                        _entries.TryRemove(key, out _);
                        RemoveKeyFromTags(key);
                        CacheDiagnostics.RecordEviction(ProviderName, "expired");
                    }
                    CacheDiagnostics.RecordMiss(ProviderName, nameof(GetManyAsync));
                    continue;
                }

                entry.LastAccessedAt = now;
                if (entry.Value is T typedValue)
                {
                    result[key] = typedValue;
                    CacheDiagnostics.RecordHit(ProviderName, nameof(GetManyAsync));
                }
                else if (entry.Value is null)
                {
                    result[key] = default!;
                    CacheDiagnostics.RecordHit(ProviderName, nameof(GetManyAsync));
                }
                else
                {
                    CacheDiagnostics.RecordMiss(ProviderName, nameof(GetManyAsync));
                }
            }
            else
            {
                CacheDiagnostics.RecordMiss(ProviderName, nameof(GetManyAsync));
            }
        }

        return Task.FromResult(Result<IReadOnlyDictionary<string, T>>.Success(result));
    }

    /// <inheritdoc />
    public async Task<Result<bool>> SetManyAsync<T>(
        IEnumerable<KeyValuePair<string, T>> items,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("set_many", ProviderName, "multiple");

        foreach (var kvp in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SetAsync(kvp.Key, kvp.Value, options, cancellationToken).ConfigureAwait(false);
        }

        return Result<bool>.Success(true);
    }

    /// <inheritdoc />
    public Task<Result<long>> RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("remove_many", ProviderName, "multiple");

        long count = 0;
        foreach (var key in keys)
        {
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            if (_entries.TryRemove(key, out _))
            {
                RemoveKeyFromTags(key);
                count++;
            }
        }

        if (count > 0)
        {
            CacheDiagnostics.RecordEviction(ProviderName, "explicit_bulk", count);
        }

        return Task.FromResult(Result<long>.Success(count));
    }

    /// <inheritdoc />
    public Task<Result<bool>> ExpireAsync(string key, TimeSpan timeToLive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (timeToLive <= TimeSpan.Zero)
        {
            return Task.FromResult(Result<bool>.Failure(new Error("Cache.InvalidTimeToLive", "TimeToLive must be strictly positive.")));
        }
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("expire", ProviderName, key);

        var now = _timeProvider.GetUtcNow();
        if (_entries.TryGetValue(key, out var entry) && !entry.IsExpired(now))
        {
            entry.AbsoluteExpiration = now.Add(timeToLive);
            return Task.FromResult(Result<bool>.Success(true));
        }

        return Task.FromResult(Result<bool>.Success(false));
    }

    /// <inheritdoc />
    public Task<Result<bool>> RefreshAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("refresh", ProviderName, key);

        var now = _timeProvider.GetUtcNow();
        if (_entries.TryGetValue(key, out var entry) && !entry.IsExpired(now))
        {
            entry.LastAccessedAt = now;
            return Task.FromResult(Result<bool>.Success(true));
        }

        return Task.FromResult(Result<bool>.Success(false));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }

    /// <inheritdoc />
    public Task<Result<bool>> RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_tagKeys.TryRemove(tag, out var keySet))
        {
            return Task.FromResult(Result<bool>.Success(false));
        }

        var keys = keySet.Keys.ToList();
        foreach (var key in keys)
        {
            _entries.TryRemove(key, out _);
            RemoveKeyFromTags(key);
        }

        if (keys.Count > 0)
        {
            CacheDiagnostics.RecordEviction(ProviderName, "tag", keys.Count);
        }

        return Task.FromResult(Result<bool>.Success(true));
    }

    /// <inheritdoc />
    public async Task<Result<bool>> RemoveByTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tags);
        cancellationToken.ThrowIfCancellationRequested();

        var allSucceeded = true;
        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            var result = await RemoveByTagAsync(tag, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                allSucceeded = false;
            }
        }

        return Result<bool>.Success(allSucceeded);
    }

    /// <summary>
    /// Scans the internal cache storage and actively evicts all entries whose expiration duration has elapsed
    /// and are no longer eligible for stale fallback. Cleans up associated tag indices.
    /// </summary>
    /// <returns>The number of expired entries successfully evicted from memory.</returns>
    public int RemoveExpiredEntries()
    {
        var now = _timeProvider.GetUtcNow();
        var evictedCount = 0;

        foreach (var kvp in _entries)
        {
            var key = kvp.Key;
            var entry = kvp.Value;

            if (entry.IsExpired(now) && !entry.IsWithinStaleWindow(now))
            {
                if (_entries.TryRemove(key, out _))
                {
                    RemoveKeyFromTags(key);
                    evictedCount++;
                }
            }
        }

        if (evictedCount > 0)
        {
            CacheDiagnostics.RecordEviction(ProviderName, "expired_sweep", evictedCount);
        }

        foreach (var (tag, tagSet) in _tagKeys)
        {
            if (tagSet.IsEmpty)
            {
                _tagKeys.TryRemove(tag, out _);
            }
        }

        return evictedCount;
    }

    private void RemoveKeyFromTags(string key)
    {
        if (_keyTags.TryRemove(key, out var tags))
        {
            foreach (var tag in tags)
            {
                if (_tagKeys.TryGetValue(tag, out var tagSet))
                {
                    tagSet.TryRemove(key, out _);
                }
            }
        }
    }
}
