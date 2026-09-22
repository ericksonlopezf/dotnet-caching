// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Result;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EricksonLopez.Caching.Redis;

/// <summary>
/// Redis-backed L2 cache provider with distributed stampede protection, tag-based eviction,
/// configurable retry loop, and prefix-based invalidation.
/// Thread-safe for concurrent access across multiple application instances.
/// </summary>
public sealed class RedisCacheProvider : ITaggedCacheProvider, IDisposable, IAsyncDisposable
{
    private const string ProviderName = "redis";

    private const string UnlockLuaScript = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        else
            return 0
        end
        """;

    private const string PrefixDeletionLuaScript = """
        local cursor = '0'
        local deleted = 0
        local iterations = 0
        repeat
            local result = redis.call('SCAN', cursor, 'MATCH', ARGV[1] .. '*', 'COUNT', 100)
            cursor = result[1]
            local keys = result[2]
            local count = #keys
            if count > 0 then
                local batchSize = 1000
                for i = 1, count, batchSize do
                    local batchEnd = math.min(i + batchSize - 1, count)
                    local batch = {}
                    for j = i, batchEnd do
                        batch[#batch + 1] = keys[j]
                    end
                    local ok, res = pcall(redis.call, 'UNLINK', unpack(batch))
                    if ok and type(res) == 'number' then
                        deleted = deleted + res
                    end
                end
            end
            iterations = iterations + 1
        until cursor == '0' or iterations >= 1000
        return deleted
        """;

    private const string TagDeletionLuaScript = """
        local members = redis.call('SMEMBERS', KEYS[1])
        local deleted = 0
        local count = #members
        if count > 0 then
            local batchSize = 1000
            for i = 1, count, batchSize do
                local batchEnd = math.min(i + batchSize - 1, count)
                local batch = {}
                for j = i, batchEnd do
                    batch[#batch + 1] = members[j]
                end
                local ok, res = pcall(redis.call, 'UNLINK', unpack(batch))
                if ok and type(res) == 'number' then
                    deleted = deleted + res
                end
            end
        end
        redis.call('DEL', KEYS[1])
        return deleted
        """;

    private readonly IConnectionMultiplexer _connection;
    private readonly ILogger<RedisCacheProvider> _logger;
    private readonly RedisCacheOptions _options;
    private readonly ICacheSerializer _serializer;
    private readonly bool _ownsConnection;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance using an externally managed connection multiplexer.
    /// </summary>
    /// <param name="connection">An existing Redis connection multiplexer. Cannot be <see langword="null"/>.</param>
    /// <param name="options">Redis cache configuration options. Cannot be <see langword="null"/>.</param>
    /// <param name="logger">Logger instance. Cannot be <see langword="null"/>.</param>
    /// <param name="serializer">Optional custom serializer. Defaults to <see cref="SystemTextJsonCacheSerializer.Default"/>.</param>
    public RedisCacheProvider(
        IConnectionMultiplexer connection,
        IOptions<RedisCacheOptions> options,
        ILogger<RedisCacheProvider> logger,
        ICacheSerializer? serializer = null)
        : this(connection, options, logger, serializer, null)
    {
    }

    /// <summary>
    /// Initializes a new instance using an externally managed connection multiplexer and custom time provider.
    /// </summary>
    /// <param name="connection">An existing Redis connection multiplexer. Cannot be <see langword="null"/>.</param>
    /// <param name="options">Redis cache configuration options. Cannot be <see langword="null"/>.</param>
    /// <param name="logger">Logger instance. Cannot be <see langword="null"/>.</param>
    /// <param name="serializer">Custom serializer, or <see langword="null"/> for default.</param>
    /// <param name="timeProvider">Optional time provider for time-dependent operations. Defaults to <see cref="TimeProvider.System"/>.</param>
    public RedisCacheProvider(
        IConnectionMultiplexer connection,
        IOptions<RedisCacheOptions> options,
        ILogger<RedisCacheProvider> logger,
        ICacheSerializer? serializer,
        TimeProvider? timeProvider)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? _options.Serializer ?? SystemTextJsonCacheSerializer.Default;
        _ownsConnection = false;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Initializes a new instance that owns and manages its own connection.
    /// </summary>
    internal RedisCacheProvider(
        IConnectionMultiplexer connection,
        RedisCacheOptions options,
        ILogger<RedisCacheProvider> logger,
        bool ownsConnection,
        ICacheSerializer? serializer = null,
        TimeProvider? timeProvider = null)
    {
        _connection = connection;
        _logger = logger;
        _options = options;
        _serializer = serializer ?? _options.Serializer ?? SystemTextJsonCacheSerializer.Default;
        _ownsConnection = ownsConnection;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    [UnconditionalSuppressMessage("AOT", "IL2026",
        Justification = "Serialization is handled by ICacheSerializer which can be configured for Native AOT.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Serialization is handled by ICacheSerializer which can be configured for Native AOT.")]
    private async Task<(bool Found, Result<T?> Result)> TryGetInternalAsync<T>(string key, CancellationToken cancellationToken)
    {
        var fullKey = BuildKey(key);
        var execResult = await ExecuteWithRetryAsync<(bool Found, Result<T?> Result)>(
            nameof(GetAsync),
            async db =>
            {
                var value = await db.StringGetAsync(fullKey).ConfigureAwait(false);

                if (value.IsNull)
                {
                    CacheDiagnostics.RecordMiss(ProviderName, nameof(GetAsync));
                    return Result<(bool Found, Result<T?> Result)>.Success((false, Result<T?>.Success(default)));
                }

                try
                {
                    var deserialized = _serializer.Deserialize<T>(value.ToString());
                    CacheDiagnostics.RecordHit(ProviderName, nameof(GetAsync));
                    return Result<(bool Found, Result<T?> Result)>.Success((true, Result<T?>.Success(deserialized)));
                }
                catch (Exception ex)
                {
                    Log.GetFailed(_logger, key, ex);
                    CacheDiagnostics.RecordError(ProviderName, "DESERIALIZATION_FAILED");
                    return Result<(bool Found, Result<T?> Result)>.Success((true, Result<T?>.Failure(CacheErrors.DeserializationFailed(key, ex.Message))));
                }
            },
            ex => Log.GetFailed(_logger, key, ex),
            cancellationToken).ConfigureAwait(false);

        if (execResult.IsSuccess)
        {
            return execResult.Value;
        }

        if (_options.FailOpen)
        {
            CacheDiagnostics.RecordError(ProviderName, "FAIL_OPEN_MISS");
            return (false, Result<T?>.Success(default));
        }

        return (false, Result<T?>.Failure(execResult.Error));
    }

    /// <inheritdoc/>
    [UnconditionalSuppressMessage("AOT", "IL2026",
        Justification = "Serialization is handled by ICacheSerializer which can be configured for Native AOT.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Serialization is handled by ICacheSerializer which can be configured for Native AOT.")]
    public async Task<Result<T?>> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("get", ProviderName, key);

        var (_, result) = await TryGetInternalAsync<T>(key, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc/>
    [UnconditionalSuppressMessage("AOT", "IL2026",
        Justification = "Serialization is handled by ICacheSerializer which can be configured for Native AOT.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Serialization is handled by ICacheSerializer which can be configured for Native AOT.")]
    public async Task<Result<T>> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("get_or_create", ProviderName, key);

        // 1. Try reading from cache first (including cached null)
        var (cachedFound, cachedResult) = await TryGetInternalAsync<T>(key, cancellationToken).ConfigureAwait(false);
        if (cachedFound && cachedResult.IsSuccess)
        {
            return Result<T>.Success(cachedResult.Value!);
        }

        var lockKey = BuildLockKey(key);
        var lockToken = Guid.NewGuid().ToString("N");
        var db = GetDatabase();

        // 2. Attempt to acquire distributed lock via SETNX
        bool lockAcquired;
        try
        {
            lockAcquired = await db.StringSetAsync(lockKey, lockToken, _options.LockTimeout, When.NotExists).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            Log.GetFailed(_logger, key, ex);
            // If lock acquisition failed due to network, attempt fallback execution directly
            return await ExecuteFactoryAndCacheAsync(key, factory, options, cancellationToken).ConfigureAwait(false);
        }

        if (lockAcquired)
        {
            Log.LockAcquired(_logger, key);
            try
            {
                // Double-check inside distributed lock
                var (doubleFound, doubleCheck) = await TryGetInternalAsync<T>(key, cancellationToken).ConfigureAwait(false);
                if (doubleFound && doubleCheck.IsSuccess)
                {
                    CacheDiagnostics.RecordStampedePrevented(ProviderName);
                    return Result<T>.Success(doubleCheck.Value!);
                }

                return await ExecuteFactoryAndCacheAsync(key, factory, options, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await db.ScriptEvaluateAsync(UnlockLuaScript, keys: [lockKey], values: [lockToken]).ConfigureAwait(false);
                    Log.LockReleased(_logger, key);
                }
                catch (Exception ex) when (ex is RedisException or TimeoutException)
                {
                    Log.DeleteFailed(_logger, lockKey, ex);
                }
            }
        }

        // 3. Spin-wait polling if another instance holds the lock
        Log.LockWait(_logger, key);
        var waitStart = _timeProvider.GetUtcNow();
        while (_timeProvider.GetUtcNow() - waitStart < _options.MaxLockWaitTime)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(_options.LockRetryInterval, cancellationToken).ConfigureAwait(false);

            var (spinFound, spinCheck) = await TryGetInternalAsync<T>(key, cancellationToken).ConfigureAwait(false);
            if (spinFound && spinCheck.IsSuccess)
            {
                CacheDiagnostics.RecordStampedePrevented(ProviderName);
                return Result<T>.Success(spinCheck.Value!);
            }

            // Try to acquire the lock again in case previous holder finished
            try
            {
                if (await db.StringSetAsync(lockKey, lockToken, _options.LockTimeout, When.NotExists).ConfigureAwait(false))
                {
                    Log.LockAcquired(_logger, key);
                    try
                    {
                        var (recheckFound, recheck) = await TryGetInternalAsync<T>(key, cancellationToken).ConfigureAwait(false);
                        if (recheckFound && recheck.IsSuccess)
                        {
                            CacheDiagnostics.RecordStampedePrevented(ProviderName);
                            return Result<T>.Success(recheck.Value!);
                        }

                        return await ExecuteFactoryAndCacheAsync(key, factory, options, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        try
                        {
                            await db.ScriptEvaluateAsync(UnlockLuaScript, keys: [lockKey], values: [lockToken]).ConfigureAwait(false);
                            Log.LockReleased(_logger, key);
                        }
                        catch (Exception ex) when (ex is RedisException or TimeoutException)
                        {
                            Log.DeleteFailed(_logger, lockKey, ex);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                // Continue spin-waiting on transient lock check errors
            }
        }

        // 4. Timeout exhausted: final check before executing factory as fallback
        var (timeoutFound, timeoutCached) = await TryGetInternalAsync<T>(key, cancellationToken).ConfigureAwait(false);
        if (timeoutFound && timeoutCached.IsSuccess)
        {
            return Result<T>.Success(timeoutCached.Value!);
        }

        return await ExecuteFactoryAndCacheAsync(key, factory, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    [UnconditionalSuppressMessage("AOT", "IL2026",
        Justification = "Serialization is handled by ICacheSerializer which can be configured for Native AOT.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Serialization is handled by ICacheSerializer which can be configured for Native AOT.")]
    public async Task<Result<bool>> SetAsync<T>(
        string key,
        T value,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("set", ProviderName, key);

        var fullKey = BuildKey(key);
        string serialized;
        try
        {
            serialized = _serializer.Serialize(value);
        }
        catch (Exception ex)
        {
            Log.SetFailed(_logger, key, ex);
            CacheDiagnostics.RecordError(ProviderName, "SERIALIZATION_FAILED");
            return Result<bool>.Failure(CacheErrors.SerializationFailed(key, ex.Message));
        }

        var expiry = ResolveExpiry(options);

        return await ExecuteWithRetryAsync<bool>(
            nameof(SetAsync),
            async db =>
            {
                var result = await db.StringSetAsync(fullKey, serialized, expiry, false, When.Always, CommandFlags.None).ConfigureAwait(false);

                if (result)
                {
                    var keyTagsKey = BuildKeyTagsKey(fullKey);
                    var previousTags = await db.SetMembersAsync(keyTagsKey).ConfigureAwait(false);

                    var newTags = options?.Tags?
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .ToHashSet(StringComparer.Ordinal) ?? [];

                    // Remove fullKey from tags that are no longer associated
                    if (previousTags.Length > 0)
                    {
                        foreach (var prevTag in previousTags)
                        {
                            var prevTagStr = (string?)prevTag;
                            if (prevTagStr != null && !newTags.Contains(prevTagStr))
                            {
                                await db.SetRemoveAsync(BuildTagKey(prevTagStr), fullKey).ConfigureAwait(false);
                            }
                        }
                    }

                    if (newTags.Count > 0)
                    {
                        var tagRedisValues = new RedisValue[newTags.Count];
                        var idx = 0;
                        foreach (var tag in newTags)
                        {
                            tagRedisValues[idx++] = tag;
                            var tagKey = BuildTagKey(tag);
                            await db.SetAddAsync(tagKey, fullKey).ConfigureAwait(false);
                            if (expiry.HasValue)
                            {
                                await db.KeyExpireAsync(tagKey, expiry.Value.Add(TimeSpan.FromHours(1))).ConfigureAwait(false);
                            }
                        }

                        await db.KeyDeleteAsync(keyTagsKey).ConfigureAwait(false);
                        await db.SetAddAsync(keyTagsKey, tagRedisValues).ConfigureAwait(false);
                        if (expiry.HasValue)
                        {
                            await db.KeyExpireAsync(keyTagsKey, expiry.Value.Add(TimeSpan.FromHours(1))).ConfigureAwait(false);
                        }
                    }
                    else if (previousTags.Length > 0)
                    {
                        await db.KeyDeleteAsync(keyTagsKey).ConfigureAwait(false);
                    }
                }

                return Result<bool>.Success(result);
            },
            ex => Log.SetFailed(_logger, key, ex),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SetWithTagsAsync<T>(
        string key,
        T value,
        IEnumerable<string> tags,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(tags);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("set_with_tags", ProviderName, key);

        var tagList = tags.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        var mergedOptions = options ?? new CacheEntryOptions();
        mergedOptions.Tags = tagList;

        return await SetAsync(key, value, mergedOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("remove", ProviderName, key);

        var fullKey = BuildKey(key);
        return await ExecuteWithRetryAsync<bool>(
            nameof(RemoveAsync),
            async db =>
            {
                var keyTagsKey = BuildKeyTagsKey(fullKey);
                var previousTags = await db.SetMembersAsync(keyTagsKey).ConfigureAwait(false);
                if (previousTags.Length > 0)
                {
                    foreach (var prevTag in previousTags)
                    {
                        var prevTagStr = (string?)prevTag;
                        if (prevTagStr != null)
                        {
                            await db.SetRemoveAsync(BuildTagKey(prevTagStr), fullKey).ConfigureAwait(false);
                        }
                    }
                    await db.KeyDeleteAsync(keyTagsKey).ConfigureAwait(false);
                }

                var result = await db.KeyDeleteAsync(fullKey).ConfigureAwait(false);
                if (result)
                {
                    CacheDiagnostics.RecordEviction(ProviderName, "explicit");
                }
                return Result<bool>.Success(result);
            },
            ex => Log.DeleteFailed(_logger, key, ex),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("remove_by_prefix", ProviderName, prefix);

        var fullPrefix = EscapeRedisGlobPattern(BuildKey(prefix));
        return await ExecuteWithRetryAsync<bool>(
            nameof(RemoveByPrefixAsync),
            async db =>
            {
                var result = await db.ScriptEvaluateAsync(PrefixDeletionLuaScript, values: [fullPrefix]).ConfigureAwait(false);
                var count = (long)result;

                Log.PrefixDeletionCompleted(_logger, count, prefix);
                if (count > 0)
                {
                    CacheDiagnostics.RecordEviction(ProviderName, "prefix", count);
                }
                return Result<bool>.Success(count > 0);
            },
            ex => Log.PrefixDeletionFailed(_logger, prefix, ex),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("exists", ProviderName, key);

        var fullKey = BuildKey(key);
        var execResult = await ExecuteWithRetryAsync<bool>(
            nameof(ExistsAsync),
            async db =>
            {
                var exists = await db.KeyExistsAsync(fullKey).ConfigureAwait(false);
                return Result<bool>.Success(exists);
            },
            ex => Log.GetFailed(_logger, key, ex),
            cancellationToken).ConfigureAwait(false);

        if (execResult.IsSuccess)
        {
            return execResult;
        }

        if (_options.FailOpen)
        {
            CacheDiagnostics.RecordError(ProviderName, "FAIL_OPEN_EXISTS");
            return Result<bool>.Success(false);
        }

        return execResult;
    }

    /// <inheritdoc/>
    [UnconditionalSuppressMessage("AOT", "IL2026",
        Justification = "Serialization is handled by ICacheSerializer which can be configured for Native AOT.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Serialization is handled by ICacheSerializer which can be configured for Native AOT.")]
    public async Task<Result<IReadOnlyDictionary<string, T>>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("get_many", ProviderName, "multiple");

        var keyList = keys.Where(k => !string.IsNullOrEmpty(k)).Distinct().ToList();
        if (keyList.Count == 0)
        {
            return Result<IReadOnlyDictionary<string, T>>.Success(new Dictionary<string, T>(StringComparer.Ordinal));
        }

        var redisKeys = new RedisKey[keyList.Count];
        for (var i = 0; i < keyList.Count; i++)
        {
            redisKeys[i] = BuildKey(keyList[i]);
        }

        var execResult = await ExecuteWithRetryAsync<IReadOnlyDictionary<string, T>>(
            nameof(GetManyAsync),
            async db =>
            {
                var values = await db.StringGetAsync(redisKeys).ConfigureAwait(false);
                var dict = new Dictionary<string, T>(StringComparer.Ordinal);

                for (var i = 0; i < keyList.Count; i++)
                {
                    var val = values[i];
                    if (val.IsNull)
                    {
                        CacheDiagnostics.RecordMiss(ProviderName, nameof(GetManyAsync));
                        continue;
                    }

                    try
                    {
                        var deserialized = _serializer.Deserialize<T>(val.ToString());
                        if (deserialized is not null)
                        {
                            dict[keyList[i]] = deserialized;
                            CacheDiagnostics.RecordHit(ProviderName, nameof(GetManyAsync));
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.GetFailed(_logger, keyList[i], ex);
                        CacheDiagnostics.RecordError(ProviderName, "DESERIALIZATION_FAILED");
                    }
                }

                return Result<IReadOnlyDictionary<string, T>>.Success(dict);
            },
            ex => Log.GetFailed(_logger, "multiple", ex),
            cancellationToken).ConfigureAwait(false);

        if (execResult.IsSuccess)
        {
            return execResult;
        }

        if (_options.FailOpen)
        {
            CacheDiagnostics.RecordError(ProviderName, "FAIL_OPEN_MISS");
            return Result<IReadOnlyDictionary<string, T>>.Success(new Dictionary<string, T>(StringComparer.Ordinal));
        }

        return execResult;
    }

    /// <inheritdoc/>
    [UnconditionalSuppressMessage("AOT", "IL2026",
        Justification = "Serialization is handled by ICacheSerializer which can be configured for Native AOT.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Serialization is handled by ICacheSerializer which can be configured for Native AOT.")]
    public async Task<Result<bool>> SetManyAsync<T>(
        IEnumerable<KeyValuePair<string, T>> items,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("set_many", ProviderName, "multiple");

        var itemList = items.ToList();
        if (itemList.Count == 0)
        {
            return Result<bool>.Success(true);
        }

        var allSuccess = true;
        foreach (var kvp in itemList)
        {
            var res = await SetAsync(kvp.Key, kvp.Value, options, cancellationToken).ConfigureAwait(false);
            if (res.IsFailure || !res.Value)
            {
                allSuccess = false;
            }
        }

        return Result<bool>.Success(allSuccess);
    }

    /// <inheritdoc/>
    public async Task<Result<long>> RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("remove_many", ProviderName, "multiple");

        var keyList = keys.Where(k => !string.IsNullOrEmpty(k)).Distinct().ToList();
        if (keyList.Count == 0)
        {
            return Result<long>.Success(0);
        }

        var redisKeys = new RedisKey[keyList.Count];
        for (var i = 0; i < keyList.Count; i++)
        {
            redisKeys[i] = BuildKey(keyList[i]);
        }

        return await ExecuteWithRetryAsync<long>(
            nameof(RemoveManyAsync),
            async db =>
            {
                foreach (var rk in redisKeys)
                {
                    var fullKey = rk.ToString();
                    var keyTagsKey = BuildKeyTagsKey(fullKey);
                    var previousTags = await db.SetMembersAsync(keyTagsKey).ConfigureAwait(false);
                    if (previousTags.Length > 0)
                    {
                        foreach (var prevTag in previousTags)
                        {
                            var prevTagStr = (string?)prevTag;
                            if (prevTagStr != null)
                            {
                                await db.SetRemoveAsync(BuildTagKey(prevTagStr), fullKey).ConfigureAwait(false);
                            }
                        }
                        await db.KeyDeleteAsync(keyTagsKey).ConfigureAwait(false);
                    }
                }

                var deletedCount = await db.KeyDeleteAsync(redisKeys).ConfigureAwait(false);
                if (deletedCount > 0)
                {
                    CacheDiagnostics.RecordEviction(ProviderName, "explicit_bulk", deletedCount);
                }
                return Result<long>.Success(deletedCount);
            },
            ex => Log.DeleteFailed(_logger, "multiple", ex),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> ExpireAsync(string key, TimeSpan timeToLive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (timeToLive <= TimeSpan.Zero)
        {
            return Result<bool>.Failure(new Error("Cache.InvalidTimeToLive", "TimeToLive must be strictly positive."));
        }
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("expire", ProviderName, key);

        var fullKey = BuildKey(key);
        return await ExecuteWithRetryAsync<bool>(
            nameof(ExpireAsync),
            async db =>
            {
                var result = await db.KeyExpireAsync(fullKey, timeToLive).ConfigureAwait(false);
                return Result<bool>.Success(result);
            },
            ex => Log.SetFailed(_logger, key, ex),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> RefreshAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("refresh", ProviderName, key);

        var expiry = _options.DefaultAbsoluteExpiration;
        var fullKey = BuildKey(key);
        return await ExecuteWithRetryAsync<bool>(
            nameof(RefreshAsync),
            async db =>
            {
                var result = await db.KeyExpireAsync(fullKey, expiry).ConfigureAwait(false);
                return Result<bool>.Success(result);
            },
            ex => Log.SetFailed(_logger, key, ex),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("remove_by_tag", ProviderName, tag);

        var tagKey = BuildTagKey(tag);
        return await ExecuteWithRetryAsync<bool>(
            nameof(RemoveByTagAsync),
            async db =>
            {
                var result = await db.ScriptEvaluateAsync(TagDeletionLuaScript, keys: [tagKey]).ConfigureAwait(false);
                var count = (long)result;

                Log.TagDeletionCompleted(_logger, count, tag);
                if (count > 0)
                {
                    CacheDiagnostics.RecordEviction(ProviderName, "tag", count);
                }
                return Result<bool>.Success(count > 0);
            },
            ex => Log.TagDeletionFailed(_logger, tag, ex),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> RemoveByTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tags);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = CacheDiagnostics.StartActivity("remove_by_tags", ProviderName, "multiple");

        var allSucceeded = true;
        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            var result = await RemoveByTagAsync(tag, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                allSucceeded = false;
            }
        }

        return Result<bool>.Success(allSucceeded);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsConnection)
        {
            _connection.Dispose();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_ownsConnection && _connection is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else if (_ownsConnection)
        {
            _connection.Dispose();
        }
    }

    private async Task<Result<TResult>> ExecuteWithRetryAsync<TResult>(
        string operation,
        Func<IDatabase, Task<Result<TResult>>> action,
        Action<Exception> logTerminalFailure,
        CancellationToken cancellationToken)
    {
        var maxRetries = Math.Max(0, _options.MaxRetries);
        var attempt = 0;

        while (true)
        {
            try
            {
                var db = GetDatabase();
                return await action(db).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                if (attempt < maxRetries && !cancellationToken.IsCancellationRequested)
                {
                    attempt++;
                    Log.RetryAttempt(_logger, operation, attempt, maxRetries, ex);

                    // Exponential backoff with full randomized jitter to prevent retry storms
                    var baseDelayMs = Math.Max(1.0, _options.RetryDelay.TotalMilliseconds);
                    var maxJitter = Math.Min(baseDelayMs * Math.Pow(2, attempt - 1), 30_000.0);
                    var randomizedMs = Random.Shared.NextDouble() * maxJitter;
                    var delay = TimeSpan.FromMilliseconds(Math.Max(5.0, randomizedMs));

                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                logTerminalFailure(ex);
                CacheDiagnostics.RecordError(ProviderName, "CONNECTION_FAILED");
                return Result<TResult>.Failure(CacheErrors.ConnectionFailed(ex.Message));
            }
        }
    }

    private async Task<Result<T>> ExecuteFactoryAndCacheAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        CacheEntryOptions? options,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await factory(cancellationToken).ConfigureAwait(false);
            await SetAsync(key, value, options, cancellationToken).ConfigureAwait(false);
            return Result<T>.Success(value);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.FactoryFailed(_logger, key, ex);
            CacheDiagnostics.RecordError(ProviderName, "FACTORY_FAILED");
            return Result<T>.Failure(CacheErrors.FactoryFailed(key, ex.Message));
        }
    }

    private IDatabase GetDatabase() => _connection.GetDatabase(_options.Database);

    private string BuildKey(string key) =>
        string.IsNullOrEmpty(_options.InstanceName) ? key : $"{_options.InstanceName}:{key}";

    private string BuildLockKey(string key) =>
        $"{BuildKey(key)}:__lock";

    private string BuildTagKey(string tag) =>
        string.IsNullOrEmpty(_options.InstanceName) ? $"tag:{tag}" : $"{_options.InstanceName}:tag:{tag}";

    private static string BuildKeyTagsKey(string fullKey) =>
        $"{fullKey}:__tags";

    private TimeSpan? ResolveExpiry(CacheEntryOptions? options) =>
        options?.AbsoluteExpirationRelativeToNow ?? options?.SlidingExpiration ?? _options.DefaultAbsoluteExpiration;

    private static string EscapeRedisGlobPattern(string input) =>
        input
            .Replace(@"\", @"\\")
            .Replace("*", @"\*")
            .Replace("?", @"\?")
            .Replace("[", @"\[")
            .Replace("]", @"\]");
}
