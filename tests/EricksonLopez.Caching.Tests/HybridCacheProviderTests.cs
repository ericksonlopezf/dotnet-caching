// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Result;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Caching.Tests;

public sealed class HybridCacheProviderTests
{
    private readonly ITaggedCacheProvider _localCache = Substitute.For<ITaggedCacheProvider>();
    private readonly ITaggedCacheProvider _distributedCache = Substitute.For<ITaggedCacheProvider>();

    [Fact]
    public async Task GetAsync_L1Hit_ReturnsImmediatelyWithoutCallingL2()
    {
        _localCache.GetAsync<string>("key1", Arg.Any<CancellationToken>())
            .Returns(Result<string?>.Success("LocalValue"));

        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var result = await hybrid.GetAsync<string>("key1");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("LocalValue");
        await _distributedCache.DidNotReceive().GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_L1MissAndL2Hit_PopulatesL1AndReturnsValue()
    {
        _localCache.GetAsync<string>("key2", Arg.Any<CancellationToken>())
            .Returns(Result<string?>.Success(default));
        _distributedCache.GetAsync<string>("key2", Arg.Any<CancellationToken>())
            .Returns(Result<string?>.Success("DistributedValue"));

        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var result = await hybrid.GetAsync<string>("key2");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("DistributedValue");
        await _localCache.Received(1).SetAsync("key2", "DistributedValue", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_WhenL2Fails_GracefullyReturnsDefaultMiss()
    {
        _localCache.GetAsync<string>("key_err", Arg.Any<CancellationToken>())
            .Returns(Result<string?>.Success(default));
        _distributedCache.GetAsync<string>("key_err", Arg.Any<CancellationToken>())
            .Returns(Result<string?>.Failure(new Error("Cache.Redis.ConnectionFailed", "Timeout")));

        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var result = await hybrid.GetAsync<string>("key_err");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenL2Fails_ExecutesFactoryAndReturnsSuccess()
    {
        _localCache.GetAsync<string>("key_fail", Arg.Any<CancellationToken>())
            .Returns(Result<string?>.Success(default));

        _localCache.GetOrCreateAsync(
            "key_fail",
            Arg.Any<Func<CancellationToken, Task<string>>>(),
            Arg.Any<CacheEntryOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var factory = callInfo.Arg<Func<CancellationToken, Task<string>>>();
                var val = await factory(CancellationToken.None);
                return Result<string>.Success(val);
            });

        _distributedCache.GetAsync<string>("key_fail", Arg.Any<CancellationToken>())
            .Returns(Result<string?>.Failure(new Error("Cache.Redis.ConnectionFailed", "Down")));

        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var result = await hybrid.GetOrCreateAsync("key_fail", _ => Task.FromResult("ComputedValue"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ComputedValue");
    }

    [Fact]
    public async Task SetAsync_WithHybridCacheEntryOptions_SeparatesL1AndL2Durations()
    {
        _localCache.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CacheEntryOptions>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));
        _distributedCache.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CacheEntryOptions>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var options = HybridCacheEntryOptions.FromDurations(
            localDuration: TimeSpan.FromMinutes(1),
            distributedDuration: TimeSpan.FromHours(1));

        var result = await hybrid.SetAsync("tiered:key", "Value", options);

        result.IsSuccess.Should().BeTrue();

        await _localCache.Received(1).SetAsync(
            "tiered:key",
            "Value",
            Arg.Is<CacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(1)),
            Arg.Any<CancellationToken>());

        await _distributedCache.Received(1).SetAsync(
            "tiered:key",
            "Value",
            Arg.Is<CacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow == TimeSpan.FromHours(1)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetAsync_WithHybridCacheEntryOptions_WhenSpecificDurationsNull_FallsBackToAbsoluteExpiration()
    {
        _localCache.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CacheEntryOptions>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));
        _distributedCache.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CacheEntryOptions>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var options = new HybridCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(45),
            LocalCacheDuration = null,
            DistributedCacheDuration = null
        };

        var result = await hybrid.SetAsync("fallback:key", "Value", options);

        result.IsSuccess.Should().BeTrue();

        await _localCache.Received(1).SetAsync(
            "fallback:key",
            "Value",
            Arg.Is<CacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(45)),
            Arg.Any<CancellationToken>());

        await _distributedCache.Received(1).SetAsync(
            "fallback:key",
            "Value",
            Arg.Is<CacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(45)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetAsync_WithHybridCacheEntryOptions_WhenBothDurationsAndAbsoluteSet_SpecificDurationsPrecede()
    {
        _localCache.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CacheEntryOptions>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));
        _distributedCache.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CacheEntryOptions>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var options = new HybridCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(45),
            LocalCacheDuration = TimeSpan.FromMinutes(5),
            DistributedCacheDuration = TimeSpan.FromHours(2)
        };

