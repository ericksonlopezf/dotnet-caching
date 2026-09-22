// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Caching.Redis;

/// <summary>
/// Configuration options for the Redis L2 cache provider.
/// </summary>
public sealed class RedisCacheOptions
{
    /// <summary>
    /// The StackExchange.Redis configuration string (e.g. "localhost:6379,abortConnect=false").
    /// </summary>
    public string Configuration { get; set; } = "localhost:6379,abortConnect=false";

    /// <summary>
    /// Optional instance name prefix prepended to all cache keys for namespace isolation.
    /// </summary>
    public string? InstanceName { get; set; }

    /// <summary>
    /// Default absolute expiration relative to now for cache entries when not explicitly specified.
    /// </summary>
    public TimeSpan DefaultAbsoluteExpiration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The Redis database index to use (0–15). Defaults to 0.
    /// </summary>
    public int Database { get; set; }

    /// <summary>
    /// Maximum number of retry attempts for Redis operations before returning a failure result.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Delay between retry attempts for transient network failures.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Time-to-live for the distributed stampede protection lock in Redis.
    /// Prevents deadlocks if the lock holder terminates unexpectedly.
    /// </summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Polling interval while waiting for another instance holding the distributed lock.
    /// </summary>
    public TimeSpan LockRetryInterval { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Maximum total time to wait for a distributed lock before executing the factory as fallback.
    /// </summary>
    public TimeSpan MaxLockWaitTime { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Optional custom serializer for cached items. Defaults to <see cref="SystemTextJsonCacheSerializer.Default"/>.
    /// </summary>
    public ICacheSerializer? Serializer { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether cache read operations (such as <c>GetAsync</c> and <c>ExistsAsync</c>)
    /// should fail open (return cache miss <c>Result.Success(default)</c>) when Redis is unavailable,
    /// rather than returning an error result. Defaults to <see langword="false"/>.
    /// </summary>
    public bool FailOpen { get; set; }
}
