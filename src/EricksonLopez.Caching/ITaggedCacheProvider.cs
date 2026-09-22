// Copyright © Erickson Lopez. MIT License.
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Result;

namespace EricksonLopez.Caching;

/// <summary>
/// Defines a cache provider that supports categorizing entries with tags for multi-key bulk invalidation.
/// </summary>
public interface ITaggedCacheProvider : ICacheProvider
{
    /// <summary>
    /// Sets a value in the cache associated with one or more tags.
    /// </summary>
    /// <typeparam name="T">The type of the item being cached.</typeparam>
    /// <param name="key">The unique cache key.</param>
    /// <param name="value">The value to cache.</param>
    /// <param name="tags">The collection of tags to associate with this cache entry.</param>
    /// <param name="options">Optional expiration and eviction options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Result{T}"/> indicating whether the operation succeeded.</returns>
    Task<Result<bool>> SetWithTagsAsync<T>(
        string key,
        T value,
        IEnumerable<string> tags,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evicts all cache entries associated with the specified tag.
    /// </summary>
    /// <param name="tag">The tag whose associated entries will be removed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Result{T}"/> indicating whether the eviction succeeded.</returns>
    Task<Result<bool>> RemoveByTagAsync(
        string tag,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evicts all cache entries associated with any of the specified tags.
    /// </summary>
    /// <param name="tags">The collection of tags whose associated entries will be removed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Result{T}"/> indicating whether the eviction succeeded.</returns>
    Task<Result<bool>> RemoveByTagsAsync(
        IEnumerable<string> tags,
        CancellationToken cancellationToken = default);
}
