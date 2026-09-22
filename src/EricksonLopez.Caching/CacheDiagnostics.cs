// Copyright © Erickson Lopez. MIT License.
using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EricksonLopez.Caching;

/// <summary>
/// Provides OpenTelemetry-compatible metrics and distributed tracing instruments for the caching library.
/// Uses <see cref="System.Diagnostics.Metrics.Meter"/> and <see cref="System.Diagnostics.ActivitySource"/>
/// without external third-party dependencies.
/// </summary>
public static class CacheDiagnostics
{
    /// <summary>
    /// The name of the OpenTelemetry meter used by this library.
    /// </summary>
    public const string MeterName = "EricksonLopez.Caching";

    /// <summary>
    /// The name of the OpenTelemetry ActivitySource used for distributed tracing.
    /// </summary>
    public const string ActivitySourceName = "EricksonLopez.Caching";

    /// <summary>
    /// The <see cref="System.Diagnostics.ActivitySource"/> instance for caching trace spans.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> HitsCounter = Meter.CreateCounter<long>(
        "cache.hits",
        unit: "{hits}",
        description: "Number of successful cache hits.");

    private static readonly Counter<long> MissesCounter = Meter.CreateCounter<long>(
        "cache.misses",
        unit: "{misses}",
        description: "Number of cache misses.");

    private static readonly Counter<long> StampedesPreventedCounter = Meter.CreateCounter<long>(
        "cache.stampedes_prevented",
        unit: "{stampedes}",
        description: "Number of cache stampedes prevented by single-flight locking.");

    private static readonly Counter<long> EvictionsCounter = Meter.CreateCounter<long>(
        "cache.evictions",
        unit: "{evictions}",
        description: "Number of cache entries evicted.");

    private static readonly Counter<long> ErrorsCounter = Meter.CreateCounter<long>(
        "cache.errors",
        unit: "{errors}",
        description: "Number of cache provider infrastructure errors.");

    private static string GetSpanName(string operation) => operation switch
    {
        "get" => "Cache get",
        "set" => "Cache set",
        "get_or_create" => "Cache get_or_create",
        "remove" => "Cache remove",
        "remove_by_prefix" => "Cache remove_by_prefix",
        "remove_by_tag" => "Cache remove_by_tag",
        "remove_by_tags" => "Cache remove_by_tags",
        "exists" => "Cache exists",
        "set_with_tags" => "Cache set_with_tags",
        _ => $"Cache {operation}"
    };

    /// <summary>
    /// Starts a new distributed trace span for a cache operation if a listener is active.
    /// </summary>
    /// <param name="operation">The operation name (e.g., "get", "set", "get_or_create", "remove").</param>
    /// <param name="provider">The cache provider name (e.g., "memory", "redis", "hybrid").</param>
    /// <param name="key">The cache key being accessed.</param>
    /// <returns>The created <see cref="Activity"/>, or <see langword="null"/> if no listeners exist.</returns>
    public static Activity? StartActivity(string operation, string provider, string key)
    {
        if (!ActivitySource.HasListeners())
        {
            return null;
        }

        var activity = ActivitySource.StartActivity(GetSpanName(operation), ActivityKind.Internal);
        if (activity is not null)
        {
            activity.SetTag("cache.operation", operation);
            activity.SetTag("cache.provider", provider);
            activity.SetTag("cache.key", key);
        }

        return activity;
    }

    /// <summary>
    /// Records a cache hit for the specified provider and operation using allocation-free TagList.
    /// </summary>
    public static void RecordHit(string provider, string operation = "get")
    {
        TagList tags = new()
        {
            { "cache.provider", provider },
            { "cache.operation", operation }
        };
        HitsCounter.Add(1, tags);
    }

    /// <summary>
    /// Records a cache miss for the specified provider and operation using allocation-free TagList.
    /// </summary>
    public static void RecordMiss(string provider, string operation = "get")
    {
        TagList tags = new()
        {
            { "cache.provider", provider },
            { "cache.operation", operation }
        };
        MissesCounter.Add(1, tags);
    }

    /// <summary>
    /// Records that a concurrent cache stampede was prevented by single-flight synchronization.
    /// </summary>
    public static void RecordStampedePrevented(string provider)
    {
        TagList tags = new()
        {
            { "cache.provider", provider }
        };
        StampedesPreventedCounter.Add(1, tags);
    }

    /// <summary>
    /// Records the eviction of one or more cache entries.
    /// </summary>
    public static void RecordEviction(string provider, string reason, long count = 1)
    {
        if (count <= 0) return;
        TagList tags = new()
        {
            { "cache.provider", provider },
            { "eviction.reason", reason }
        };
        EvictionsCounter.Add(count, tags);
    }

    /// <summary>
    /// Records an infrastructure error during cache access.
    /// </summary>
    public static void RecordError(string provider, string errorCode)
    {
        TagList tags = new()
        {
            { "cache.provider", provider },
            { "error.code", errorCode }
        };
        ErrorsCounter.Add(1, tags);
    }
}
