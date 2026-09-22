// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;

namespace EricksonLopez.Caching;

/// <summary>
/// Specifies expiration, eviction, and resilience rules for cached entries.
/// </summary>
public class CacheEntryOptions
{
    /// <summary>
    /// The maximum allowable duration for any cache expiration option (10 years).
    /// </summary>
    public static readonly TimeSpan MaxAllowedDuration = TimeSpan.FromDays(3650);

    private TimeSpan? _absoluteExpirationRelativeToNow;
    private TimeSpan? _slidingExpiration;
    private TimeSpan? _failSafeMaxStale;

    /// <summary>
    /// Gets or sets an absolute expiration time relative to now.
    /// Must be strictly positive and less than or equal to <see cref="MaxAllowedDuration"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when value is less than or equal to <see cref="TimeSpan.Zero"/> or exceeds <see cref="MaxAllowedDuration"/>.
    /// </exception>
    public TimeSpan? AbsoluteExpirationRelativeToNow
    {
        get => _absoluteExpirationRelativeToNow;
        set
        {
            if (value.HasValue)
            {
                ValidateDuration(value.Value, nameof(AbsoluteExpirationRelativeToNow));
            }
            _absoluteExpirationRelativeToNow = value;
        }
    }

    /// <summary>
    /// Gets or sets how long a cache entry can be inactive before it will also be removed.
    /// Must be strictly positive and less than or equal to <see cref="MaxAllowedDuration"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when value is less than or equal to <see cref="TimeSpan.Zero"/> or exceeds <see cref="MaxAllowedDuration"/>.
    /// </exception>
    public TimeSpan? SlidingExpiration
    {
        get => _slidingExpiration;
        set
        {
            if (value.HasValue)
            {
                ValidateDuration(value.Value, nameof(SlidingExpiration));
            }
            _slidingExpiration = value;
        }
    }

    /// <summary>
    /// Gets or sets an optional maximum duration during which an expired entry can be returned as fallback
    /// if the background/underlying factory execution fails.
    /// Must be strictly positive and less than or equal to <see cref="MaxAllowedDuration"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when value is less than or equal to <see cref="TimeSpan.Zero"/> or exceeds <see cref="MaxAllowedDuration"/>.
    /// </exception>
    public TimeSpan? FailSafeMaxStale
    {
        get => _failSafeMaxStale;
        set
        {
            if (value.HasValue)
            {
                ValidateDuration(value.Value, nameof(FailSafeMaxStale));
            }
            _failSafeMaxStale = value;
        }
    }

    /// <summary>
    /// Gets or sets optional tags associated with this cache entry for bulk invalidation.
    /// </summary>
    public IReadOnlyCollection<string>? Tags { get; set; }

    /// <summary>
    /// Creates options with the specified absolute expiration duration.
    /// </summary>
    /// <param name="duration">The time-to-live relative to now after which the cached entry expires.</param>
    /// <returns>A new <see cref="CacheEntryOptions"/> with <see cref="AbsoluteExpirationRelativeToNow"/> set to <paramref name="duration"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="duration"/> is less than or equal to <see cref="TimeSpan.Zero"/> or exceeds <see cref="MaxAllowedDuration"/>.
    /// </exception>
    public static CacheEntryOptions FromAbsolute(TimeSpan duration) =>
        new() { AbsoluteExpirationRelativeToNow = duration };

    /// <summary>
    /// Creates options with the specified sliding expiration duration.
    /// </summary>
    /// <param name="duration">The inactivity window after which the cached entry expires if not accessed.</param>
    /// <returns>A new <see cref="CacheEntryOptions"/> with <see cref="SlidingExpiration"/> set to <paramref name="duration"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="duration"/> is less than or equal to <see cref="TimeSpan.Zero"/> or exceeds <see cref="MaxAllowedDuration"/>.
    /// </exception>
    public static CacheEntryOptions FromSliding(TimeSpan duration) =>
        new() { SlidingExpiration = duration };

    /// <summary>
    /// Creates options with absolute expiration and fail-safe stale tolerance.
    /// </summary>
    /// <param name="duration">Normal time-to-live before the entry becomes stale.</param>
    /// <param name="maxStale">Maximum duration beyond TTL during which the stale entry may be served if factory fails.</param>
    /// <returns>
    /// A new <see cref="CacheEntryOptions"/> with both <see cref="AbsoluteExpirationRelativeToNow"/>
    /// and <see cref="FailSafeMaxStale"/> configured.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="duration"/> or <paramref name="maxStale"/> is less than or equal to <see cref="TimeSpan.Zero"/> or exceeds <see cref="MaxAllowedDuration"/>.
    /// </exception>
    public static CacheEntryOptions FromAbsoluteWithFailSafe(TimeSpan duration, TimeSpan maxStale) =>
        new() { AbsoluteExpirationRelativeToNow = duration, FailSafeMaxStale = maxStale };

    /// <summary>
    /// Validates that a duration is strictly positive and does not exceed <see cref="MaxAllowedDuration"/>.
    /// </summary>
    /// <param name="duration">The duration to validate.</param>
    /// <param name="paramName">The name of the parameter or property.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when validation fails.</exception>
    protected internal static void ValidateDuration(TimeSpan duration, string paramName)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(paramName, duration, "Duration must be strictly positive.");
        }

        if (duration > MaxAllowedDuration)
        {
            throw new ArgumentOutOfRangeException(paramName, duration, $"Duration cannot exceed {MaxAllowedDuration.TotalDays} days.");
        }
    }
}
