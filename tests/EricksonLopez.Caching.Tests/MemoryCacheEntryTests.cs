// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.Caching.Tests;

public sealed class MemoryCacheEntryTests
{
    [Fact]
    public void IsExpired_WhenNoExpirationsSet_ReturnsFalse()
    {
        var now = DateTimeOffset.UtcNow;
        var entry = new MemoryCacheEntry("value", now);

        entry.IsExpired(now.AddDays(10)).Should().BeFalse();
    }

    [Fact]
    public void IsExpired_WhenNowEqualsAbsoluteExpiration_ReturnsTrue()
    {
        var now = DateTimeOffset.UtcNow;
        var abs = now.AddMinutes(5);
        var entry = new MemoryCacheEntry("value", now, absoluteExpiration: abs);

        entry.IsExpired(abs.AddTicks(-1)).Should().BeFalse();
        entry.IsExpired(abs).Should().BeTrue(); // Exact boundary kills >= mutated to >
        entry.IsExpired(abs.AddMinutes(1)).Should().BeTrue();
    }

    [Fact]
    public void IsExpired_WhenElapsedEqualsSlidingExpiration_ReturnsTrue()
    {
        var now = DateTimeOffset.UtcNow;
        var sliding = TimeSpan.FromMinutes(2);
        var entry = new MemoryCacheEntry("value", now, slidingExpiration: sliding);

        entry.IsExpired(now + sliding - TimeSpan.FromTicks(1)).Should().BeFalse();
        entry.IsExpired(now + sliding).Should().BeTrue(); // Exact boundary kills >= mutated to >
        entry.IsExpired(now + sliding + TimeSpan.FromMinutes(1)).Should().BeTrue();
    }

    [Fact]
    public void IsWithinStaleWindow_WhenFailSafeMaxStaleIsNull_ReturnsFalse()
    {
        var now = DateTimeOffset.UtcNow;
        var entry = new MemoryCacheEntry("value", now, failSafeMaxStale: null);

        entry.IsWithinStaleWindow(now).Should().BeFalse();
        entry.IsWithinStaleWindow(now.AddMinutes(5)).Should().BeFalse();
    }

    [Fact]
    public void IsWithinStaleWindow_WithAbsoluteExpiration_WhenNowEqualsBoundary_ReturnsTrue()
    {
        var now = DateTimeOffset.UtcNow;
        var abs = now.AddMinutes(5);
        var stale = TimeSpan.FromMinutes(10);
        var entry = new MemoryCacheEntry("value", now, absoluteExpiration: abs, failSafeMaxStale: stale);

        var boundary = abs + stale;
        entry.IsWithinStaleWindow(boundary).Should().BeTrue(); // Exact boundary kills <= mutated to <
        entry.IsWithinStaleWindow(boundary + TimeSpan.FromTicks(1)).Should().BeFalse();
    }

    [Fact]
    public void IsWithinStaleWindow_WithSlidingExpirationOnly_CalculatesBaseFromLastAccessedAt()
    {
        var now = DateTimeOffset.UtcNow;
        var sliding = TimeSpan.FromMinutes(3);
        var stale = TimeSpan.FromMinutes(5);
        var entry = new MemoryCacheEntry("value", now, slidingExpiration: sliding, failSafeMaxStale: stale);

        var boundary = now + sliding + stale;
        entry.IsWithinStaleWindow(boundary).Should().BeTrue(); // Kills ternary true/false mutations
        entry.IsWithinStaleWindow(boundary + TimeSpan.FromTicks(1)).Should().BeFalse();
    }

    [Fact]
    public void IsWithinStaleWindow_WithNeitherAbsoluteNorSliding_CalculatesBaseFromCreatedAt()
    {
        var now = DateTimeOffset.UtcNow;
        var stale = TimeSpan.FromMinutes(5);
        var entry = new MemoryCacheEntry("value", now, absoluteExpiration: null, slidingExpiration: null, failSafeMaxStale: stale);

        var boundary = now + stale;
        entry.IsWithinStaleWindow(boundary).Should().BeTrue(); // Kills fallback to CreatedAt mutation
        entry.IsWithinStaleWindow(boundary + TimeSpan.FromTicks(1)).Should().BeFalse();
    }

    [Fact]
    public void IsWithinStaleWindow_WithBothAbsoluteAndSliding_UsesEarlierExpirationAsBase()
    {
        var now = DateTimeOffset.UtcNow;
        var abs = now.AddMinutes(10);
        var sliding = TimeSpan.FromMinutes(2);
        var stale = TimeSpan.FromMinutes(1);
        var entry = new MemoryCacheEntry("value", now, absoluteExpiration: abs, slidingExpiration: sliding, failSafeMaxStale: stale);

        // Sliding expires at now + 2m, which is earlier than abs (now + 10m).
        // Stale boundary should be now + 2m + 1m = now + 3m.
        var boundary = now + sliding + stale;
        entry.IsWithinStaleWindow(boundary).Should().BeTrue();
        entry.IsWithinStaleWindow(boundary + TimeSpan.FromTicks(1)).Should().BeFalse();
    }
}
