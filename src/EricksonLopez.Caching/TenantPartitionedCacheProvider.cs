// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Result;

namespace EricksonLopez.Caching;

/// <summary>
/// Partitioned cache provider decorator that enforces tenant isolation by automatically prefixing
/// all cache keys, tags, and bulk operations with the current tenant identifier.
/// Prevents cross-tenant data crossover and cache poisoning.
/// </summary>
public sealed class TenantPartitionedCacheProvider : ITaggedCacheProvider
{
    private readonly ICacheProvider _innerProvider;
    private readonly Func<string?> _tenantIdResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantPartitionedCacheProvider"/> class.
    /// </summary>
    /// <param name="innerProvider">The underlying cache provider. Cannot be <see langword="null"/>.</param>
    /// <param name="tenantIdResolver">Delegate resolving the current tenant identifier. Cannot be <see langword="null"/>.</param>
    public TenantPartitionedCacheProvider(ICacheProvider innerProvider, Func<string?> tenantIdResolver)
    {
        _innerProvider = innerProvider ?? throw new ArgumentNullException(nameof(innerProvider));
        _tenantIdResolver = tenantIdResolver ?? throw new ArgumentNullException(nameof(tenantIdResolver));
    }

    /// <inheritdoc/>
    public Task<Result<T?>> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        // Resolve tenant ID once per operation (FIND-HIGH-003 fix)
        var tenantId = _tenantIdResolver();
        return _innerProvider.GetAsync<T>(BuildPartitionedKey(tenantId, key), cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<T>> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        // Resolve tenant ID once per operation (FIND-HIGH-003 fix)
        var tenantId = _tenantIdResolver();
        var partitionedOptions = PartitionOptionsWithTenant(tenantId, options);
        return _innerProvider.GetOrCreateAsync(BuildPartitionedKey(tenantId, key), factory, partitionedOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<bool>> SetAsync<T>(
        string key,
        T value,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        // Resolve tenant ID once per operation (FIND-HIGH-003 fix)
        var tenantId = _tenantIdResolver();
        var partitionedOptions = PartitionOptionsWithTenant(tenantId, options);
        return _innerProvider.SetAsync(BuildPartitionedKey(tenantId, key), value, partitionedOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<bool>> SetWithTagsAsync<T>(
        string key,
        T value,
        IEnumerable<string> tags,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(tags);

        // Resolve tenant ID once per operation (FIND-HIGH-003 fix)
        var tenantId = _tenantIdResolver();
        var partitionedKey = BuildPartitionedKey(tenantId, key);
        var partitionedTags = tags.Select(t => BuildPartitionedTag(tenantId, t));
        var partitionedOptions = PartitionOptionsWithTenant(tenantId, options);

        if (_innerProvider is ITaggedCacheProvider tagged)
        {
            return tagged.SetWithTagsAsync(partitionedKey, value, partitionedTags, partitionedOptions, cancellationToken);
        }

        return _innerProvider.SetAsync(partitionedKey, value, partitionedOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<bool>> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var tenantId = _tenantIdResolver();
        return _innerProvider.RemoveAsync(BuildPartitionedKey(tenantId, key), cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<bool>> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        var tenantId = _tenantIdResolver();
        return _innerProvider.RemoveByPrefixAsync(BuildPartitionedKey(tenantId, prefix), cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<long>> RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var tenantId = _tenantIdResolver();
        var partitionedKeys = keys.Select(k => BuildPartitionedKey(tenantId, k));
        return _innerProvider.RemoveManyAsync(partitionedKeys, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyDictionary<string, T>>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        cancellationToken.ThrowIfCancellationRequested();

        // Resolve tenant ID once for all keys in this operation (FIND-HIGH-003 fix)
        var tenantId = _tenantIdResolver();
        var keyList = keys.ToList();
        var keyMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in keyList)
        {
            if (!string.IsNullOrEmpty(key))
            {
                keyMap[BuildPartitionedKey(tenantId, key)] = key;
            }
        }

        var innerResult = await _innerProvider.GetManyAsync<T>(keyMap.Keys, cancellationToken).ConfigureAwait(false);
        if (innerResult.IsFailure)
        {
            return innerResult;
        }

        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var kvp in innerResult.Value)
        {
            if (keyMap.TryGetValue(kvp.Key, out var originalKey))
            {
                result[originalKey] = kvp.Value;
            }
        }

        return Result<IReadOnlyDictionary<string, T>>.Success(result);
    }

    /// <inheritdoc/>
    public Task<Result<bool>> SetManyAsync<T>(
        IEnumerable<KeyValuePair<string, T>> items,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var tenantId = _tenantIdResolver();
        var partitionedOptions = PartitionOptionsWithTenant(tenantId, options);
        var partitionedItems = items.Select(kvp => new KeyValuePair<string, T>(BuildPartitionedKey(tenantId, kvp.Key), kvp.Value));
        return _innerProvider.SetManyAsync(partitionedItems, partitionedOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<bool>> ExpireAsync(string key, TimeSpan timeToLive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var tenantId = _tenantIdResolver();
        return _innerProvider.ExpireAsync(BuildPartitionedKey(tenantId, key), timeToLive, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<bool>> RefreshAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var tenantId = _tenantIdResolver();
        return _innerProvider.RefreshAsync(BuildPartitionedKey(tenantId, key), cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var tenantId = _tenantIdResolver();
        return _innerProvider.ExistsAsync(BuildPartitionedKey(tenantId, key), cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<bool>> RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        var tenantId = _tenantIdResolver();
        if (_innerProvider is ITaggedCacheProvider tagged)
        {
            return tagged.RemoveByTagAsync(BuildPartitionedTag(tenantId, tag), cancellationToken);
        }

        return Task.FromResult(Result<bool>.Failure(new Error(
            "Cache.TagsNotSupported",
            $"The inner cache provider '{_innerProvider.GetType().Name}' does not implement ITaggedCacheProvider.")));
    }

    /// <inheritdoc/>
    public Task<Result<bool>> RemoveByTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var tenantId = _tenantIdResolver();
        var partitionedTags = tags.Select(t => BuildPartitionedTag(tenantId, t));
        if (_innerProvider is ITaggedCacheProvider tagged)
        {
            return tagged.RemoveByTagsAsync(partitionedTags, cancellationToken);
        }

        return Task.FromResult(Result<bool>.Failure(new Error(
            "Cache.TagsNotSupported",
            $"The inner cache provider '{_innerProvider.GetType().Name}' does not implement ITaggedCacheProvider.")));
    }

    private string PartitionKey(string key)
    {
        var tenantId = _tenantIdResolver();
        return BuildPartitionedKey(tenantId, key);
    }

    private string PartitionTag(string tag)
    {
        var tenantId = _tenantIdResolver();
        return BuildPartitionedTag(tenantId, tag);
    }

    private CacheEntryOptions? PartitionOptions(CacheEntryOptions? options)
    {
        if (options?.Tags is null)
        {
            return options;
        }

        var tenantId = _tenantIdResolver();
        return PartitionOptionsWithTenant(tenantId, options);
    }

    private static CacheEntryOptions? PartitionOptionsWithTenant(string? tenantId, CacheEntryOptions? options)
    {
        if (options?.Tags is null)
        {
            return options;
        }

        var partitionedTags = options.Tags.Select(t => BuildPartitionedTag(tenantId, t)).ToList();

        if (options is HybridCacheEntryOptions hybrid)
        {
            return new HybridCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = hybrid.AbsoluteExpirationRelativeToNow,
                SlidingExpiration = hybrid.SlidingExpiration,
                FailSafeMaxStale = hybrid.FailSafeMaxStale,
                LocalCacheDuration = hybrid.LocalCacheDuration,
                DistributedCacheDuration = hybrid.DistributedCacheDuration,
                Tags = partitionedTags
            };
        }

        return new CacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = options.AbsoluteExpirationRelativeToNow,
            SlidingExpiration = options.SlidingExpiration,
            FailSafeMaxStale = options.FailSafeMaxStale,
            Tags = partitionedTags
        };
    }

    private static string BuildPartitionedKey(string? tenantId, string key)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return $"default:{key}";
        }

        var sanitizedTenantId = tenantId.Replace(":", "%3A");
        return $"tenant:{sanitizedTenantId}:{key}";
    }

    private static string BuildPartitionedTag(string? tenantId, string tag)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return $"default:tag:{tag}";
        }

        var sanitizedTenantId = tenantId.Replace(":", "%3A");
        return $"tenant:{sanitizedTenantId}:tag:{tag}";
    }
}
