// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Result;

namespace EricksonLopez.Caching;

/// <summary>
/// Defines contracts for high-throughput cache invalidation by key prefix or hierarchical tag.
/// </summary>
public interface ICacheInvalidator
{
    /// <summary>
    /// Invalidates a single cached entry by its key.
    /// </summary>
    /// <param name="key">The cache key to evict.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A Result indicating whether the entry existed and was evicted.</returns>
    Task<Result<bool>> InvalidateAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<bool>.Success(true));

    /// <summary>
    /// Invalidates multiple cached entries by their keys.
    /// </summary>
    /// <param name="keys">The collection of cache keys to evict.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A Result containing the count of entries evicted.</returns>
    Task<Result<long>> InvalidateManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<long>.Success(0));

    /// <summary>
    /// Invalidates all cached entries matching the specified key prefix.
    /// </summary>
    /// <param name="prefix">The key prefix to evict.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A Result indicating whether the invalidation succeeded.</returns>
    Task<Result<bool>> InvalidateByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all cached entries matching any of the specified key prefixes.
    /// </summary>
    /// <param name="prefixes">The collection of key prefixes to evict.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A Result indicating whether all invalidations succeeded.</returns>
    Task<Result<bool>> InvalidateByPrefixesAsync(IEnumerable<string> prefixes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all cached entries associated with the specified tag.
    /// </summary>
    /// <param name="tag">The tag whose associated entries will be evicted.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A Result indicating whether the tag invalidation succeeded.</returns>
    Task<Result<bool>> InvalidateByTagAsync(string tag, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all cached entries associated with any of the specified tags.
    /// </summary>
    /// <param name="tags">The collection of tags whose associated entries will be evicted.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A Result indicating whether all tag invalidations succeeded.</returns>
    Task<Result<bool>> InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default);
}
