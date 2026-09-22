// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Result;
using Microsoft.Extensions.Logging;

namespace EricksonLopez.Caching;

/// <summary>
/// Canonical implementation of <see cref="ICacheInvalidator"/> backed by <see cref="ICacheProvider"/>.
/// </summary>
public sealed class CacheInvalidator : ICacheInvalidator
{
    private readonly ICacheProvider _cacheProvider;
    private readonly ILogger<CacheInvalidator>? _logger;

    private static readonly Action<ILogger, string, Exception?> LogCacheInvalidated =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(1, nameof(InvalidateByPrefixAsync)),
            "Cache entries invalidated for prefix: {Prefix}");

    private static readonly Action<ILogger, string, Exception?> LogCacheTagInvalidated =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(2, nameof(InvalidateByTagAsync)),
            "Cache entries invalidated for tag: {Tag}");

    private static readonly Action<ILogger, string, Exception?> LogCacheKeyInvalidated =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(3, nameof(InvalidateAsync)),
            "Cache entry invalidated for key: {Key}");

    private static readonly Action<ILogger, long, Exception?> LogCacheKeysInvalidated =
        LoggerMessage.Define<long>(
            LogLevel.Debug,
            new EventId(4, nameof(InvalidateManyAsync)),
            "Cache entries invalidated. Count: {Count}");

    /// <summary>
    /// Initializes a new instance of the <see cref="CacheInvalidator"/> class.
    /// </summary>
    /// <param name="cacheProvider">The underlying cache provider.</param>
    /// <param name="logger">Optional logger instance.</param>
    public CacheInvalidator(
        ICacheProvider cacheProvider,
        ILogger<CacheInvalidator>? logger = null)
    {
        _cacheProvider = cacheProvider ?? throw new ArgumentNullException(nameof(cacheProvider));
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> InvalidateAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Result<bool>.Failure(new Error("Cache.InvalidKey", "The cache invalidation key cannot be null or empty."));
        }

        var result = await _cacheProvider.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess && result.Value && _logger is not null)
        {
            LogCacheKeyInvalidated(_logger, key, null);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<Result<long>> InvalidateManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var result = await _cacheProvider.RemoveManyAsync(keys, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess && _logger is not null)
        {
            LogCacheKeysInvalidated(_logger, result.Value, null);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> InvalidateByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return Result<bool>.Failure(new Error("Cache.InvalidPrefix", "The cache invalidation prefix cannot be null or empty."));
        }

        var result = await _cacheProvider.RemoveByPrefixAsync(prefix, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess && _logger is not null)
        {
            LogCacheInvalidated(_logger, prefix, null);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> InvalidateByPrefixesAsync(IEnumerable<string> prefixes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefixes);

        var allSucceeded = true;
        foreach (var prefix in prefixes)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                continue;
            }

            var result = await InvalidateByPrefixAsync(prefix, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                allSucceeded = false;
            }
        }

        return Result<bool>.Success(allSucceeded);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> InvalidateByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return Result<bool>.Failure(new Error("Cache.InvalidTag", "The cache invalidation tag cannot be null or empty."));
        }

        if (_cacheProvider is not ITaggedCacheProvider taggedProvider)
        {
            return Result<bool>.Failure(new Error(
                "Cache.TagsNotSupported",
                $"The configured cache provider '{_cacheProvider.GetType().Name}' does not implement ITaggedCacheProvider."));
        }

        var result = await taggedProvider.RemoveByTagAsync(tag, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess && _logger is not null)
        {
            LogCacheTagInvalidated(_logger, tag, null);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var allSucceeded = true;
        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            var result = await InvalidateByTagAsync(tag, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                allSucceeded = false;
            }
        }

        return Result<bool>.Success(allSucceeded);
    }
}
