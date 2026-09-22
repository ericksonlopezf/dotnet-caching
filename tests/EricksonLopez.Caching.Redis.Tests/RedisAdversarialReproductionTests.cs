// Copyright © Erickson Lopez. MIT License.
using System;
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
/// Reproduction test suite proving Redis provider defects found during audit.
/// </summary>
public sealed class RedisAdversarialReproductionTests
{
    private readonly IConnectionMultiplexer _multiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly RedisCacheOptions _options = new()
    {
        InstanceName = "repro_app",
        Database = 0,
        MaxRetries = 2,
        RetryDelay = TimeSpan.FromMilliseconds(5)
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
    public async Task Defect_RedisTimeoutException_IsNowHandledGracefully_ReturnsFailureResult()
    {
        // RedisTimeoutException inherits from System.TimeoutException, NOT RedisException!
        typeof(RedisTimeoutException).IsSubclassOf(typeof(RedisException)).Should().BeFalse(
            "PROOF OF TYPE HIERARCHY: RedisTimeoutException inherits from TimeoutException, NOT RedisException");

        var provider = CreateProvider();
        _database.StringGetAsync(Arg.Any<RedisKey>())
            .ThrowsAsync(new RedisTimeoutException(CommandFlags.None, "Timeout performing GET (5000ms)", CommandStatus.WaitingToBeSent));

        // VERIFIED REMEDIATION: calling GetAsync now catches TimeoutException and returns Failure result instead of crashing
        var result = await provider.GetAsync<string>("timeout_key");
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cache.Redis.ConnectionFailed");
        result.Error.Description.Should().Contain("Timeout performing GET");
    }

    [Fact]
    public async Task Defect_SlidingExpiration_DoesNotSlideOnGetAsync_InRedis()
    {
        var provider = CreateProvider();
        _database.StringGetAsync(Arg.Any<RedisKey>())
            .Returns((RedisValue)"\"data\"");

        // Read item with sliding expiration
        var result = await provider.GetAsync<string>("sliding_key");
        result.IsSuccess.Should().BeTrue();

        // DOCUMENTED DEFECT: KeyExpireAsync is NEVER called on read, so TTL never slides in Redis!
        await _database.DidNotReceiveWithAnyArgs().KeyExpireAsync(default!, default(TimeSpan?));
    }
}
