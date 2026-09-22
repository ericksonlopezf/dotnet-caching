// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Caching.Redis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;
using Xunit;

namespace EricksonLopez.Caching.Redis.Tests;

/// <summary>
/// Chaos engineering test suite for Redis L2 cache provider.
/// Simulates network partitions, transient faults, timeout cascades, and connection pool exhaustion.
/// </summary>
public sealed class ChaosResilienceTests
{
    private readonly IConnectionMultiplexer _multiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly RedisCacheOptions _options = new()
    {
        InstanceName = "chaos_app",
        Database = 0,
        MaxRetries = 3,
        RetryDelay = TimeSpan.FromMilliseconds(2),
        LockTimeout = TimeSpan.FromSeconds(5),
        LockRetryInterval = TimeSpan.FromMilliseconds(5),
        MaxLockWaitTime = TimeSpan.FromMilliseconds(50)
    };

    private RedisCacheProvider CreateProvider()
    {
        _multiplexer.GetDatabase(_options.Database).Returns(_database);
        return new RedisCacheProvider(
            _multiplexer,
            Options.Create(_options),
            NullLogger<RedisCacheProvider>.Instance,
            SystemTextJsonCacheSerializer.Default);
    }

    [Fact]
    public async Task Chaos_TransientRedisFailure_RecoversOnSecondAttempt()
    {
        var provider = CreateProvider();
        const string key = "transient_fail_key";

        // First attempt throws, second succeeds
        var callCount = 0;
        _database.StringGetAsync(Arg.Any<RedisKey>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new RedisConnectionException(ConnectionFailureType.SocketClosed, CommandFlags.None, "Socket closed abruptly", null, CommandStatus.Unknown);
                }
                return (RedisValue)"\"recovered_value\"";
            });

        var result = await provider.GetAsync<string>(key);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("recovered_value");
        callCount.Should().Be(2, "operation should succeed after transient retry");
    }

    [Fact]
    public async Task Chaos_RedisConnectionFailureCascade_RetriesUpToMaxAndFailsGracefully()
    {
        var provider = CreateProvider();
        const string key = "conn_cascade_key";

        _database.StringGetAsync(Arg.Any<RedisKey>())
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "Connection failure", null, CommandStatus.Unknown));

        var result = await provider.GetAsync<string>(key);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cache.Redis.ConnectionFailed");
        // 1 initial + 3 retries = 4 attempts total
        await _database.Received(4).StringGetAsync(Arg.Any<RedisKey>());
    }

    [Fact]
    public async Task Chaos_PreCancelledToken_DoesNotAttemptRedisOperations()
    {
        var provider = CreateProvider();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => provider.GetAsync<string>("any_key", cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        await _database.DidNotReceive().StringGetAsync(Arg.Any<RedisKey>());
    }

    [Fact]
    public async Task Chaos_SetAsync_WhenRedisFailsPermanently_ReturnsFailureResult()
    {
        var provider = CreateProvider();
        _database.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "Endpoint unreachable", null, CommandStatus.Unknown));

        var result = await provider.SetAsync("fail_key", "fail_val");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cache.Redis.ConnectionFailed");
    }

    [Fact]
    public async Task Chaos_GetAsync_WhenFailOpenEnabled_ReturnsSuccessDefaultOnRedisFailure()
    {
        var options = new RedisCacheOptions
        {
            InstanceName = "chaos_failopen",
            Database = 0,
            MaxRetries = 1,
            RetryDelay = TimeSpan.FromMilliseconds(1),
            FailOpen = true
        };

        _multiplexer.GetDatabase(options.Database).Returns(_database);
        var provider = new RedisCacheProvider(
            _multiplexer,
            Options.Create(options),
            NullLogger<RedisCacheProvider>.Instance,
            SystemTextJsonCacheSerializer.Default);

        _database.StringGetAsync(Arg.Any<RedisKey>())
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "Redis cluster down", null, CommandStatus.Unknown));

        var result = await provider.GetAsync<string>("any_key");

        result.IsSuccess.Should().BeTrue("FailOpen must degrade to a cache miss instead of an error result");
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task Chaos_ExistsAsync_WhenFailOpenEnabled_ReturnsSuccessFalseOnRedisFailure()
    {
        var options = new RedisCacheOptions
        {
            InstanceName = "chaos_failopen",
            Database = 0,
            MaxRetries = 1,
            RetryDelay = TimeSpan.FromMilliseconds(1),
            FailOpen = true
        };

        _multiplexer.GetDatabase(options.Database).Returns(_database);
        var provider = new RedisCacheProvider(
            _multiplexer,
            Options.Create(options),
            NullLogger<RedisCacheProvider>.Instance,
            SystemTextJsonCacheSerializer.Default);

        _database.KeyExistsAsync(Arg.Any<RedisKey>())
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "Redis cluster down", null, CommandStatus.Unknown));

        var result = await provider.ExistsAsync("any_key");

        result.IsSuccess.Should().BeTrue("FailOpen must return false instead of error result when Redis is unreachable");
        result.Value.Should().BeFalse();
    }
}
