// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Result;

namespace EricksonLopez.Caching;

/// <summary>
/// Defines standard contract for caching operations with functional Result returns and stampede protection.
/// </summary>
public interface ICacheProvider
{
    /// <summary>
    /// Retrieves a cached item by its key.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The unique cache key identifying the entry. Cannot be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Token to observe for cancellation requests.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> containing the cached value on a hit, or
    /// <see langword="null"/> on a cache miss. A cache miss returns <see cref="Result{T}.Success"/>
    /// with a <see langword="null"/> value — it is never a failure.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    Task<Result<T?>> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves multiple cached items by their keys.
    /// </summary>
    /// <typeparam name="T">The type of the cached values.</typeparam>
    /// <param name="keys">The unique cache keys identifying the entries. Cannot be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Token to observe for cancellation requests.</param>
    /// <returns>A <see cref="Result{T}"/> containing a read-only dictionary of key-value pairs for all hits.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="keys"/> is <see langword="null"/>.</exception>
    async Task<Result<IReadOnlyDictionary<string, T>>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var dict = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            var res = await GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
            if (res.IsSuccess && res.Value is not null)
            {
                dict[key] = res.Value;
            }
        }
        return Result<IReadOnlyDictionary<string, T>>.Success(dict);
    }

    /// <summary>
    /// Gets an existing item or acquires an atomic lock to execute the factory and populate the cache (stampede-proof).
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The unique cache key. Cannot be <see langword="null"/>.</param>
    /// <param name="factory">
    /// The async delegate invoked exactly once under concurrent cache misses (single-flight guarantee).
    /// Cannot be <see langword="null"/>.
    /// </param>
    /// <param name="options">
    /// Optional expiration, sliding TTL, fail-safe stale window, and tag options.
    /// When <see langword="null"/>, provider defaults apply.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cancellation requests. Propagated to the factory delegate.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> containing the cached or freshly computed value.
    /// Returns a failure result only if the factory throws a non-cancellation exception and no fail-safe stale
    /// value is available.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="key"/> or <paramref name="factory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Propagated when <paramref name="cancellationToken"/> is cancelled. Never swallowed.
    /// </exception>
    Task<Result<T>> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a value in the cache with the given expiration options.
    /// </summary>
    /// <typeparam name="T">The type of the value to cache.</typeparam>
    /// <param name="key">The unique cache key. Cannot be <see langword="null"/>.</param>
    /// <param name="value">The value to store. May be <see langword="null"/> for reference types.</param>
    /// <param name="options">
    /// Optional expiration and tag options. When <see langword="null"/>, provider defaults apply.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cancellation requests.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> of <see cref="bool"/> indicating whether the set operation succeeded.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    Task<Result<bool>> SetAsync<T>(
        string key,
        T value,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets multiple key-value pairs in the cache with the given expiration and tag options.
    /// </summary>
    /// <typeparam name="T">The type of the values to cache.</typeparam>
    /// <param name="items">The key-value pairs to store. Cannot be <see langword="null"/>.</param>
    /// <param name="options">Optional expiration and tag options.</param>
    /// <param name="cancellationToken">Token to observe for cancellation requests.</param>
    /// <returns>A <see cref="Result{T}"/> indicating whether all items were successfully set.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="items"/> is <see langword="null"/>.</exception>
    async Task<Result<bool>> SetManyAsync<T>(
        IEnumerable<KeyValuePair<string, T>> items,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var allSuccess = true;
        foreach (var kvp in items)
        {
            var res = await SetAsync(kvp.Key, kvp.Value, options, cancellationToken).ConfigureAwait(false);
            if (res.IsFailure || !res.Value)
            {
                allSuccess = false;
            }
        }
        return Result<bool>.Success(allSuccess);
    }

    /// <summary>
    /// Removes an item from the cache by its key.
    /// </summary>
    /// <param name="key">The unique cache key to remove. Cannot be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Token to observe for cancellation requests.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> of <see cref="bool"/>: <see langword="true"/> if the entry existed and was removed;
    /// <see langword="false"/> if no entry was found for the key.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    Task<Result<bool>> RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes multiple items from the cache by their keys.
    /// </summary>
    /// <param name="keys">The collection of unique cache keys to remove. Cannot be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Token to observe for cancellation requests.</param>
    /// <returns>A <see cref="Result{T}"/> containing the count of entries successfully removed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="keys"/> is <see langword="null"/>.</exception>
    async Task<Result<long>> RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        long removedCount = 0;
        foreach (var key in keys)
        {
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            var res = await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            if (res.IsSuccess && res.Value)
            {
                removedCount++;
            }
        }
        return Result<long>.Success(removedCount);
    }

    /// <summary>
    /// Removes all cached entries matching the specified key prefix.
    /// </summary>
    /// <param name="prefix">
    /// The prefix to match against cache keys. Cannot be <see langword="null"/>, empty, or whitespace.
    /// An empty or whitespace prefix is rejected to prevent accidental whole-cache wipeouts.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cancellation requests.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> of <see cref="bool"/> indicating whether the operation completed successfully.
    /// Returns <see langword="true"/> even when no matching keys were found.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="prefix"/> is <see langword="null"/>, empty, or whitespace.
    /// </exception>
    Task<Result<bool>> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether an entry exists for the given key and is not expired.
    /// </summary>
    /// <param name="key">The unique cache key. Cannot be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Token to observe for cancellation requests.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> containing <see langword="true"/> if the entry exists and is active;
    /// otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    Task<Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<bool>.Success(false));

    /// <summary>
    /// Sets a new absolute expiration timeout for the specified key.
    /// </summary>
    /// <param name="key">The unique cache key. Cannot be <see langword="null"/>.</param>
    /// <param name="timeToLive">The new time-to-live duration from now. Must be positive.</param>
    /// <param name="cancellationToken">Token to observe for cancellation requests.</param>
    /// <returns>A <see cref="Result{T}"/> indicating whether the timeout was updated.</returns>
    Task<Result<bool>> ExpireAsync(string key, TimeSpan timeToLive, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<bool>.Success(false));

    /// <summary>
    /// Refreshes the expiration window of a cached item without reading or altering its value.
    /// </summary>
    /// <param name="key">The unique cache key. Cannot be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Token to observe for cancellation requests.</param>
    /// <returns>A <see cref="Result{T}"/> indicating whether the entry was found and refreshed.</returns>
    Task<Result<bool>> RefreshAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<bool>.Success(false));
}
