// Copyright © Erickson Lopez. MIT License.
using System;
using Microsoft.Extensions.Logging;

namespace EricksonLopez.Caching.Redis;

/// <summary>
/// High-performance, zero-allocation log message definitions for the Redis cache provider.
/// </summary>
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Redis GET failed for key '{Key}'")]
    internal static partial void GetFailed(ILogger logger, string key, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Redis SET failed for key '{Key}'")]
    internal static partial void SetFailed(ILogger logger, string key, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Redis DELETE failed for key '{Key}'")]
    internal static partial void DeleteFailed(ILogger logger, string key, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Redis prefix deletion failed for prefix '{Prefix}'")]
    internal static partial void PrefixDeletionFailed(ILogger logger, string prefix, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Removed {Count} cache entries with prefix '{Prefix}'")]
    internal static partial void PrefixDeletionCompleted(ILogger logger, long count, string prefix);

    [LoggerMessage(Level = LogLevel.Error, Message = "Factory execution failed for cache key '{Key}'")]
    internal static partial void FactoryFailed(ILogger logger, string key, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Transient Redis failure on {Operation} (attempt {Attempt}/{MaxRetries}). Retrying...")]
    internal static partial void RetryAttempt(ILogger logger, string operation, int attempt, int maxRetries, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Acquired distributed lock for key '{Key}'")]
    internal static partial void LockAcquired(ILogger logger, string key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Waiting for distributed lock release for key '{Key}'")]
    internal static partial void LockWait(ILogger logger, string key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Released distributed lock for key '{Key}'")]
    internal static partial void LockReleased(ILogger logger, string key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Redis tag deletion failed for tag '{Tag}'")]
    internal static partial void TagDeletionFailed(ILogger logger, string tag, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Removed {Count} cache entries for tag '{Tag}'")]
    internal static partial void TagDeletionCompleted(ILogger logger, long count, string tag);
}
