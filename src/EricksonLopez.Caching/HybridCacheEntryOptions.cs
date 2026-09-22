// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Caching;

/// <summary>
/// Specifies tiered expiration and eviction rules for hybrid (L1 in-memory + L2 distributed) cache entries.
/// </summary>
public sealed class HybridCacheEntryOptions : CacheEntryOptions
{
    private TimeSpan? _localCacheDuration;
    private TimeSpan? _distributedCacheDuration;

    /// <summary>
    /// Gets or sets an optional duration relative to now specifically for the L1 local in-memory cache.
    /// When <see langword="null"/>, falls back to <see cref="CacheEntryOptions.AbsoluteExpirationRelativeToNow"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when value is less than or equal to <see cref="TimeSpan.Zero"/> or exceeds <see cref="CacheEntryOptions.MaxAllowedDuration"/>.
    /// </exception>
    public TimeSpan? LocalCacheDuration
    {
        get => _localCacheDuration;
        set
        {
            if (value.HasValue)
            {
                ValidateDuration(value.Value, nameof(LocalCacheDuration));
            }
            _localCacheDuration = value;
        }
    }

    /// <summary>
    /// Gets or sets an optional duration relative to now specifically for the L2 distributed cache.
    /// When <see langword="null"/>, falls back to <see cref="CacheEntryOptions.AbsoluteExpirationRelativeToNow"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when value is less than or equal to <see cref="TimeSpan.Zero"/> or exceeds <see cref="CacheEntryOptions.MaxAllowedDuration"/>.
    /// </exception>
    public TimeSpan? DistributedCacheDuration
    {
        get => _distributedCacheDuration;
        set
        {
            if (value.HasValue)
            {
                ValidateDuration(value.Value, nameof(DistributedCacheDuration));
            }
            _distributedCacheDuration = value;
        }
    }

    /// <summary>
    /// Creates a new instance of <see cref="HybridCacheEntryOptions"/> with distinct durations for L1 and L2 caches.
    /// </summary>
    /// <param name="localDuration">Duration for the L1 local in-memory cache.</param>
    /// <param name="distributedDuration">Duration for the L2 distributed cache.</param>
    /// <returns>Configured <see cref="HybridCacheEntryOptions"/> instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when either duration is less than or equal to <see cref="TimeSpan.Zero"/> or exceeds <see cref="CacheEntryOptions.MaxAllowedDuration"/>.
    /// </exception>
    public static HybridCacheEntryOptions FromDurations(TimeSpan localDuration, TimeSpan distributedDuration) =>
        new()
        {
            LocalCacheDuration = localDuration,
            DistributedCacheDuration = distributedDuration,
            AbsoluteExpirationRelativeToNow = distributedDuration
        };
}
