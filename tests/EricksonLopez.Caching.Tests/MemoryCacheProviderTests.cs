// Copyright © Erickson Lopez. MIT License.
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EricksonLopez.Caching.Tests;

public sealed class MemoryCacheProviderTests
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task GetAsync_NonExistentKey_ReturnsSuccessWithNull()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        var result = await provider.GetAsync<string>("missing_key");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_AndGetAsync_ReturnsCachedValue()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        await provider.SetAsync("user:100", "Erickson Lopez");
        var result = await provider.GetAsync<string>("user:100");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Erickson Lopez");
    }

    [Fact]
    public async Task AbsoluteExpiration_ExpiresAfterTtl()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        await provider.SetAsync("session:abc", "active", CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(10)));

        // Advance 5 minutes - still valid
        _timeProvider.Advance(TimeSpan.FromMinutes(5));
        var validResult = await provider.GetAsync<string>("session:abc");
        validResult.Value.Should().Be("active");

        // Advance another 6 minutes (total 11) - expired
        _timeProvider.Advance(TimeSpan.FromMinutes(6));
        var expiredResult = await provider.GetAsync<string>("session:abc");
        expiredResult.Value.Should().BeNull();
    }

    [Fact]
    public async Task SlidingExpiration_RefreshesOnAccess()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        await provider.SetAsync("token:123", "secret", CacheEntryOptions.FromSliding(TimeSpan.FromMinutes(5)));

        // Advance 4 minutes and access
        _timeProvider.Advance(TimeSpan.FromMinutes(4));
        var accessResult = await provider.GetAsync<string>("token:123");
        accessResult.Value.Should().Be("secret");

        // Advance another 4 minutes (8 minutes from start, but 4 min from last access) - still active
        _timeProvider.Advance(TimeSpan.FromMinutes(4));
        var stillActive = await provider.GetAsync<string>("token:123");
        stillActive.Value.Should().Be("secret");

        // Advance 6 minutes without access - expired
        _timeProvider.Advance(TimeSpan.FromMinutes(6));
        var expiredResult = await provider.GetAsync<string>("token:123");
        expiredResult.Value.Should().BeNull();
    }

    [Fact]
    public async Task GetOrCreateAsync_StampedeProtection_InvokesFactoryOnlyOnce()
    {
        var provider = new MemoryCacheProvider(_timeProvider);
        var factoryCallCount = 0;

        async Task<string> SlowFactory(CancellationToken ct)
        {
            Interlocked.Increment(ref factoryCallCount);
            await Task.Delay(50, ct);
            return "ExpensiveCalculatedValue";
        }

        // Launch 10 concurrent requests for the exact same key
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => provider.GetOrCreateAsync("expensive:key", SlowFactory))
            .ToList();

        var results = await Task.WhenAll(tasks);

        factoryCallCount.Should().Be(1);
        results.All(r => r.IsSuccess && r.Value == "ExpensiveCalculatedValue").Should().BeTrue();
    }

    [Fact]
    public async Task RemoveByPrefixAsync_RemovesAllMatchingKeys()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        await provider.SetAsync("tenant:1:users", "U1");
        await provider.SetAsync("tenant:1:orders", "O1");
        await provider.SetAsync("tenant:2:users", "U2");

        await provider.RemoveByPrefixAsync("tenant:1:");

        (await provider.GetAsync<string>("tenant:1:users")).Value.Should().BeNull();
        (await provider.GetAsync<string>("tenant:1:orders")).Value.Should().BeNull();
        (await provider.GetAsync<string>("tenant:2:users")).Value.Should().Be("U2");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoveByPrefixAsync_EmptyOrWhitespacePrefix_ThrowsArgumentException(string prefix)
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        var act = () => provider.RemoveByPrefixAsync(prefix);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SetWithTagsAsync_AndRemoveByTagAsync_RemovesTaggedEntriesOnly()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        await provider.SetWithTagsAsync("product:101", "Laptop", ["catalog", "electronics"]);
        await provider.SetWithTagsAsync("product:102", "Shirt", ["catalog", "apparel"]);
        await provider.SetAsync("user:42", "Alice");

        // Evict electronics
        var result = await provider.RemoveByTagAsync("electronics");
        result.IsSuccess.Should().BeTrue();

        (await provider.GetAsync<string>("product:101")).Value.Should().BeNull();
        (await provider.GetAsync<string>("product:102")).Value.Should().Be("Shirt");
        (await provider.GetAsync<string>("user:42")).Value.Should().Be("Alice");
    }

    [Fact]
    public async Task RemoveByTagsAsync_MultipleTags_RemovesAllAssociatedEntries()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        await provider.SetWithTagsAsync("key1", "val1", ["tagA"]);
        await provider.SetWithTagsAsync("key2", "val2", ["tagB"]);
        await provider.SetWithTagsAsync("key3", "val3", ["tagC"]);

        var result = await provider.RemoveByTagsAsync(["tagA", "tagB"]);
        result.IsSuccess.Should().BeTrue();

        (await provider.GetAsync<string>("key1")).Value.Should().BeNull();
        (await provider.GetAsync<string>("key2")).Value.Should().BeNull();
        (await provider.GetAsync<string>("key3")).Value.Should().Be("val3");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoveByTagAsync_EmptyOrWhitespaceTag_ThrowsArgumentException(string tag)
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        var act = () => provider.RemoveByTagAsync(tag);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenFactoryFails_AndStaleEntryWithinStaleWindow_ReturnsStaleValue()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        // Prime the cache with 5-minute TTL and 10-minute fail-safe stale tolerance
        var options = CacheEntryOptions.FromAbsoluteWithFailSafe(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10));
        await provider.SetAsync("weather:city", "Sunny 25C", options);

        // Advance 7 minutes (TTL expired, but within 10-min stale window)
        _timeProvider.Advance(TimeSpan.FromMinutes(7));

        // GetOrCreate with a factory that fails
        var result = await provider.GetOrCreateAsync<string>(
            "weather:city",
            _ => throw new InvalidOperationException("External weather service down"),
            options);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Sunny 25C");
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenFactoryFails_AndStaleWindowExceeded_PropagatesException()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        // Prime cache with 5-min TTL and 5-min stale tolerance (total 10 min window)
        var options = CacheEntryOptions.FromAbsoluteWithFailSafe(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        await provider.SetAsync("rate:usd", "1.0", options);

        // Advance 12 minutes (both TTL and stale tolerance exceeded)
        _timeProvider.Advance(TimeSpan.FromMinutes(12));

        var result = await provider.GetOrCreateAsync<string>(
            "rate:usd",
            _ => throw new InvalidOperationException("Exchange service down"),
            options);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cache.Memory.FactoryFailed");
    }

    [Fact]
    public async Task DefaultConstructor_UsesSystemTimeProvider()
    {
        var provider = new MemoryCacheProvider();

        await provider.SetAsync("sys:key", "val");
        var result = await provider.GetAsync<string>("sys:key");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("val");
    }

    [Theory]
    [InlineData(null)]
    public async Task GetAsync_NullKey_ThrowsArgumentNullException(string? key)
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        var act = () => provider.GetAsync<string>(key!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetOrCreateAsync_NullArguments_ThrowsArgumentNullException()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        var actKey = () => provider.GetOrCreateAsync<string>(null!, _ => Task.FromResult("val"));
        var actFactory = () => provider.GetOrCreateAsync<string>("key", null!);

        await actKey.Should().ThrowAsync<ArgumentNullException>();
        await actFactory.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SetAsync_NullKey_ThrowsArgumentNullException()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        var act = () => provider.SetAsync(null!, "val");

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SetWithTagsAsync_NullArguments_ThrowsArgumentNullException()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        var actKey = () => provider.SetWithTagsAsync(null!, "val", ["tag"]);
        var actTags = () => provider.SetWithTagsAsync("key", "val", null!);

        await actKey.Should().ThrowAsync<ArgumentNullException>();
        await actTags.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RemoveAsync_NullKey_ThrowsArgumentNullException()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        var act = () => provider.RemoveAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    public async Task RemoveByPrefixAsync_NullPrefix_ThrowsArgumentException(string? prefix)
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        var act = () => provider.RemoveByPrefixAsync(prefix!);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    public async Task RemoveByTagAsync_NullTag_ThrowsArgumentException(string? tag)
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        var act = () => provider.RemoveByTagAsync(tag!);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RemoveByTagsAsync_NullTags_ThrowsArgumentNullException()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        var act = () => provider.RemoveByTagsAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetAsync_TypeMismatch_ReturnsNullMiss()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        await provider.SetAsync("number_or_string", "not_an_int");

        var result = await provider.GetAsync<int?>("number_or_string");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ExpiredEntryWithoutStaleTolerance_EvictsFromEntries()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        // 5-min TTL with NO fail-safe max stale
        await provider.SetAsync("exp:item", "temporary", CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(5)));

        // Advance 10 minutes (expired beyond any tolerance)
        _timeProvider.Advance(TimeSpan.FromMinutes(10));

        var result1 = await provider.GetAsync<string>("exp:item");
        var result2 = await provider.GetAsync<string>("exp:item");

        result1.Value.Should().BeNull();
        result2.Value.Should().BeNull();
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenFactoryThrowsOperationCanceledException_RethrowsImmediately()
    {
        var provider = new MemoryCacheProvider(_timeProvider);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => provider.GetOrCreateAsync<string>(
            "cancel_key",
            ct => throw new OperationCanceledException(ct),
            null,
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenStaleEntryTypeMismatches_PropagatesException()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        // Prime cache with a string value and fail-safe stale tolerance
        var options = CacheEntryOptions.FromAbsoluteWithFailSafe(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10));
        await provider.SetAsync("type:stale", "hello", options);

        _timeProvider.Advance(TimeSpan.FromMinutes(7));

        // Attempt to GetOrCreate as int
        var result = await provider.GetOrCreateAsync<int>(
            "type:stale",
            _ => throw new InvalidOperationException("Failed"),
            options);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cache.Memory.FactoryFailed");
    }

    [Fact]
    public async Task RemoveAsync_WhenKeyDoesNotExist_ReturnsSuccessFalse()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        var result = await provider.RemoveAsync("nonexistent_key");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveByTagAsync_WhenTagDoesNotExist_ReturnsSuccessFalse()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        var result = await provider.RemoveByTagAsync("nonexistent_tag");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveByPrefixAsync_WhenNoKeysMatch_ReturnsSuccessTrue()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        var result = await provider.RemoveByPrefixAsync("nomatch:");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task SetWithTagsAsync_WithWhitespaceTags_FiltersOutWhitespaceTags()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        await provider.SetWithTagsAsync("item:test", "val", ["valid_tag", "", "   "]);

        var evictResult = await provider.RemoveByTagAsync("valid_tag");
        evictResult.IsSuccess.Should().BeTrue();
        evictResult.Value.Should().BeTrue();

        var getResult = await provider.GetAsync<string>("item:test");
        getResult.Value.Should().BeNull();
    }

    [Fact]
    public async Task SetWithTagsAsync_WithNullOptions_InitializesOptionsAndSucceeds()
    {
        var provider = new MemoryCacheProvider(_timeProvider);
        var result = await provider.SetWithTagsAsync("key:nullopt", "val", ["tag1"], options: null);
        result.IsSuccess.Should().BeTrue();
        (await provider.GetAsync<string>("key:nullopt")).Value.Should().Be("val");
    }

    [Fact]
    public async Task SetWithTagsAsync_WithExplicitOptions_PreservesOptionsAndAddsTags()
    {
        var provider = new MemoryCacheProvider(_timeProvider);
        var options = CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(15));
        var result = await provider.SetWithTagsAsync("key:opt", "val", ["tag1"], options);
        result.IsSuccess.Should().BeTrue();
        (await provider.GetAsync<string>("key:opt")).Value.Should().Be("val");
    }

    [Fact]
    public async Task RemoveByTagsAsync_SkipsWhitespaceTags()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        await provider.SetWithTagsAsync("tag_item", "data", ["tag_alpha"]);

        var result = await provider.RemoveByTagsAsync(["", "   ", "tag_alpha"]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();

        (await provider.GetAsync<string>("tag_item")).Value.Should().BeNull();
    }

    [Fact]
    public async Task FailSafe_WithSlidingExpirationOnly_CalculatesStaleWindowFromLastAccessed()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        // Entry with 5 min sliding, 10 min fail-safe stale, no absolute
        var options = new CacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(5),
            FailSafeMaxStale = TimeSpan.FromMinutes(10)
        };
        await provider.SetAsync("sliding_stale", "cached_data", options);

        // Advance 7 minutes (expired sliding TTL of 5m, but within 10m stale window)
        _timeProvider.Advance(TimeSpan.FromMinutes(7));

        var result = await provider.GetOrCreateAsync<string>(
            "sliding_stale",
            _ => throw new InvalidOperationException("Upstream failed"),
            options);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("cached_data");
    }

    [Fact]
    public async Task FailSafe_WithNeitherAbsoluteNorSliding_CalculatesStaleWindowFromCreatedAt()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        // Entry with no expiration but failSafeMaxStale
        var options = new CacheEntryOptions
        {
            FailSafeMaxStale = TimeSpan.FromMinutes(10)
        };
        await provider.SetAsync("created_stale", "initial", options);

        // Advance 5 minutes
        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        var result = await provider.GetOrCreateAsync<string>(
            "created_stale",
            _ => throw new InvalidOperationException("Failed"),
            options);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("initial");
    }

    [Fact]
    public async Task RemoveExpiredEntries_ActivelyEvictsExpiredItemsFromDictionary()
    {
        // Arrange
        var provider = new MemoryCacheProvider(_timeProvider);
        var options = CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(5));

        await provider.SetAsync("exp:1", "val1", options);
        await provider.SetAsync("exp:2", "val2", options);
        await provider.SetAsync("permanent", "val3"); // No expiration

        // Advance 6 minutes so exp:1 and exp:2 expire
        _timeProvider.Advance(TimeSpan.FromMinutes(6));

        // Act - deterministic compaction sweep
        var evicted = provider.RemoveExpiredEntries();

        // Assert
        evicted.Should().Be(2, "two expired entries must be evicted during sweep");

        // Verify permanent is still present
        var permResult = await provider.GetAsync<string>("permanent");
        permResult.Value.Should().Be("val3");

        // Calling sweep again should find 0 expired entries
        provider.RemoveExpiredEntries().Should().Be(0);
    }

    [Fact]
    public async Task ExistsAsync_WhenKeyExistsAndNotExpired_ReturnsSuccessTrue()
    {
        var provider = new MemoryCacheProvider(_timeProvider);
        await provider.SetAsync("active:key", "value", CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(10)));

        var exists = await provider.ExistsAsync("active:key");

        exists.IsSuccess.Should().BeTrue();
        exists.Value.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_WhenKeyDoesNotExist_ReturnsSuccessFalse()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        var exists = await provider.ExistsAsync("nonexistent:key");

        exists.IsSuccess.Should().BeTrue();
        exists.Value.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_WhenKeyExpired_ReturnsSuccessFalse()
    {
        var timeProvider = new FakeTimeProvider();
        var provider = new MemoryCacheProvider(timeProvider: timeProvider);
        await provider.SetAsync("expiring:key", "val", CacheEntryOptions.FromAbsolute(TimeSpan.FromSeconds(5)));

        timeProvider.Advance(TimeSpan.FromSeconds(10));

        var exists = await provider.ExistsAsync("expiring:key");

        exists.IsSuccess.Should().BeTrue();
        exists.Value.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_WhenKeyIsNull_ThrowsArgumentNullException()
    {
        var provider = new MemoryCacheProvider(_timeProvider);

        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.ExistsAsync(null!));
    }
}
