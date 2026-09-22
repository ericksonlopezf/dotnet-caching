// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Result;

namespace EricksonLopez.Caching;

/// <summary>
/// Multi-tier hybrid cache coordinating an in-memory L1 cache with a distributed L2 cache.
/// Provides tiered TTL configuration, graceful degradation upon L2 failure, and tag-based invalidation.
/// </summary>
public sealed class HybridCacheProvider : ITaggedCacheProvider
{
    private const string ProviderName = "hybrid";

    private readonly ICacheProvider _localCache;
    private readonly ICacheProvider _distributedCache;
    private readonly bool _failOpenOnRemoval;

    /// <summary>
    /// Initializes a new instance of the <see cref="HybridCacheProvider"/> class with default fail-open removal semantics.
    /// Preserves binary backward compatibility with pre-compiled assemblies.
    /// </summary>
    /// <param name="localCache">The L1 fast local in-memory cache. Cannot be <see langword="null"/>.</param>
    /// <param name="distributedCache">The L2 shared distributed cache. Cannot be <see langword="null"/>.</param>
    public HybridCacheProvider(
        ICacheProvider localCache,
        ICacheProvider distributedCache)
        : this(localCache, distributedCache, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HybridCacheProvider"/> class with configurable removal fault tolerance.
    /// </summary>
    /// <param name="localCache">The L1 fast local in-memory cache. Cannot be <see langword="null"/>.</param>
    /// <param name="distributedCache">The L2 shared distributed cache. Cannot be <see langword="null"/>.</param>
    /// <param name="failOpenOnRemoval">
    /// If <see langword="true"/>, failures in L2 during removal/invalidation will be ignored and reported as success (fail-open).
    /// Defaults to <see langword="true"/> for backward compatibility, or <see langword="false"/> for strict distributed invalidation consistency.
    /// </param>
    public HybridCacheProvider(
        ICacheProvider localCache,
        ICacheProvider distributedCache,
        bool failOpenOnRemoval)
    {
        _localCache = localCache ?? throw new ArgumentNullException(nameof(localCache));
        _distributedCache = distributedCache ?? throw new ArgumentNullException(nameof(distributedCache));
        _failOpenOnRemoval = failOpenOnRemoval;
    }

    /// <inheritdoc />
    public async Task<Result<T?>> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("get", ProviderName, key);

        // 1. Fast L1 In-Memory lookup
        var l1Result = await _localCache.GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        if (l1Result.IsSuccess && l1Result.Value is not null)
        {
            CacheDiagnostics.RecordHit(ProviderName, "L1");
            return l1Result;
        }

        // Check if L1 has the key cached as null
        var l1Exists = await _localCache.ExistsAsync(key, cancellationToken).ConfigureAwait(false);
        if (l1Exists.IsSuccess && l1Exists.Value)
        {
            CacheDiagnostics.RecordHit(ProviderName, "L1");
            return l1Result;
        }

        // 2. Fallback to L2 Distributed lookup with graceful degradation
        Result<T?> l2Result;
        try
        {
            l2Result = await _distributedCache.GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CacheDiagnostics.RecordError(ProviderName, "L2_GET_FAILED");
            return Result<T?>.Success(default);
        }

        if (l2Result.IsSuccess && l2Result.Value is not null)
        {
            CacheDiagnostics.RecordHit(ProviderName, "L2");
            // Populate L1 cache with retrieved L2 value
            await _localCache.SetAsync(key, l2Result.Value, cancellationToken: cancellationToken).ConfigureAwait(false);
            return l2Result;
        }

        // Check if L2 has the key cached as null
        Result<bool> l2Exists;
        try
        {
            l2Exists = await _distributedCache.ExistsAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            l2Exists = Result<bool>.Success(false);
        }

        if (l2Exists.IsSuccess && l2Exists.Value)
        {
            CacheDiagnostics.RecordHit(ProviderName, "L2");
            await _localCache.SetAsync(key, default(T)!, cancellationToken: cancellationToken).ConfigureAwait(false);
            return Result<T?>.Success(default);
        }

        if (l2Result.IsFailure)
        {
            CacheDiagnostics.RecordError(ProviderName, l2Result.Error.Code);
            // Graceful degradation: treat L2 failure as cache miss
            return Result<T?>.Success(default);
        }

        CacheDiagnostics.RecordMiss(ProviderName);
        return Result<T?>.Success(default);
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

        var existing = await GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        if (existing.IsSuccess && existing.Value is not null)
        {
            return Result<T>.Success(existing.Value);
        }

        var existingExists = await ExistsAsync(key, cancellationToken).ConfigureAwait(false);
        if (existingExists.IsSuccess && existingExists.Value)
        {
            return Result<T>.Success(default!);
        }

        var (l1Options, l2Options) = SplitTierOptions(options);

        // Atomically coordinate via L1 GetOrCreate to prevent stampedes locally
        return await _localCache.GetOrCreateAsync(
            key,
            async ct =>
            {
                ct.ThrowIfCancellationRequested();

                // Re-check L2 distributed cache inside single-flight lock
                Result<T?> l2Check;
                try
                {
                    l2Check = await _distributedCache.GetAsync<T>(key, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    CacheDiagnostics.RecordError(ProviderName, "L2_RECHECK_FAILED");
                    l2Check = Result<T?>.Failure(new Error("Cache.L2Unavailable", ex.Message));
                }

                if (l2Check.IsSuccess && l2Check.Value is not null)
                {
                    return l2Check.Value;
                }

                try
                {
                    var l2CheckExists = await _distributedCache.ExistsAsync(key, ct).ConfigureAwait(false);
                    if (l2CheckExists.IsSuccess && l2CheckExists.Value)
                    {
                        return default!;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Proceed to factory
                }

                // Coordinate through L2's GetOrCreateAsync if available to leverage distributed stampede protection across cluster instances
                try
                {
                    var distResult = await _distributedCache.GetOrCreateAsync(key, factory, l2Options, ct).ConfigureAwait(false);
                    if (distResult.IsSuccess)
                    {
                        return distResult.Value;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    CacheDiagnostics.RecordError(ProviderName, "L2_GET_OR_CREATE_FAILED");
                }

                var value = await factory(ct).ConfigureAwait(false);

                // Populate L2 with resilience (do not fail the operation if L2 is unreachable)
                try
                {
                    await _distributedCache.SetAsync(key, value, l2Options, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    CacheDiagnostics.RecordError(ProviderName, "L2_SET_FAILED");
                }

                return value;
            },
            l1Options,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<bool>> SetAsync<T>(
        string key,
        T value,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("set", ProviderName, key);

        var (l1Options, l2Options) = SplitTierOptions(options);

        // Populate L2 with resilience
        Result<bool> l2Set;
        try
        {
            l2Set = await _distributedCache.SetAsync(key, value, l2Options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CacheDiagnostics.RecordError(ProviderName, "L2_SET_FAILED");
            l2Set = Result<bool>.Failure(new Error("Cache.L2Unavailable", ex.Message));
        }

        var l1Set = await _localCache.SetAsync(key, value, l1Options, cancellationToken).ConfigureAwait(false);

        // Return true if at least L1 or L2 succeeded (graceful degradation)
        return Result<bool>.Success(l1Set.IsSuccess || l2Set.IsSuccess);
    }

    /// <inheritdoc />
    public async Task<Result<bool>> SetWithTagsAsync<T>(
        string key,
        T value,
        IEnumerable<string> tags,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(tags);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("set_with_tags", ProviderName, key);

        var (l1Options, l2Options) = SplitTierOptions(options);

        if (_distributedCache is ITaggedCacheProvider taggedL2)
        {
            try
            {
                await taggedL2.SetWithTagsAsync(key, value, tags, l2Options, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                CacheDiagnostics.RecordError(ProviderName, "L2_TAG_SET_FAILED");
            }
        }
        else
        {
            await _distributedCache.SetAsync(key, value, l2Options, cancellationToken).ConfigureAwait(false);
        }

        if (_localCache is ITaggedCacheProvider taggedL1)
        {
            await taggedL1.SetWithTagsAsync(key, value, tags, l1Options, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _localCache.SetAsync(key, value, l1Options, cancellationToken).ConfigureAwait(false);
        }

        return Result<bool>.Success(true);
    }

    /// <inheritdoc />
    public async Task<Result<bool>> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("remove", ProviderName, key);

        Result<bool> l2Result = Result<bool>.Success(true);
        try
        {
            var res = await _distributedCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            if (res.IsFailure)
            {
                l2Result = res;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CacheDiagnostics.RecordError(ProviderName, "L2_REMOVE_FAILED");
            l2Result = Result<bool>.Failure(new Error("Cache.Hybrid.L2RemovalFailed", $"L2 removal failed for key '{key}': {ex.Message}"));
        }

        var l1Result = await _localCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);

        if (!_failOpenOnRemoval && l2Result.IsFailure)
        {
            return l2Result;
        }

        return Result<bool>.Success(true);
    }

    /// <inheritdoc />
    public async Task<Result<bool>> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("remove_by_prefix", ProviderName, prefix);

        Result<bool> l2Result = Result<bool>.Success(true);
        try
        {
            var res = await _distributedCache.RemoveByPrefixAsync(prefix, cancellationToken).ConfigureAwait(false);
            if (res.IsFailure)
            {
                l2Result = res;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CacheDiagnostics.RecordError(ProviderName, "L2_PREFIX_REMOVE_FAILED");
            l2Result = Result<bool>.Failure(new Error("Cache.Hybrid.L2PrefixRemovalFailed", $"L2 prefix removal failed for prefix '{prefix}': {ex.Message}"));
        }

        var l1Result = await _localCache.RemoveByPrefixAsync(prefix, cancellationToken).ConfigureAwait(false);

        if (!_failOpenOnRemoval && l2Result.IsFailure)
        {
            return l2Result;
        }

        return Result<bool>.Success(true);
    }

    /// <inheritdoc />
    public async Task<Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("exists", ProviderName, key);

        var l1Exists = await _localCache.ExistsAsync(key, cancellationToken).ConfigureAwait(false);
        if (l1Exists.IsSuccess && l1Exists.Value)
        {
            return l1Exists;
        }

        try
        {
            return await _distributedCache.ExistsAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CacheDiagnostics.RecordError(ProviderName, "L2_EXISTS_FAILED");
            return Result<bool>.Success(false);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyDictionary<string, T>>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("get_many", ProviderName, "multiple");

        var keyList = keys.Where(k => !string.IsNullOrEmpty(k)).Distinct().ToList();
        var result = new Dictionary<string, T>(StringComparer.Ordinal);

        // 1. Fast L1 lookup
        var l1Result = await _localCache.GetManyAsync<T>(keyList, cancellationToken).ConfigureAwait(false);
        if (l1Result.IsSuccess)
        {
            foreach (var kvp in l1Result.Value)
            {
                result[kvp.Key] = kvp.Value;
            }
        }

        var missingKeys = keyList.Where(k => !result.ContainsKey(k)).ToList();
        if (missingKeys.Count == 0)
        {
            return Result<IReadOnlyDictionary<string, T>>.Success(result);
        }

        // 2. L2 fallback for missing keys
        Result<IReadOnlyDictionary<string, T>> l2Result;
        try
        {
            l2Result = await _distributedCache.GetManyAsync<T>(missingKeys, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CacheDiagnostics.RecordError(ProviderName, "L2_GET_MANY_FAILED");
            return Result<IReadOnlyDictionary<string, T>>.Success(result);
        }

        if (l2Result.IsSuccess && l2Result.Value.Count > 0)
        {
            var itemsToPopulateL1 = new List<KeyValuePair<string, T>>();
            foreach (var kvp in l2Result.Value)
            {
                result[kvp.Key] = kvp.Value;
                itemsToPopulateL1.Add(kvp);
            }

            if (itemsToPopulateL1.Count > 0)
            {
                await _localCache.SetManyAsync(itemsToPopulateL1, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }

        return Result<IReadOnlyDictionary<string, T>>.Success(result);
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

        var itemList = items.ToList();
        var (l1Options, l2Options) = SplitTierOptions(options);

        // Set L2
        try
        {
            await _distributedCache.SetManyAsync(itemList, l2Options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CacheDiagnostics.RecordError(ProviderName, "L2_SET_MANY_FAILED");
        }

        // Set L1
        await _localCache.SetManyAsync(itemList, l1Options, cancellationToken).ConfigureAwait(false);

        return Result<bool>.Success(true);
    }

    /// <inheritdoc />
    public async Task<Result<long>> RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("remove_many", ProviderName, "multiple");

        var keyList = keys.ToList();

        Result<long> l2Result = Result<long>.Success(0);
        try
        {
            l2Result = await _distributedCache.RemoveManyAsync(keyList, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CacheDiagnostics.RecordError(ProviderName, "L2_REMOVE_MANY_FAILED");
            l2Result = Result<long>.Failure(new Error("Cache.Hybrid.L2RemovalFailed", $"L2 bulk removal failed: {ex.Message}"));
        }

        var l1Result = await _localCache.RemoveManyAsync(keyList, cancellationToken).ConfigureAwait(false);

        if (!_failOpenOnRemoval && l2Result.IsFailure)
        {
            return l2Result;
        }

        long count = Math.Max(l1Result.IsSuccess ? l1Result.Value : 0, l2Result.IsSuccess ? l2Result.Value : 0);
        return Result<long>.Success(count);
    }

    /// <inheritdoc />
    public async Task<Result<bool>> ExpireAsync(string key, TimeSpan timeToLive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("expire", ProviderName, key);

        try
        {
            await _distributedCache.ExpireAsync(key, timeToLive, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CacheDiagnostics.RecordError(ProviderName, "L2_EXPIRE_FAILED");
        }

        return await _localCache.ExpireAsync(key, timeToLive, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<bool>> RefreshAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("refresh", ProviderName, key);

        try
        {
            await _distributedCache.RefreshAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CacheDiagnostics.RecordError(ProviderName, "L2_REFRESH_FAILED");
        }

        return await _localCache.RefreshAsync(key, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<bool>> RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("remove_by_tag", ProviderName, tag);

        Result<bool> l2Result = Result<bool>.Success(true);
        if (_distributedCache is ITaggedCacheProvider taggedL2)
        {
            try
            {
                var res = await taggedL2.RemoveByTagAsync(tag, cancellationToken).ConfigureAwait(false);
                if (res.IsFailure)
                {
                    l2Result = res;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                CacheDiagnostics.RecordError(ProviderName, "L2_TAG_REMOVE_FAILED");
                l2Result = Result<bool>.Failure(new Error("Cache.Hybrid.L2TagRemovalFailed", $"L2 tag removal failed for tag '{tag}': {ex.Message}"));
            }
        }

        var l1Result = Result<bool>.Success(true);
        if (_localCache is ITaggedCacheProvider taggedL1)
        {
            l1Result = await taggedL1.RemoveByTagAsync(tag, cancellationToken).ConfigureAwait(false);
        }

        if (!_failOpenOnRemoval && l2Result.IsFailure)
        {
            return l2Result;
        }

        return Result<bool>.Success(true);
    }

    /// <inheritdoc />
    public async Task<Result<bool>> RemoveByTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tags);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("remove_by_tags", ProviderName, "multiple");

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

    private static (CacheEntryOptions? L1, CacheEntryOptions? L2) SplitTierOptions(CacheEntryOptions? options)
    {
        if (options is not HybridCacheEntryOptions hybrid)
        {
            return (options, options);
        }

        var l1 = new CacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = hybrid.LocalCacheDuration ?? hybrid.AbsoluteExpirationRelativeToNow,
            SlidingExpiration = hybrid.SlidingExpiration,
            FailSafeMaxStale = hybrid.FailSafeMaxStale,
            Tags = hybrid.Tags
        };

        var l2 = new CacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = hybrid.DistributedCacheDuration ?? hybrid.AbsoluteExpirationRelativeToNow,
            SlidingExpiration = hybrid.SlidingExpiration,
            FailSafeMaxStale = hybrid.FailSafeMaxStale,
            Tags = hybrid.Tags
        };

        return (l1, l2);
    }
}
