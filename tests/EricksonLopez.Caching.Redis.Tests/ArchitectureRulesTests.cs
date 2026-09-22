// Copyright © Erickson Lopez. MIT License.
using System;
using System.Linq;
using System.Reflection;
using AwesomeAssertions;
using EricksonLopez.Caching.Redis;
using NetArchTest.Rules;
using Xunit;

namespace EricksonLopez.Caching.Redis.Tests;

public sealed class ArchitectureRulesTests
{
    private static readonly Assembly RedisAssembly = typeof(RedisCacheProvider).Assembly;

    [Fact]
    public void RedisCacheProvider_ShouldImplement_ITaggedCacheProviderAndIAsyncDisposable()
    {
        var result = Types.InAssembly(RedisAssembly)
            .That()
            .HaveName("RedisCacheProvider")
            .Should()
            .ImplementInterface(typeof(ITaggedCacheProvider))
            .And()
            .ImplementInterface(typeof(IAsyncDisposable))
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void RedisClasses_ShouldBeSealed()
    {
        var types = Types.InAssembly(RedisAssembly)
            .That()
            .AreClasses()
            .And()
            .AreNotAbstract()
            .GetTypes()
            .Where(t => !t.IsSealed && !t.Name.StartsWith('<'))
            .ToList();

        types.Should().BeEmpty();
    }

    [Fact]
    public void PublicTypes_ShouldResideIn_RedisNamespace()
    {
        var result = Types.InAssembly(RedisAssembly)
            .That()
            .ArePublic()
            .Should()
            .ResideInNamespace("EricksonLopez.Caching.Redis")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
