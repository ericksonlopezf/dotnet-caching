// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Caching.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;
using Xunit;

namespace EricksonLopez.Caching.Redis.Tests;

/// <summary>
/// Unit tests for <see cref="RedisCacheProvider"/> using a mocked <see cref="IConnectionMultiplexer"/>.
/// </summary>
public sealed class RedisCacheProviderTests
{
    private readonly IConnectionMultiplexer _multiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly RedisCacheOptions _options = new()
    {
        InstanceName = "test",
        Database = 0,
        DefaultAbsoluteExpiration = TimeSpan.FromMinutes(5),
        MaxRetries = 2,
        RetryDelay = TimeSpan.FromMilliseconds(5),
        LockTimeout = TimeSpan.FromSeconds(5),
        LockRetryInterval = TimeSpan.FromMilliseconds(10),
        MaxLockWaitTime = TimeSpan.FromMilliseconds(100)
    };

    private RedisCacheProvider CreateProvider(ICacheSerializer? serializer = null)
    {
        _multiplexer.GetDatabase(_options.Database).Returns(_database);
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<bool>(),
            Arg.Any<When>(),
            Arg.Any<CommandFlags>())
            .Returns(true);
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>(),
            Arg.Any<CommandFlags>())
            .Returns(true);
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>())
            .Returns(true);

        return new RedisCacheProvider(
            _multiplexer,
            Options.Create(_options),
            NullLogger<RedisCacheProvider>.Instance,
            serializer);
    }

    [Fact]
    public async Task GetAsync_WhenKeyDoesNotExist_ReturnsSuccessWithNull()
    {
        var provider = CreateProvider();
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var result = await provider.GetAsync<string>("missing:key");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_WhenKeyExists_DeserializesAndReturnsValue()
    {
        var provider = CreateProvider();
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue("\"cached-value\""));

        var result = await provider.GetAsync<string>("user:42");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("cached-value");
    }

    [Fact]
    public async Task GetAsync_WhenRedisThrows_RetriesAndReturnsFailure()
    {
        var provider = CreateProvider();
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Throws(new RedisException("connection error"));

        var result = await provider.GetAsync<string>("some:key");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cache.Redis.ConnectionFailed");
        // 1 initial + 2 retries = 3 calls
        await _database.Received(3).StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task SetAsync_WhenSuccessful_ReturnsTrue()
    {
        var provider = CreateProvider();

        var result = await provider.SetAsync("order:99", "paid");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task SetAsync_WhenRedisThrows_RetriesAndReturnsFailure()
    {
        var provider = CreateProvider();
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<bool>(),
            Arg.Any<When>(),
            Arg.Any<CommandFlags>())
            .Throws(new RedisException("timeout"));

        var result = await provider.SetAsync("order:99", "paid");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cache.Redis.ConnectionFailed");
    }

    [Fact]
    public async Task RemoveAsync_WhenKeyExists_ReturnsTrue()
    {
        var provider = CreateProvider();
        _database.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(true);

        var result = await provider.RemoveAsync("user:42");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task RemoveAsync_WhenKeyDoesNotExist_ReturnsFalse()
    {
        var provider = CreateProvider();
        _database.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(false);

        var result = await provider.RemoveAsync("user:nonexistent");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task BuildKey_PrefixesWithInstanceName()
    {
        var provider = CreateProvider();
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        await provider.GetAsync<string>("product:1");

        await _database.Received(1).StringGetAsync(
            Arg.Is<RedisKey>(k => k.ToString() == "test:product:1"),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task GetAsync_WithNullKey_ThrowsArgumentNullException()
    {
        var provider = CreateProvider();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            provider.GetAsync<string>(null!));
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenCacheHit_DoesNotCallFactory()
    {
        var provider = CreateProvider();
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue("\"cached\""));

        var factoryCallCount = 0;

        var result = await provider.GetOrCreateAsync<string>(
            "session:1",
            async _ => { factoryCallCount++; return await Task.FromResult("factory-value"); });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("cached");
        factoryCallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenCacheMiss_AcquiresDistributedLockAndCaches()
    {
        var provider = CreateProvider();
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var factoryCallCount = 0;

        var result = await provider.GetOrCreateAsync<string>(
            "session:2",
            async _ => { factoryCallCount++; return await Task.FromResult("factory-value"); });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("factory-value");
        factoryCallCount.Should().Be(1);

        // Verify lock release Lua script was evaluated
        await _database.Received().ScriptEvaluateAsync(
            Arg.Is<string>(s => s.Contains("redis.call('DEL'")),
            Arg.Is<RedisKey[]>(keys => keys[0].ToString().EndsWith(":__lock")),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoveByPrefixAsync_WhenEmptyOrWhitespacePrefix_ThrowsArgumentException(string prefix)
    {
        var provider = CreateProvider();

        var act = () => provider.RemoveByPrefixAsync(prefix);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RemoveByPrefixAsync_ValidPrefix_ExecutesScanLuaScript()
    {
        var provider = CreateProvider();
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create((RedisValue)3L));

        var result = await provider.RemoveByPrefixAsync("tenant:10:");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoveByTagAsync_WhenEmptyOrWhitespaceTag_ThrowsArgumentException(string tag)
    {
        var provider = CreateProvider();

        var act = () => provider.RemoveByTagAsync(tag);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SetWithTagsAsync_StoresKeyAndAssociatesTag()
    {
        var provider = CreateProvider();

        var result = await provider.SetWithTagsAsync("item:1", "Data", ["catalog", "inventory"]);

        result.IsSuccess.Should().BeTrue();
        await _database.Received(1).SetAddAsync(
            Arg.Is<RedisKey>(k => k.ToString() == "test:tag:catalog"),
            Arg.Is<RedisValue>(v => v.ToString() == "test:item:1"),
            Arg.Any<CommandFlags>());
        await _database.Received(1).SetAddAsync(
            Arg.Is<RedisKey>(k => k.ToString() == "test:tag:inventory"),
            Arg.Is<RedisValue>(v => v.ToString() == "test:item:1"),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task RemoveByTagAsync_ValidTag_ExecutesTagDeletionLuaScript()
    {
        var provider = CreateProvider();
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create((RedisValue)2L));

        var result = await provider.RemoveByTagAsync("catalog");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task RemoveByTagsAsync_MultipleTags_ExecutesDeletionForEachTag()
    {
        var provider = CreateProvider();
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create((RedisValue)1L));

        var result = await provider.RemoveByTagsAsync(["tag1", "tag2"]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task CustomSerializer_IsUsedWhenConfigured()
    {
        var customSerializer = Substitute.For<ICacheSerializer>();
        customSerializer.Serialize("hello").Returns("\"custom-hello\"");
        customSerializer.Deserialize<string>("\"custom-hello\"").Returns("hello");

        var provider = CreateProvider(customSerializer);

        await provider.SetAsync("test:custom", "hello");
        customSerializer.Received(1).Serialize("hello");

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue("\"custom-hello\""));

        var getResult = await provider.GetAsync<string>("test:custom");
        getResult.Value.Should().Be("hello");
        customSerializer.Received(1).Deserialize<string>("\"custom-hello\"");
    }

    [Fact]
    public void Constructor_NullArguments_ThrowsArgumentNullException()
    {
        var act1 = () => new RedisCacheProvider(null!, Options.Create(_options), NullLogger<RedisCacheProvider>.Instance);
        var act2 = () => new RedisCacheProvider(_multiplexer, null!, NullLogger<RedisCacheProvider>.Instance);
        var act3 = () => new RedisCacheProvider(_multiplexer, Options.Create(_options), null!);

        act1.Should().Throw<ArgumentNullException>().WithParameterName("connection");
        act2.Should().Throw<ArgumentNullException>().WithParameterName("options");
        act3.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task GuardClauses_NullArguments_ThrowAppropriateExceptions()
    {
        var provider = CreateProvider();

        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.GetOrCreateAsync<string>(null!, _ => Task.FromResult("val")));
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.GetOrCreateAsync<string>("k", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.SetAsync<string>(null!, "val"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.SetWithTagsAsync<string>(null!, "val", ["tag"]));
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.SetWithTagsAsync<string>("k", "val", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.RemoveAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.RemoveByPrefixAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.RemoveByTagAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.RemoveByTagsAsync(null!));
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenLockThrowsRedisException_ExecutesFactoryAsFallback()
    {
        var provider = CreateProvider();
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>())
            .Throws(new RedisException("Lock cluster error"));

        var result = await provider.GetOrCreateAsync<string>("resilient:key", _ => Task.FromResult("fallback_val"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("fallback_val");
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenDoubleCheckHitsInsideLock_ReturnsCachedValue()
    {
        var provider = CreateProvider();
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(
                RedisValue.Null,
                new RedisValue("\"populated_by_concurrent\""));

        var factoryCalled = false;
        var result = await provider.GetOrCreateAsync<string>("race:key", _ =>
        {
            factoryCalled = true;
            return Task.FromResult("factory");
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("populated_by_concurrent");
        factoryCalled.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenUnlockLuaThrowsRedisException_DoesNotFailOperation()
    {
        var provider = CreateProvider();
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);
        _database.ScriptEvaluateAsync(
            Arg.Is<string>(s => s.Contains("redis.call('DEL'")),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Throws(new RedisException("Unlock timeout"));

        var result = await provider.GetOrCreateAsync<string>("unlock_err:key", _ => Task.FromResult("computed"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("computed");
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenFactoryThrowsGeneralException_ReturnsFactoryFailedError()
    {
        var provider = CreateProvider();
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var result = await provider.GetOrCreateAsync<string>(
            "factory_fail:key",
            _ => throw new InvalidOperationException("External API failed"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cache.Redis.FactoryFailed");
        result.Error.Description.Should().Contain("factory_fail:key");
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenFactoryThrowsOperationCanceledException_RethrowsImmediately()
    {
        var provider = CreateProvider();
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var act = () => provider.GetOrCreateAsync<string>(
            "canceled:key",
            _ => throw new OperationCanceledException());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetOrCreateAsync_SpinWait_WhenAnotherInstancePopulatesCache_ReturnsPopulatedValue()
    {
        var options = new RedisCacheOptions
        {
            InstanceName = "test",
            LockTimeout = TimeSpan.FromSeconds(5),
            LockRetryInterval = TimeSpan.FromMilliseconds(10),
            MaxLockWaitTime = TimeSpan.FromMilliseconds(200)
        };
        _multiplexer.GetDatabase(options.Database).Returns(_database);

        var provider = new RedisCacheProvider(
            _multiplexer,
            Options.Create(options),
            NullLogger<RedisCacheProvider>.Instance);

        // First call: null (initial miss)
        // Lock acquisition fails (already held)
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>())
            .Returns(false);

        // Spin-check: returns populated value
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(
                RedisValue.Null,
                new RedisValue("\"spin_cached_value\""));

        var factoryCalled = false;
        var result = await provider.GetOrCreateAsync<string>("spin:key", _ =>
        {
            factoryCalled = true;
            return Task.FromResult("factory");
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("spin_cached_value");
        factoryCalled.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrCreateAsync_SpinWait_TimeoutExhausted_ExecutesFactoryAsFallback()
    {
        var options = new RedisCacheOptions
        {
            InstanceName = "test",
            LockTimeout = TimeSpan.FromSeconds(5),
            LockRetryInterval = TimeSpan.FromMilliseconds(10),
            MaxLockWaitTime = TimeSpan.FromMilliseconds(50)
        };
        _multiplexer.GetDatabase(options.Database).Returns(_database);

        var provider = new RedisCacheProvider(
            _multiplexer,
            Options.Create(options),
            NullLogger<RedisCacheProvider>.Instance);

        // Always miss on GET
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);
        // Lock acquisition always fails
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>())
            .Returns(false);

        var result = await provider.GetOrCreateAsync<string>("timeout_spin:key", _ => Task.FromResult("fallback_after_timeout"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("fallback_after_timeout");
    }

    [Fact]
    public async Task SetAsync_WithSlidingExpirationAndTags_SetsKeyExpiryAndTagSets()
    {
        var provider = CreateProvider();
        var options = new CacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(10),
            Tags = ["tagA", "", "   "]
        };

        var result = await provider.SetAsync("item:sliding", "data", options);

        result.IsSuccess.Should().BeTrue();
        await _database.Received(1).SetAddAsync(
            Arg.Is<RedisKey>(k => k.ToString() == "test:tag:tagA"),
            Arg.Is<RedisValue>(v => v.ToString() == "test:item:sliding"),
            Arg.Any<CommandFlags>());
        await _database.Received(1).KeyExpireAsync(
            Arg.Is<RedisKey>(k => k.ToString() == "test:tag:tagA"),
            Arg.Is<TimeSpan?>(t => t.HasValue && t.Value > TimeSpan.FromHours(1)));
    }

    [Fact]
    public async Task RemoveByPrefixAsync_WhenCountIsZero_ReturnsSuccessFalse()
    {
        var provider = CreateProvider();
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create((RedisValue)0L));

        var result = await provider.RemoveByPrefixAsync("prefix:empty:");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveByTagAsync_WhenCountIsZero_ReturnsSuccessFalse()
    {
        var provider = CreateProvider();
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create((RedisValue)0L));

        var result = await provider.RemoveByTagAsync("tag_empty");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveByTagsAsync_SkipsWhitespaceAndReturnsFalseWhenOneFails()
    {
        var provider = CreateProvider();
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(
                _ => RedisResult.Create((RedisValue)1L),
                _ => throw new RedisException("Tag deletion error"));

        var result = await provider.RemoveByTagsAsync(["tag1", "", "   ", "tag_fail"]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task BuildKey_WithoutInstanceName_DoesNotPrependPrefix()
    {
        var noPrefixOptions = new RedisCacheOptions
        {
            InstanceName = null
        };
        _multiplexer.GetDatabase(noPrefixOptions.Database).Returns(_database);
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var provider = new RedisCacheProvider(
            _multiplexer,
            Options.Create(noPrefixOptions),
            NullLogger<RedisCacheProvider>.Instance);

        await provider.GetAsync<string>("standalone_key");

        await _database.Received(1).StringGetAsync(
            Arg.Is<RedisKey>(k => k.ToString() == "standalone_key"),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task DisposeAsync_WhenExternalConnection_DoesNotDisposeMultiplexer()
    {
        var provider = CreateProvider();

        await provider.DisposeAsync();

        _multiplexer.DidNotReceive().Dispose();
    }

    [Fact]
    public async Task DisposeAsync_WhenOwnsConnection_DisposesMultiplexer()
    {
        var internalProvider = new RedisCacheProvider(
            _multiplexer,
            _options,
            NullLogger<RedisCacheProvider>.Instance,
            ownsConnection: true);

        await internalProvider.DisposeAsync();

        await ((IAsyncDisposable)_multiplexer).Received(1).DisposeAsync();
    }

    [Fact]
    public async Task GetOrCreateAsync_SpinWait_LockAcquiredOnSecondAttempt_ExecutesFactory()
    {
        var options = new RedisCacheOptions
        {
            InstanceName = "test",
            LockTimeout = TimeSpan.FromSeconds(5),
            LockRetryInterval = TimeSpan.FromMilliseconds(10),
            MaxLockWaitTime = TimeSpan.FromSeconds(2)
        };
        _multiplexer.GetDatabase(options.Database).Returns(_database);

        var provider = new RedisCacheProvider(
            _multiplexer,
            Options.Create(options),
            NullLogger<RedisCacheProvider>.Instance);

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        // First attempt fails, second attempt succeeds
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>())
            .Returns(false, true);

        var factoryCalled = false;
        var result = await provider.GetOrCreateAsync<string>("reacquired:key", _ =>
        {
            factoryCalled = true;
            return Task.FromResult("reacquired_val");
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("reacquired_val");
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public async Task GetOrCreateAsync_SpinWait_LockAcquiredOnSecondAttempt_DoubleCheckHits()
    {
        var options = new RedisCacheOptions
        {
            InstanceName = "test",
            LockTimeout = TimeSpan.FromSeconds(5),
            LockRetryInterval = TimeSpan.FromMilliseconds(10),
            MaxLockWaitTime = TimeSpan.FromSeconds(2)
        };
        _multiplexer.GetDatabase(options.Database).Returns(_database);

        var provider = new RedisCacheProvider(
            _multiplexer,
            Options.Create(options),
            NullLogger<RedisCacheProvider>.Instance);

        // Initial GET null, spin check null, recheck inside lock returns populated
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(
                RedisValue.Null,
                RedisValue.Null,
                new RedisValue("\"hit_on_recheck\""));

        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>())
            .Returns(false, true);

        var factoryCalled = false;
        var result = await provider.GetOrCreateAsync<string>("recheck_spin:key", _ =>
        {
            factoryCalled = true;
            return Task.FromResult("should_not_call");
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hit_on_recheck");
        factoryCalled.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrCreateAsync_SpinWait_WhenLockThrowsRedisException_ContinuesSpinWait()
    {
        var options = new RedisCacheOptions
        {
            InstanceName = "test",
            LockTimeout = TimeSpan.FromSeconds(5),
            LockRetryInterval = TimeSpan.FromMilliseconds(10),
            MaxLockWaitTime = TimeSpan.FromMilliseconds(60)
        };
        _multiplexer.GetDatabase(options.Database).Returns(_database);

        var provider = new RedisCacheProvider(
            _multiplexer,
            Options.Create(options),
            NullLogger<RedisCacheProvider>.Instance);

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>())
            .Throws(new RedisException("Transient check error"));

        var result = await provider.GetOrCreateAsync<string>("spin_ex:key", _ => Task.FromResult("fallback_after_ex"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("fallback_after_ex");
    }

    [Fact]
    public async Task GetOrCreateAsync_SpinWait_WhenUnlockThrowsRedisException_OperationStillSucceeds()
    {
        var options = new RedisCacheOptions
        {
            InstanceName = "test",
            LockTimeout = TimeSpan.FromSeconds(5),
            LockRetryInterval = TimeSpan.FromMilliseconds(10),
            MaxLockWaitTime = TimeSpan.FromSeconds(2)
        };
        _multiplexer.GetDatabase(options.Database).Returns(_database);

        var provider = new RedisCacheProvider(
            _multiplexer,
            Options.Create(options),
            NullLogger<RedisCacheProvider>.Instance);

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>())
            .Returns(false, true);

        _database.ScriptEvaluateAsync(
            Arg.Is<string>(s => s.Contains("redis.call('DEL'")),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Throws(new RedisException("Unlock error on spin"));

        var result = await provider.GetOrCreateAsync<string>("unlock_spin_err:key", _ => Task.FromResult("spin_val"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("spin_val");
    }

    [Fact]
    public async Task BuildTagKey_WithoutInstanceName_DoesNotPrependPrefix()
    {
        var noPrefixOptions = new RedisCacheOptions
        {
            InstanceName = null
        };
        _multiplexer.GetDatabase(noPrefixOptions.Database).Returns(_database);
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<bool>(),
            Arg.Any<When>(),
            Arg.Any<CommandFlags>())
            .Returns(true);

        var provider = new RedisCacheProvider(
            _multiplexer,
            Options.Create(noPrefixOptions),
            NullLogger<RedisCacheProvider>.Instance);

        await provider.SetWithTagsAsync("item:1", "data", ["my_tag"]);

        await _database.Received(1).SetAddAsync(
            Arg.Is<RedisKey>(k => k.ToString() == "tag:my_tag"),
            Arg.Is<RedisValue>(v => v.ToString() == "item:1"),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task ExistsAsync_WhenKeyExists_ReturnsSuccessTrue()
    {
        var provider = CreateProvider();
        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(true);

        var result = await provider.ExistsAsync("user:42");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_WhenKeyDoesNotExist_ReturnsSuccessFalse()
    {
        var provider = CreateProvider();
        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(false);

        var result = await provider.ExistsAsync("nonexistent:42");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public void AddRedisCaching_WithFactoryOverload_ResolvesSuccessfully()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddRedisCaching(_ => _multiplexer, opt => opt.InstanceName = "test:");

        using var sp = services.BuildServiceProvider();
        var cache = sp.GetService<ICacheProvider>();
        var taggedCache = sp.GetService<ITaggedCacheProvider>();

        cache.Should().NotBeNull();
        taggedCache.Should().NotBeNull();
    }

    [Fact]
    public async Task SetAsync_WhenOverwrittenWithDifferentTags_CleansUpPreviousTagAssociation()
    {
        var provider = CreateProvider();
        var fullKey = "test:item:1";
        var keyTagsKey = "test:item:1:__tags";

        // Previous tags: "old_tag"
        _database.SetMembersAsync(Arg.Is<RedisKey>(k => k.ToString() == keyTagsKey), Arg.Any<CommandFlags>())
            .Returns([new RedisValue("old_tag")]);

        // Overwrite with "new_tag"
        var result = await provider.SetAsync("item:1", "val", new CacheEntryOptions { Tags = ["new_tag"] });

        result.IsSuccess.Should().BeTrue();

        // Must remove item:1 from old_tag
        await _database.Received(1).SetRemoveAsync(
            Arg.Is<RedisKey>(k => k.ToString() == "test:tag:old_tag"),
            Arg.Is<RedisValue>(v => v.ToString() == fullKey),
            Arg.Any<CommandFlags>());

        // Must add item:1 to new_tag
        await _database.Received(1).SetAddAsync(
            Arg.Is<RedisKey>(k => k.ToString() == "test:tag:new_tag"),
            Arg.Is<RedisValue>(v => v.ToString() == fullKey),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task RemoveAsync_CleansUpReverseTagAssociation()
    {
        var provider = CreateProvider();
        var fullKey = "test:item:2";
        var keyTagsKey = "test:item:2:__tags";

        _database.SetMembersAsync(Arg.Is<RedisKey>(k => k.ToString() == keyTagsKey), Arg.Any<CommandFlags>())
            .Returns([new RedisValue("tag_x")]);
        _database.KeyDeleteAsync(Arg.Is<RedisKey>(k => k.ToString() == fullKey), Arg.Any<CommandFlags>())
            .Returns(true);

        var result = await provider.RemoveAsync("item:2");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();

        // Must remove fullKey from tag_x
        await _database.Received(1).SetRemoveAsync(
            Arg.Is<RedisKey>(k => k.ToString() == "test:tag:tag_x"),
            Arg.Is<RedisValue>(v => v.ToString() == fullKey),
            Arg.Any<CommandFlags>());

        // Must delete reverse tracker key
        await _database.Received(1).KeyDeleteAsync(
            Arg.Is<RedisKey>(k => k.ToString() == keyTagsKey),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task GetManyAsync_ReturnsDeserializedDictionary()
    {
        var provider = CreateProvider();
        _database.StringGetAsync(Arg.Any<RedisKey[]>(), Arg.Any<CommandFlags>())
            .Returns([new RedisValue("\"val1\""), RedisValue.Null, new RedisValue("\"val3\"")]);

        var result = await provider.GetManyAsync<string>(["k1", "k2", "k3"]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value["k1"].Should().Be("val1");
        result.Value["k3"].Should().Be("val3");
    }

    [Fact]
    public async Task RemoveManyAsync_DeletesKeysAndReturnsCount()
    {
        var provider = CreateProvider();
        _database.KeyDeleteAsync(Arg.Any<RedisKey[]>(), Arg.Any<CommandFlags>())
            .Returns(3);

        var result = await provider.RemoveManyAsync(["k1", "k2", "k3"]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(3);
    }

    [Fact]
    public async Task ExpireAsync_CallsKeyExpireAndReturnsSuccess()
    {
        var provider = CreateProvider();
        _database.KeyExpireAsync(Arg.Is<RedisKey>(k => k.ToString() == "test:my_key"), Arg.Any<TimeSpan?>(), Arg.Any<ExpireWhen>(), Arg.Any<CommandFlags>())
            .Returns(true);

        var result = await provider.ExpireAsync("my_key", TimeSpan.FromMinutes(10));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task RefreshAsync_WhenDefaultExpirationConfigured_RefreshesKey()
    {
        var provider = CreateProvider();
        _database.KeyExpireAsync(Arg.Is<RedisKey>(k => k.ToString() == "test:session:1"), Arg.Any<TimeSpan?>(), Arg.Any<ExpireWhen>(), Arg.Any<CommandFlags>())
            .Returns(true);

        var result = await provider.RefreshAsync("session:1");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }
}
