// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.Caching.Tests;

public sealed class CacheEntryOptionsTests
{
    [Fact]
    public void FromAbsolute_SetsAbsoluteExpiration_AndLeavesOtherPropertiesNull()
    {
        var duration = TimeSpan.FromMinutes(15);

        var options = CacheEntryOptions.FromAbsolute(duration);

        options.AbsoluteExpirationRelativeToNow.Should().Be(duration);
        options.SlidingExpiration.Should().BeNull();
        options.FailSafeMaxStale.Should().BeNull();
        options.Tags.Should().BeNull();
    }

    [Fact]
    public void FromSliding_SetsSlidingExpiration_AndLeavesOtherPropertiesNull()
    {
        var duration = TimeSpan.FromMinutes(30);

        var options = CacheEntryOptions.FromSliding(duration);

        options.SlidingExpiration.Should().Be(duration);
        options.AbsoluteExpirationRelativeToNow.Should().BeNull();
        options.FailSafeMaxStale.Should().BeNull();
        options.Tags.Should().BeNull();
    }

    [Fact]
    public void FromAbsoluteWithFailSafe_SetsAbsoluteAndMaxStale_Correctly()
    {
        var ttl = TimeSpan.FromMinutes(5);
        var maxStale = TimeSpan.FromMinutes(20);

        var options = CacheEntryOptions.FromAbsoluteWithFailSafe(ttl, maxStale);

        options.AbsoluteExpirationRelativeToNow.Should().Be(ttl);
        options.FailSafeMaxStale.Should().Be(maxStale);
        options.SlidingExpiration.Should().BeNull();
        options.Tags.Should().BeNull();
    }

    [Fact]
    public void Properties_CanBeAssignedAndRetrievedDirectly()
    {
        var tags = new List<string> { "tag1", "tag2" };
        var options = new CacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10),
            SlidingExpiration = TimeSpan.FromSeconds(20),
            FailSafeMaxStale = TimeSpan.FromSeconds(30),
            Tags = tags
        };

        options.AbsoluteExpirationRelativeToNow.Should().Be(TimeSpan.FromSeconds(10));
        options.SlidingExpiration.Should().Be(TimeSpan.FromSeconds(20));
        options.FailSafeMaxStale.Should().Be(TimeSpan.FromSeconds(30));
        options.Tags.Should().BeSameAs(tags);
    }

    [Fact]
    public void HybridCacheEntryOptions_FromDurations_ConfiguresTieredDurationsAndAbsoluteExpiration()
    {
        var local = TimeSpan.FromSeconds(30);
        var distributed = TimeSpan.FromHours(2);

        var options = HybridCacheEntryOptions.FromDurations(local, distributed);

        options.LocalCacheDuration.Should().Be(local);
        options.DistributedCacheDuration.Should().Be(distributed);
        options.AbsoluteExpirationRelativeToNow.Should().Be(distributed);
        options.SlidingExpiration.Should().BeNull();
        options.FailSafeMaxStale.Should().BeNull();
        options.Tags.Should().BeNull();
    }

    [Fact]
    public void HybridCacheEntryOptions_PropertiesCanBeAssignedDirectly()
    {
        var tags = new[] { "catalog", "tier1" };
        var options = new HybridCacheEntryOptions
        {
            LocalCacheDuration = TimeSpan.FromMinutes(1),
            DistributedCacheDuration = TimeSpan.FromMinutes(10),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
            SlidingExpiration = TimeSpan.FromMinutes(2),
            FailSafeMaxStale = TimeSpan.FromMinutes(5),
            Tags = tags
        };

        options.LocalCacheDuration.Should().Be(TimeSpan.FromMinutes(1));
        options.DistributedCacheDuration.Should().Be(TimeSpan.FromMinutes(10));
        options.AbsoluteExpirationRelativeToNow.Should().Be(TimeSpan.FromMinutes(10));
        options.SlidingExpiration.Should().Be(TimeSpan.FromMinutes(2));
        options.FailSafeMaxStale.Should().Be(TimeSpan.FromMinutes(5));
        options.Tags.Should().BeSameAs(tags);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1000)]
    public void AbsoluteExpiration_WhenZeroOrNegative_ThrowsArgumentOutOfRangeException(int seconds)
    {
        var options = new CacheEntryOptions();
        var act = () => options.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(seconds);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AbsoluteExpiration_WhenExceedsMaxAllowedDuration_ThrowsArgumentOutOfRangeException()
    {
        var options = new CacheEntryOptions();
        var act = () => options.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(4000);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void SlidingExpiration_WhenZeroOrNegative_ThrowsArgumentOutOfRangeException(int seconds)
    {
        var options = new CacheEntryOptions();
        var act = () => options.SlidingExpiration = TimeSpan.FromSeconds(seconds);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void FailSafeMaxStale_WhenZeroOrNegative_ThrowsArgumentOutOfRangeException(int seconds)
    {
        var options = new CacheEntryOptions();
        var act = () => options.FailSafeMaxStale = TimeSpan.FromSeconds(seconds);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void FromAbsolute_WhenZeroOrNegative_ThrowsArgumentOutOfRangeException()
    {
        var act = () => CacheEntryOptions.FromAbsolute(TimeSpan.FromSeconds(-1));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void FromSliding_WhenZeroOrNegative_ThrowsArgumentOutOfRangeException()
    {
        var act = () => CacheEntryOptions.FromSliding(TimeSpan.Zero);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void FromAbsoluteWithFailSafe_WhenInvalidDurations_ThrowsArgumentOutOfRangeException()
    {
        var act1 = () => CacheEntryOptions.FromAbsoluteWithFailSafe(TimeSpan.FromSeconds(-1), TimeSpan.FromMinutes(5));
        act1.Should().Throw<ArgumentOutOfRangeException>();

        var act2 = () => CacheEntryOptions.FromAbsoluteWithFailSafe(TimeSpan.FromMinutes(5), TimeSpan.Zero);
        act2.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void HybridCacheEntryOptions_WhenDurationZeroOrNegative_ThrowsArgumentOutOfRangeException()
    {
        var options = new HybridCacheEntryOptions();

        var actLocal = () => options.LocalCacheDuration = TimeSpan.FromSeconds(-1);
        actLocal.Should().Throw<ArgumentOutOfRangeException>();

        var actDist = () => options.DistributedCacheDuration = TimeSpan.Zero;
        actDist.Should().Throw<ArgumentOutOfRangeException>();
    }
}
