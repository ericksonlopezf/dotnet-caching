// Copyright © Erickson Lopez. MIT License.
using EricksonLopez.Result;

namespace EricksonLopez.Caching.Redis;

/// <summary>
/// Centralized error definitions for Redis cache operations.
/// </summary>
internal static class CacheErrors
{
    /// <summary>
    /// Redis connection or command execution failed.
    /// </summary>
    internal static Error ConnectionFailed(string detail) =>
        Error.Failure("Cache.Redis.ConnectionFailed", $"Redis operation failed: {detail}");

    /// <summary>
    /// The cache factory delegate threw an exception during GetOrCreate.
    /// </summary>
    internal static Error FactoryFailed(string key, string detail) =>
        Error.Failure("Cache.Redis.FactoryFailed", $"Factory for cache key '{key}' failed: {detail}");

    /// <summary>
    /// Deserialization of the cached Redis payload failed.
    /// </summary>
    internal static Error DeserializationFailed(string key, string detail) =>
        Error.Failure("Cache.Redis.DeserializationFailed", $"Deserialization failed for cache key '{key}': {detail}");

    /// <summary>
    /// Serialization of the value for caching in Redis failed.
    /// </summary>
    internal static Error SerializationFailed(string key, string detail) =>
        Error.Failure("Cache.Redis.SerializationFailed", $"Serialization failed for cache key '{key}': {detail}");
}
