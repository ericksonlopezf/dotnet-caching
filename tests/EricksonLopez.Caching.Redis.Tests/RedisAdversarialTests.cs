// Copyright © Erickson Lopez. MIT License.
using System;
using System.Text.Json;
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
/// Adversarial security, chaos, and resilience test suite for Redis L2 cache provider.
/// Evaluates behavior under network failures, corrupted payloads, Lua script crashes, and lock contention.
/// </summary>
public sealed class RedisAdversarialTests
{
    private readonly IConnectionMultiplexer _multiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly RedisCacheOptions _options = new()
    {
        InstanceName = "adversarial_app",
        Database = 1,
        MaxRetries = 2,
        RetryDelay = TimeSpan.FromMilliseconds(5),
        LockTimeout = TimeSpan.FromSeconds(2),
        LockRetryInterval = TimeSpan.FromMilliseconds(10),
        MaxLockWaitTime = TimeSpan.FromMilliseconds(50)
    };

    private RedisCacheProvider CreateProvider(ICacheSerializer? serializer = null)
    {
        _multiplexer.GetDatabase(_options.Database).Returns(_database);
        return new RedisCacheProvider(
            _multiplexer,
            Options.Create(_options),
            NullLogger<RedisCacheProvider>.Instance,
            serializer ?? SystemTextJsonCacheSerializer.Default);
    }

    [Fact]
    public async Task DeserializationAttack_MalformedJsonPayload_ReturnsFailureResultGracefully()
    {
        // Arrange
        var provider = CreateProvider();
        const string key = "poisoned_key";
        // Malformed JSON that cannot be parsed
        _database.StringGetAsync(Arg.Any<RedisKey>())
            .Returns((RedisValue)"{ not valid json at all ::: [[");

        // Act - Safely calls GetAsync without throwing JsonException
        var result = await provider.GetAsync<TestDto>(key);

        // Assert - Evidence: returns Failure result instead of crashing application
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cache.Redis.DeserializationFailed");
        result.Error.Description.Should().Contain("poisoned_key");
    }

    [Fact]
    public async Task SerializationFailure_WhenSerializerThrows_ReturnsFailureResult()
    {
        // Arrange
        var failingSerializer = Substitute.For<ICacheSerializer>();
        failingSerializer.Serialize(Arg.Any<object>()).Returns(_ => throw new InvalidOperationException("Serialization recursion depth exceeded"));
        var provider = CreateProvider(failingSerializer);
        const string key = "fail_serialize_key";

        // Act
        var result = await provider.SetAsync(key, new TestDto("test", 123));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cache.Redis.SerializationFailed");
        result.Error.Description.Should().Contain("fail_serialize_key");
    }

    [Fact]
    public async Task Chaos_RedisConnectionCrash_RetriesAndReturnsFailureResult()
    {
        // Arrange
        var provider = CreateProvider();
        _database.StringGetAsync(Arg.Any<RedisKey>())
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "Redis instance crashed", null, CommandStatus.Unknown));

        // Act
        var result = await provider.GetAsync<string>("any_key");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cache.Redis.ConnectionFailed");
        // Verify retry occurred (initial attempt + 2 retries = 3 calls)
        await _database.Received(3).StringGetAsync(Arg.Any<RedisKey>());
    }

    [Fact]
    public async Task Chaos_LockTimeoutExhausted_FallsBackToFactoryExecution()
    {
        // Arrange
        var provider = CreateProvider();
        const string key = "contested_key";

        // Initial cache miss
        _database.StringGetAsync(Arg.Any<RedisKey>()).Returns(RedisValue.Null);
        // Lock acquisition constantly fails (held by another node)
        _database.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), When.NotExists)
            .Returns(false);

        var factoryExecuted = false;

        // Act
        var result = await provider.GetOrCreateAsync(key, _ =>
        {
            factoryExecuted = true;
            return Task.FromResult("fallback_computed_value");
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("fallback_computed_value");
        factoryExecuted.Should().BeTrue("when lock wait time expires, fallback factory execution must be triggered");
    }

    [Fact]
    public async Task PrefixWildcardInjection_VerifyScriptArguments()
    {
        // Arrange
        var provider = CreateProvider();
        const string rawPrefix = "tenant1*"; // Attacker attempts glob wildcard injection

        _database.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
            .Returns(RedisResult.Create((RedisValue)5, ResultType.Integer));

        // Act
        var result = await provider.RemoveByPrefixAsync(rawPrefix);

        // Assert
        result.IsSuccess.Should().BeTrue();
        // Check what value was passed to Lua script
        await _database.Received(1).ScriptEvaluateAsync(
            Arg.Any<string>(),
            null,
            Arg.Is<RedisValue[]>(v => v.Length == 1 && v[0].ToString() == @"adversarial_app:tenant1\*"));
    }

    [Fact]
    public async Task FactoryException_ReturnsCacheErrorsFactoryFailed()
    {
        // Arrange
        var provider = CreateProvider();
        const string key = "fail_factory_key";
        _database.StringGetAsync(Arg.Any<RedisKey>()).Returns(RedisValue.Null);
        _database.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), When.NotExists)
            .Returns(true);

        // Act
        var result = await provider.GetOrCreateAsync<string>(key, _ => throw new InvalidOperationException("External microservice down"));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cache.Redis.FactoryFailed");
        result.Error.Description.Should().Contain("External microservice down");
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenNullValueCachedInRedis_ReturnsSuccessNullWithoutInvokingFactory()
    {
        // Arrange
        var provider = CreateProvider();
        const string key = "cached_null_key";
        var factoryInvoked = false;
        // In Redis, cached null is serialized as JSON "null", so StringGet returns "null" (not RedisValue.Null)
        _database.StringGetAsync(Arg.Any<RedisKey>()).Returns((RedisValue)"null");

        // Act
        var result = await provider.GetOrCreateAsync<string?>(key, _ =>
        {
            factoryInvoked = true;
            return Task.FromResult<string?>("new_val");
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
        factoryInvoked.Should().BeFalse();
    }

    private sealed record TestDto(string Name, int Value);
}