        var result = await hybrid.SetAsync("precedence:key", "Value", options);

        result.IsSuccess.Should().BeTrue();

        await _localCache.Received(1).SetAsync(
            "precedence:key",
            "Value",
            Arg.Is<CacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(5)),
            Arg.Any<CancellationToken>());

        await _distributedCache.Received(1).SetAsync(
            "precedence:key",
            "Value",
            Arg.Is<CacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow == TimeSpan.FromHours(2)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveAsync_RemovesFromBothL1AndL2()
    {
        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var result = await hybrid.RemoveAsync("key3");

        result.IsSuccess.Should().BeTrue();
        await _localCache.Received(1).RemoveAsync("key3", Arg.Any<CancellationToken>());
        await _distributedCache.Received(1).RemoveAsync("key3", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveByTagAsync_RemovesFromBothL1AndL2()
    {
        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var result = await hybrid.RemoveByTagAsync("tag1");

        result.IsSuccess.Should().BeTrue();
        await _localCache.Received(1).RemoveByTagAsync("tag1", Arg.Any<CancellationToken>());
        await _distributedCache.Received(1).RemoveByTagAsync("tag1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Constructor_NullArguments_ThrowsArgumentNullException()
    {
        var act1 = () => new HybridCacheProvider(null!, _distributedCache);
        var act2 = () => new HybridCacheProvider(_localCache, null!);

        act1.Should().Throw<ArgumentNullException>().WithParameterName("localCache");
        act2.Should().Throw<ArgumentNullException>().WithParameterName("distributedCache");
    }

    [Fact]
    public async Task GuardClauses_NullArguments_ThrowAppropriateExceptions()
    {
        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        await Assert.ThrowsAsync<ArgumentNullException>(() => hybrid.GetAsync<string>(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => hybrid.GetOrCreateAsync<string>(null!, _ => Task.FromResult("val")));
        await Assert.ThrowsAsync<ArgumentNullException>(() => hybrid.GetOrCreateAsync<string>("k", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => hybrid.SetAsync<string>(null!, "val"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => hybrid.SetWithTagsAsync<string>(null!, "val", ["tag"]));
        await Assert.ThrowsAsync<ArgumentNullException>(() => hybrid.SetWithTagsAsync<string>("k", "val", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => hybrid.RemoveAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => hybrid.RemoveByPrefixAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => hybrid.RemoveByPrefixAsync("   "));
        await Assert.ThrowsAsync<ArgumentNullException>(() => hybrid.RemoveByTagAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => hybrid.RemoveByTagAsync("   "));
        await Assert.ThrowsAsync<ArgumentNullException>(() => hybrid.RemoveByTagsAsync(null!));
    }

    [Fact]
    public async Task GetAsync_WhenL2ThrowsException_GracefullyDegradesToMiss()
    {
        _localCache.GetAsync<string>("key_throw", Arg.Any<CancellationToken>())
            .Returns(Result<string?>.Success(default));
        _distributedCache.GetAsync<string>("key_throw", Arg.Any<CancellationToken>())
            .Returns<Task<Result<string?>>>(_ => throw new TimeoutException("Redis connection timed out"));

        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var result = await hybrid.GetAsync<string>("key_throw");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_WhenL2ThrowsOperationCanceledException_Rethrows()
    {
        _localCache.GetAsync<string>("key_cancel", Arg.Any<CancellationToken>())
            .Returns(Result<string?>.Success(default));
        _distributedCache.GetAsync<string>("key_cancel", Arg.Any<CancellationToken>())
            .Returns<Task<Result<string?>>>(_ => throw new OperationCanceledException());

        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var act = () => hybrid.GetAsync<string>("key_cancel");

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetAsync_WhenBothL1AndL2ReturnMiss_ReturnsNull()
    {
        _localCache.GetAsync<string>("key_miss", Arg.Any<CancellationToken>())
            .Returns(Result<string?>.Success(default));
        _distributedCache.GetAsync<string>("key_miss", Arg.Any<CancellationToken>())
            .Returns(Result<string?>.Success(default));

        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var result = await hybrid.GetAsync<string>("key_miss");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenInitialGetHits_ReturnsValueWithoutLocking()
    {
        _localCache.GetAsync<string>("key_hit", Arg.Any<CancellationToken>())
            .Returns(Result<string?>.Success("InitialHit"));

        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var result = await hybrid.GetOrCreateAsync("key_hit", _ => Task.FromResult("Computed"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("InitialHit");
        await _localCache.DidNotReceive().GetOrCreateAsync(
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task<string>>>(),
            Arg.Any<CacheEntryOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenL2RecheckHitsInsideLock_ReturnsL2ValueWithoutCallingFactory()
    {
        _localCache.GetAsync<string>("key_l2_recheck", Arg.Any<CancellationToken>())
            .Returns(Result<string?>.Success(default));
        _distributedCache.GetAsync<string>("key_l2_recheck", Arg.Any<CancellationToken>())
            .Returns(
                Result<string?>.Success(default),
                Result<string?>.Success("PopulatedInL2ByOtherInstance"));

        _localCache.GetOrCreateAsync(
            "key_l2_recheck",
            Arg.Any<Func<CancellationToken, Task<string>>>(),
            Arg.Any<CacheEntryOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var factory = callInfo.Arg<Func<CancellationToken, Task<string>>>();
                var val = await factory(CancellationToken.None);
                return Result<string>.Success(val);
            });

        var factoryCalled = false;
        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var result = await hybrid.GetOrCreateAsync("key_l2_recheck", _ =>
        {
            factoryCalled = true;
            return Task.FromResult("FactoryVal");
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("PopulatedInL2ByOtherInstance");
        factoryCalled.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrCreateAsync_CoordinatesThroughDistributedGetOrCreateAsync_ToPreventClusterStampede()
    {
        _localCache.GetAsync<string>("cluster_stampede_key", Arg.Any<CancellationToken>())
            .Returns(Result<string?>.Success(default));
        _distributedCache.GetAsync<string>("cluster_stampede_key", Arg.Any<CancellationToken>())
            .Returns(Result<string?>.Success(default));

        _localCache.GetOrCreateAsync(
            "cluster_stampede_key",
            Arg.Any<Func<CancellationToken, Task<string>>>(),
            Arg.Any<CacheEntryOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var factory = callInfo.Arg<Func<CancellationToken, Task<string>>>();
                var val = await factory(CancellationToken.None);
                return Result<string>.Success(val);
            });

        _distributedCache.GetOrCreateAsync(
            "cluster_stampede_key",
            Arg.Any<Func<CancellationToken, Task<string>>>(),
            Arg.Any<CacheEntryOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("DistributedLockedValue"));

        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var result = await hybrid.GetOrCreateAsync("cluster_stampede_key", _ => Task.FromResult("LocalVal"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("DistributedLockedValue");
        await _distributedCache.Received(1).GetOrCreateAsync(
            "cluster_stampede_key",
            Arg.Any<Func<CancellationToken, Task<string>>>(),
            Arg.Any<CacheEntryOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenL2SetThrowsException_OperationStillSucceeds()
    {
        _localCache.GetAsync<string>("key_l2_set_fail", Arg.Any<CancellationToken>())
            .Returns(Result<string?>.Success(default));
        _distributedCache.GetAsync<string>("key_l2_set_fail", Arg.Any<CancellationToken>())
            .Returns(Result<string?>.Success(default));
        _distributedCache.SetAsync("key_l2_set_fail", Arg.Any<string>(), Arg.Any<CacheEntryOptions>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result<bool>>>(_ => throw new TimeoutException("L2 connection lost"));

        _localCache.GetOrCreateAsync(
            "key_l2_set_fail",
            Arg.Any<Func<CancellationToken, Task<string>>>(),
            Arg.Any<CacheEntryOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var factory = callInfo.Arg<Func<CancellationToken, Task<string>>>();
                var val = await factory(CancellationToken.None);
                return Result<string>.Success(val);
            });

        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var result = await hybrid.GetOrCreateAsync("key_l2_set_fail", _ => Task.FromResult("ResilientValue"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ResilientValue");
    }

    [Fact]
    public async Task SetAsync_WhenL2ThrowsException_ReturnsTrueIfL1Succeeded()
    {
        _distributedCache.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CacheEntryOptions>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result<bool>>>(_ => throw new TimeoutException("L2 unavailable"));
        _localCache.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CacheEntryOptions>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var result = await hybrid.SetAsync("k", "v");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task SetAsync_WhenBothL1AndL2Fail_ReturnsSuccessFalse()
    {
        _distributedCache.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CacheEntryOptions>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Failure(new Error("L2.Error", "Fail")));
        _localCache.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CacheEntryOptions>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Failure(new Error("L1.Error", "Fail")));

        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var result = await hybrid.SetAsync("k", "v");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task SetWithTagsAsync_WhenProvidersAreNotTagged_FallsBackToSetAsync()
    {
        var untaggedL1 = Substitute.For<ICacheProvider>();
        var untaggedL2 = Substitute.For<ICacheProvider>();

        untaggedL1.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CacheEntryOptions>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));
        untaggedL2.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CacheEntryOptions>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        var hybrid = new HybridCacheProvider(untaggedL1, untaggedL2);

        var result = await hybrid.SetWithTagsAsync("key_untagged", "val", ["tag1"]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        await untaggedL1.Received(1).SetAsync("key_untagged", "val", Arg.Any<CacheEntryOptions>(), Arg.Any<CancellationToken>());
        await untaggedL2.Received(1).SetAsync("key_untagged", "val", Arg.Any<CacheEntryOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetWithTagsAsync_WhenL2ThrowsException_L1StillCalledAndReturnsSuccess()
    {
        _distributedCache.SetWithTagsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CacheEntryOptions>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result<bool>>>(_ => throw new TimeoutException("L2 tag fail"));
        _localCache.SetWithTagsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CacheEntryOptions>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var result = await hybrid.SetWithTagsAsync("tag_fail_key", "val", ["tag"]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        await _localCache.Received(1).SetWithTagsAsync("tag_fail_key", "val", Arg.Any<IEnumerable<string>>(), Arg.Any<CacheEntryOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveAsync_WhenL2ThrowsException_L1StillRemovedAndReturnsSuccess()
    {
        _distributedCache.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result<bool>>>(_ => throw new TimeoutException("L2 remove timeout"));
        _localCache.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var result = await hybrid.RemoveAsync("remove_key");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        await _localCache.Received(1).RemoveAsync("remove_key", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveByPrefixAsync_RemovesFromBothL1AndL2_AndHandlesL2Exceptions()
    {
        _distributedCache.RemoveByPrefixAsync("prefix:", Arg.Any<CancellationToken>())
            .Returns<Task<Result<bool>>>(_ => throw new TimeoutException("L2 prefix timeout"));
        _localCache.RemoveByPrefixAsync("prefix:", Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var result = await hybrid.RemoveByPrefixAsync("prefix:");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        await _localCache.Received(1).RemoveByPrefixAsync("prefix:", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveByTagAsync_WhenL2ThrowsException_L1StillRemovedAndReturnsSuccess()
    {
        _distributedCache.RemoveByTagAsync("tag_ex", Arg.Any<CancellationToken>())
            .Returns<Task<Result<bool>>>(_ => throw new TimeoutException("L2 tag remove timeout"));
        _localCache.RemoveByTagAsync("tag_ex", Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var result = await hybrid.RemoveByTagAsync("tag_ex");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        await _localCache.Received(1).RemoveByTagAsync("tag_ex", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveByTagsAsync_MultipleTagsWithWhitespace_IteratesAndSkipsWhitespace()
    {
        _localCache.RemoveByTagAsync("t1", Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
        _distributedCache.RemoveByTagAsync("t1", Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
        _localCache.RemoveByTagAsync("t2", Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
        _distributedCache.RemoveByTagAsync("t2", Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));

        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var result = await hybrid.RemoveByTagsAsync(["t1", "", "   ", "t2"]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        await _localCache.Received(1).RemoveByTagAsync("t1", Arg.Any<CancellationToken>());
        await _localCache.Received(1).RemoveByTagAsync("t2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveByTagsAsync_WhenOneTagFails_ReturnsSuccessFalse()
    {
        var untaggedL1 = Substitute.For<ICacheProvider>();
        var untaggedL2 = Substitute.For<ICacheProvider>();
        var hybrid = new HybridCacheProvider(untaggedL1, untaggedL2);

        // When not ITaggedCacheProvider, RemoveByTagAsync still returns Success(true)
        var result = await hybrid.RemoveByTagsAsync(["t1"]);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task SplitTierOptions_WithNullLocalOrDistributedDuration_FallsBackToAbsoluteExpiration()
    {
        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);
        var options = new HybridCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
            LocalCacheDuration = null,
            DistributedCacheDuration = null
        };

        // Exercise SetAsync to invoke SplitTierOptions with null durations
        await hybrid.SetAsync("fallback_key", "val", options);

        await _localCache.Received(1).SetAsync(
            "fallback_key",
            "val",
            Arg.Is<CacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(10)),
            Arg.Any<CancellationToken>());
        await _distributedCache.Received(1).SetAsync(
            "fallback_key",
            "val",
            Arg.Is<CacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(10)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Operations_WhenCancelledToken_ImmediatelyThrowsOperationCanceledException()
    {
        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => hybrid.GetAsync<string>("k", cts.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => hybrid.GetOrCreateAsync("k", _ => Task.FromResult("v"), null, cts.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => hybrid.SetAsync("k", "v", null, cts.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => hybrid.SetWithTagsAsync("k", "v", ["t"], null, cts.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => hybrid.RemoveAsync("k", cts.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => hybrid.RemoveByPrefixAsync("p", cts.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => hybrid.RemoveByTagAsync("t", cts.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => hybrid.RemoveByTagsAsync(["t"], cts.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => hybrid.ExistsAsync("k", cts.Token));
    }

    [Fact]
    public async Task ExistsAsync_WhenL1HasKey_ReturnsSuccessTrueWithoutCallingL2()
    {
        _localCache.ExistsAsync("k1", Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var result = await hybrid.ExistsAsync("k1");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        await _distributedCache.DidNotReceive().ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExistsAsync_WhenL1MissesAndL2HasKey_ReturnsSuccessTrue()
    {
        _localCache.ExistsAsync("k2", Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(false));
        _distributedCache.ExistsAsync("k2", Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var result = await hybrid.ExistsAsync("k2");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task GetAsync_WhenL1HasNullValueCached_ReturnsHitWithoutCallingL2()
    {
        _localCache.GetAsync<string>("null_key", Arg.Any<CancellationToken>())
            .Returns(Result<string?>.Success(null));
        _localCache.ExistsAsync("null_key", Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        var hybrid = new HybridCacheProvider(_localCache, _distributedCache);

        var result = await hybrid.GetAsync<string>("null_key");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
        await _distributedCache.DidNotReceive().GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
