// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Caching;

/// <summary>
/// Configuration options for the in-memory cache provider.
/// </summary>
public sealed class MemoryCacheOptions
{
    private int? _maxCapacity;
    private TimeSpan? _expirationScanFrequency;
    private double _compactPercentage = 0.25;

    /// <summary>
    /// Gets or sets the maximum number of entries allowed in the cache.
    /// When the limit is exceeded, compaction will evict the least recently accessed entries
    /// according to the <see cref="CompactPercentage"/> setting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <see langword="null"/> (the default), capacity is <b>unbounded</b>.
    /// </para>
    /// <para>
    /// <b>⚠ Production warning:</b> In production environments, always configure a finite <c>MaxCapacity</c>
    /// to prevent out-of-memory conditions under high key cardinality or adversarial workloads.
    /// A reasonable starting value is between 10,000 and 100,000 entries depending on entry size and available memory.
    /// </para>
    /// <para>Example: <c>options.MaxCapacity = 50_000;</c></para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when value is less than or equal to 0.</exception>
    public int? MaxCapacity
    {
        get => _maxCapacity;
        set
        {
            if (value.HasValue && value.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "MaxCapacity must be strictly positive.");
            }
            _maxCapacity = value;
        }
    }

    /// <summary>
    /// Gets or sets the frequency at which a background timer actively scans and evicts expired cache entries
    /// and prunes empty tag indexes. When <see langword="null"/>, active background sweeps are disabled and
    /// eviction occurs lazily or via explicit calls to <see cref="MemoryCacheProvider.RemoveExpiredEntries"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when value is less than or equal to <see cref="TimeSpan.Zero"/>.</exception>
    public TimeSpan? ExpirationScanFrequency
    {
        get => _expirationScanFrequency;
        set
        {
            if (value.HasValue && value.Value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "ExpirationScanFrequency must be strictly positive.");
            }
            _expirationScanFrequency = value;
        }
    }

    /// <summary>
    /// Gets or sets the percentage of entries to evict when <see cref="MaxCapacity"/> is reached.
    /// Defaults to 0.25 (25%). Must be between 0.05 and 0.50.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when value is less than 0.05 or greater than 0.50.</exception>
    public double CompactPercentage
    {
        get => _compactPercentage;
        set
        {
            if (value is < 0.05 or > 0.50)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "CompactPercentage must be between 0.05 and 0.50.");
            }
            _compactPercentage = value;
        }
    }
}
