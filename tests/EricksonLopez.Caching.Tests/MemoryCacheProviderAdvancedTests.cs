// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EricksonLopez.Caching.Tests;

public sealed class MemoryCacheProviderAdvancedTests
{
    [Fact]
    public void MemoryCacheOptions_Validation_EnforcesInvariants()
    {
        var options = new MemoryCacheOptions();

        // MaxCapacity must be strictly positive
        var actCapacityZero = () => options.MaxCapacity = 0;
        actCapacityZero.Should().Throw<ArgumentOutOfRangeException>();

        var actCapacityNeg = () => options.MaxCapacity = -5;
        actCapacityNeg.Should().Throw<ArgumentOutOfRangeException>();

        options.MaxCapacity = 500;
        options.MaxCapacity.Should().Be(500);

        // ExpirationScanFrequency must be strictly positive
        var actFreqZero = () => options.ExpirationScanFrequency = TimeSpan.Zero;
        actFreqZero.Should().Throw<ArgumentOutOfRangeException>();

        var actFreqNeg = () => options.ExpirationScanFrequency = TimeSpan.FromSeconds(-1);
        actFreqNeg.Should().Throw<ArgumentOutOfRangeException>();

        options.ExpirationScanFrequency = TimeSpan.FromMinutes(1);
        options.ExpirationScanFrequency.Should().Be(TimeSpan.FromMinutes(1));

        // CompactPercentage must be between 0.05 and 0.50
        var actPctTooLow = () => options.CompactPercentage = 0.04;
        actPctTooLow.Should().Throw<ArgumentOutOfRangeException>();

        var actPctTooHigh = () => options.CompactPercentage = 0.51;
        actPctTooHigh.Should().Throw<ArgumentOutOfRangeException>();

        options.CompactPercentage = 0.30;
        options.CompactPercentage.Should().Be(0.30);
    }

    [Fact]
    public async Task MaxCapacity_WhenExceeded_PerformsLRUCompaction()
    {
        var fakeTime = new FakeTimeProvider();
        var options = new MemoryCacheOptions
        {
            MaxCapacity = 5,
            CompactPercentage = 0.40 // 40% of 5 = 2 entries evicted
        };

        using var provider = new MemoryCacheProvider(options, fakeTime);

        // Populate 5 entries
        for (var i = 1; i <= 5; i++)
        {
            await provider.SetAsync($"key:{i}", $"val:{i}");
            fakeTime.Advance(TimeSpan.FromSeconds(1));
        }

        // Access key:1 and key:2 to make them recently used
        await provider.GetAsync<string>("key:1");
        fakeTime.Advance(TimeSpan.FromSeconds(1));
        await provider.GetAsync<string>("key:2");
        fakeTime.Advance(TimeSpan.FromSeconds(1));

        // Now LRU order is: key:3 (oldest), key:4, key:5, key:1, key:2 (newest)
        // Adding 6th entry triggers compaction of 2 entries (key:3 and key:4)
        await provider.SetAsync("key:6", "val:6");

        // key:3 and key:4 should have been evicted
        (await provider.ExistsAsync("key:3")).Value.Should().BeFalse();
        (await provider.ExistsAsync("key:4")).Value.Should().BeFalse();

        // key:1, key:2, key:5, and key:6 should remain
        (await provider.ExistsAsync("key:1")).Value.Should().BeTrue();
        (await provider.ExistsAsync("key:2")).Value.Should().BeTrue();
        (await provider.ExistsAsync("key:5")).Value.Should().BeTrue();
        (await provider.ExistsAsync("key:6")).Value.Should().BeTrue();
    }

