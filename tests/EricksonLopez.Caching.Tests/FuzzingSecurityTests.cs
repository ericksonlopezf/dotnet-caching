// Copyright © Erickson Lopez. MIT License.
using System;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EricksonLopez.Caching.Tests;

/// <summary>
/// Fuzz testing and adversarial input suite for the caching layer.
/// Validates behavior against hostile keys, massive payloads, and boundary durations.
/// </summary>
public sealed class FuzzingSecurityTests
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData("\0\0\0null_byte_key")]
    [InlineData("key\r\nwith\r\nnewlines")]
    [InlineData("key\twith\ttabs")]
    [InlineData("key_with_emojis_🔥🔥🔥_🚀_✨")]
    [InlineData("../../../../../etc/passwd")]
    [InlineData("C:\\Windows\\System32\\config\\SAM")]
    [InlineData("'; DROP TABLE cache_entries; --")]
    [InlineData("EVAL \"return redis.call('FLUSHALL')\" 0")]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("{\"json\":\"injection\",\"val\":42}")]
    public async Task FuzzKeys_HostileCharacters_DoesNotCorruptOrCrash(string hostileKey)
    {
        var provider = new MemoryCacheProvider(_timeProvider);
        const string payload = "secure_content";

        var setRes = await provider.SetAsync(hostileKey, payload);
        setRes.IsSuccess.Should().BeTrue();

        var getRes = await provider.GetAsync<string>(hostileKey);
        getRes.IsSuccess.Should().BeTrue();
        getRes.Value.Should().Be(payload);

        var existsRes = await provider.ExistsAsync(hostileKey);
        existsRes.Value.Should().BeTrue();

        var removeRes = await provider.RemoveAsync(hostileKey);
        removeRes.Value.Should().BeTrue();
    }

    [Fact]
    public async Task FuzzKeys_Massive10KKey_SucceedsWithoutMemoryCorruption()
    {
        var provider = new MemoryCacheProvider(_timeProvider);
        var hugeKey = new string('x', 10_000);
        const string payload = "huge_key_payload";

        var setRes = await provider.SetAsync(hugeKey, payload);
        setRes.IsSuccess.Should().BeTrue();

        var getRes = await provider.GetAsync<string>(hugeKey);
        getRes.Value.Should().Be(payload);

        var removeRes = await provider.RemoveAsync(hugeKey);
        removeRes.Value.Should().BeTrue();
    }

    [Fact]
    public async Task FuzzPayloads_Large1MBString_StoresAndRetrievesIntact()
    {
        var provider = new MemoryCacheProvider(_timeProvider);
        var largePayload = new string('A', 1024 * 1024); // 1 MB

        var setRes = await provider.SetAsync("large:payload:key", largePayload);
        setRes.IsSuccess.Should().BeTrue();

        var getRes = await provider.GetAsync<string>("large:payload:key");
        getRes.IsSuccess.Should().BeTrue();
        getRes.Value!.Length.Should().Be(1024 * 1024);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1000)]
    public void FuzzTTL_ZeroOrNegative_ThrowsArgumentOutOfRangeException(int milliseconds)
    {
        var invalidSpan = TimeSpan.FromMilliseconds(milliseconds);

        var act1 = () => new CacheEntryOptions { AbsoluteExpirationRelativeToNow = invalidSpan };
        act1.Should().Throw<ArgumentOutOfRangeException>();

        var act2 = () => new CacheEntryOptions { SlidingExpiration = invalidSpan };
        act2.Should().Throw<ArgumentOutOfRangeException>();

        var act3 = () => new CacheEntryOptions { FailSafeMaxStale = invalidSpan };
        act3.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void FuzzTTL_ExceedingMaxAllowedDuration_ThrowsArgumentOutOfRangeException()
    {
        var exceedingSpan = CacheEntryOptions.MaxAllowedDuration + TimeSpan.FromDays(1);

        var act1 = () => new CacheEntryOptions { AbsoluteExpirationRelativeToNow = exceedingSpan };
        act1.Should().Throw<ArgumentOutOfRangeException>();

        var act2 = () => new CacheEntryOptions { SlidingExpiration = exceedingSpan };
        act2.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void FuzzTTL_MaxAllowedDuration_SucceedsAtBoundary()
    {
        var boundarySpan = CacheEntryOptions.MaxAllowedDuration;
        var options = new CacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = boundarySpan,
            SlidingExpiration = boundarySpan,
            FailSafeMaxStale = boundarySpan
        };

        options.AbsoluteExpirationRelativeToNow.Should().Be(boundarySpan);
        options.SlidingExpiration.Should().Be(boundarySpan);
        options.FailSafeMaxStale.Should().Be(boundarySpan);
    }
}
