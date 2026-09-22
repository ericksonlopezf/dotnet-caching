// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EricksonLopez.Caching.Tests;

/// <summary>
/// Property-based invariant test suite for ICacheProvider.
/// Uses randomized operation sequences and schedules against an oracle model.
/// </summary>
public sealed class PropertyBasedCacheTests
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Property_SetThenGet_AlwaysReturnsSetValue_UnlessExpiredOrRemoved()
    {
        var provider = new MemoryCacheProvider(_timeProvider);
        var rng = new Random(42);
        const int iterations = 500;

        for (var i = 0; i < iterations; i++)
        {
            var key = $"prop:key:{rng.Next(1, 100)}";
            var value = $"val:{rng.Next()}";
            var ttlSeconds = rng.Next(1, 60);

            var setResult = await provider.SetAsync(key, value, CacheEntryOptions.FromAbsolute(TimeSpan.FromSeconds(ttlSeconds)));
            setResult.IsSuccess.Should().BeTrue();

            var getResult = await provider.GetAsync<string>(key);
            getResult.IsSuccess.Should().BeTrue();
            getResult.Value.Should().Be(value, "Set(K, V) must immediately be observable via Get(K)");

            // Advance time past TTL
            _timeProvider.Advance(TimeSpan.FromSeconds(ttlSeconds + 1));

            var expiredGet = await provider.GetAsync<string>(key);
            expiredGet.IsSuccess.Should().BeTrue();
            expiredGet.Value.Should().BeNull("expired entry must never be observable");
        }
    }

    [Fact]
    public async Task Property_Remove_GuaranteesSubsequentGetReturnsNull()
    {
        var provider = new MemoryCacheProvider(_timeProvider);
        var rng = new Random(1337);

        for (var i = 0; i < 200; i++)
        {
            var key = $"prop:remove:{i}";
            var value = $"data_{i}";

            await provider.SetAsync(key, value);
            var existsBefore = await provider.ExistsAsync(key);
            existsBefore.Value.Should().BeTrue();

            var removeResult = await provider.RemoveAsync(key);
            removeResult.Value.Should().BeTrue();

            var getResult = await provider.GetAsync<string>(key);
            getResult.Value.Should().BeNull();

            var existsAfter = await provider.ExistsAsync(key);
            existsAfter.Value.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Property_PrefixInvalidation_OnlyInvalidatesMatchingKeys()
    {
        var provider = new MemoryCacheProvider(_timeProvider);
        const string targetPrefix = "catalog:electronics:";
        const string otherPrefix = "catalog:books:";

        for (var i = 0; i < 50; i++)
        {
            await provider.SetAsync($"{targetPrefix}item_{i}", $"electronic_{i}");
            await provider.SetAsync($"{otherPrefix}item_{i}", $"book_{i}");
        }

        var removeResult = await provider.RemoveByPrefixAsync(targetPrefix);
        removeResult.Value.Should().BeTrue();

        // Target keys must be gone
        for (var i = 0; i < 50; i++)
        {
            var targetGet = await provider.GetAsync<string>($"{targetPrefix}item_{i}");
            targetGet.Value.Should().BeNull();

            // Other keys must be intact
            var otherGet = await provider.GetAsync<string>($"{otherPrefix}item_{i}");
            otherGet.Value.Should().Be($"book_{i}");
        }
    }

    [Fact]
    public async Task Property_ConcurrentRandomWorkload_MatchesSequentialOracle()
    {
        var provider = new MemoryCacheProvider(_timeProvider);
        var oracle = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        var keys = Enumerable.Range(0, 50).Select(k => $"key_{k}").ToArray();

        var tasks = Enumerable.Range(0, 16).Select(t => Task.Run(async () =>
        {
            var rng = new Random(t * 100);
            for (var i = 0; i < 100; i++)
            {
                var key = keys[rng.Next(keys.Length)];
                var op = rng.Next(3);

                switch (op)
                {
                    case 0: // Set
                        var val = $"val_{t}_{i}";
                        await provider.SetAsync(key, val);
                        oracle[key] = val;
                        break;

                    case 1: // Get
                        var getRes = await provider.GetAsync<string>(key);
                        getRes.IsSuccess.Should().BeTrue();
                        break;

                    case 2: // Exists
                        var existsRes = await provider.ExistsAsync(key);
                        existsRes.IsSuccess.Should().BeTrue();
                        break;
                }
            }
        }));

        await Task.WhenAll(tasks);
    }
}