    [Fact]
    public async Task BackgroundScan_WhenConfigured_ActivelySweepsExpiredEntries()
    {
        var fakeTime = new FakeTimeProvider();
        var options = new MemoryCacheOptions
        {
            ExpirationScanFrequency = TimeSpan.FromSeconds(10)
        };

        using var provider = new MemoryCacheProvider(options, fakeTime);

        await provider.SetAsync(
            "temp_key",
            "val",
            new CacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5) });

        (await provider.ExistsAsync("temp_key")).Value.Should().BeTrue();

        // Advance time past expiration but before timer tick
        fakeTime.Advance(TimeSpan.FromSeconds(6));

        // Advance past scan frequency to trigger background timer callback
        fakeTime.Advance(TimeSpan.FromSeconds(5));

        // Background timer swept it actively
        (await provider.ExistsAsync("temp_key")).Value.Should().BeFalse();
    }

    [Fact]
    public async Task ExpireAsync_AdjustsAbsoluteExpirationSuccessfully()
    {
        var fakeTime = new FakeTimeProvider();
        using var provider = new MemoryCacheProvider(fakeTime);

        await provider.SetAsync(
            "doc:1",
            "Initial",
            new CacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });

        // Update expiration to 5 seconds
        var expireResult = await provider.ExpireAsync("doc:1", TimeSpan.FromSeconds(5));
        expireResult.IsSuccess.Should().BeTrue();
        expireResult.Value.Should().BeTrue();

        // Advance 4 seconds: still exists
        fakeTime.Advance(TimeSpan.FromSeconds(4));
        (await provider.ExistsAsync("doc:1")).Value.Should().BeTrue();

        // Advance 2 more seconds (total 6): expired
        fakeTime.Advance(TimeSpan.FromSeconds(2));
        (await provider.ExistsAsync("doc:1")).Value.Should().BeFalse();
    }

    [Fact]
    public async Task ExpireAsync_WhenTimeToLiveIsZeroOrNegative_ReturnsFailure()
    {
        using var provider = new MemoryCacheProvider();
        await provider.SetAsync("doc:2", "value");

        var resZero = await provider.ExpireAsync("doc:2", TimeSpan.Zero);
        resZero.IsFailure.Should().BeTrue();
        resZero.Error.Code.Should().Be("Cache.InvalidTimeToLive");

        var resNeg = await provider.ExpireAsync("doc:2", TimeSpan.FromSeconds(-1));
        resNeg.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task RefreshAsync_ExtendsSlidingExpirationWindow()
    {
        var fakeTime = new FakeTimeProvider();
        using var provider = new MemoryCacheProvider(fakeTime);

        await provider.SetAsync(
            "session:abc",
            "ActiveUser",
            new CacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(5) });

        // Advance 4 minutes
        fakeTime.Advance(TimeSpan.FromMinutes(4));

        // Refresh entry
        var refreshResult = await provider.RefreshAsync("session:abc");
        refreshResult.IsSuccess.Should().BeTrue();
        refreshResult.Value.Should().BeTrue();

        // Advance another 4 minutes: would have expired without refresh (total 8 min), but with refresh it is active
        fakeTime.Advance(TimeSpan.FromMinutes(4));
        (await provider.ExistsAsync("session:abc")).Value.Should().BeTrue();

        // Advance 2 more minutes (total 6 minutes since refresh): now expired
        fakeTime.Advance(TimeSpan.FromMinutes(2));
        (await provider.ExistsAsync("session:abc")).Value.Should().BeFalse();
    }

    [Fact]
    public async Task BulkOperations_GetMany_SetMany_RemoveMany_WorkCorrectly()
    {
        using var provider = new MemoryCacheProvider();

        var items = new Dictionary<string, string>
        {
            ["alpha"] = "1",
            ["beta"] = "2",
            ["gamma"] = "3"
        };

        // SetManyAsync
        var setResult = await provider.SetManyAsync(items);
        setResult.IsSuccess.Should().BeTrue();
        setResult.Value.Should().BeTrue();

        // GetManyAsync
        var getResult = await provider.GetManyAsync<string>(["alpha", "beta", "gamma", "non_existent"]);
        getResult.IsSuccess.Should().BeTrue();
        getResult.Value.Should().HaveCount(3);
        getResult.Value["alpha"].Should().Be("1");
        getResult.Value["beta"].Should().Be("2");
        getResult.Value["gamma"].Should().Be("3");

        // RemoveManyAsync
        var removeResult = await provider.RemoveManyAsync(["alpha", "gamma", "non_existent"]);
        removeResult.IsSuccess.Should().BeTrue();
        removeResult.Value.Should().Be(2);

        // Beta still exists, alpha and gamma gone
        (await provider.ExistsAsync("beta")).Value.Should().BeTrue();
        (await provider.ExistsAsync("alpha")).Value.Should().BeFalse();
        (await provider.ExistsAsync("gamma")).Value.Should().BeFalse();
    }

    [Fact]
    public void AddMemoryCacheProvider_WithConfiguration_RegistersOptionsProperly()
    {
        var services = new ServiceCollection();
        services.AddMemoryCacheProvider(opt =>
        {
            opt.MaxCapacity = 10_000;
            opt.CompactPercentage = 0.20;
        });

        using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<MemoryCacheProvider>();
        var cacheProvider = sp.GetRequiredService<ICacheProvider>();
        var taggedProvider = sp.GetRequiredService<ITaggedCacheProvider>();

        provider.Should().NotBeNull();
        cacheProvider.Should().BeSameAs(provider);
        taggedProvider.Should().BeSameAs(provider);
    }
}
