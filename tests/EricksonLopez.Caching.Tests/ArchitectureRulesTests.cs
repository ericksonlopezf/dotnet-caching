// Copyright © Erickson Lopez. MIT License.
using System;
using System.Linq;
using System.Reflection;
using AwesomeAssertions;
using NetArchTest.Rules;
using Xunit;

namespace EricksonLopez.Caching.Tests;

public sealed class ArchitectureRulesTests
{
    private static readonly Assembly CachingAssembly = typeof(ICacheProvider).Assembly;

    [Fact]
    public void CoreCaching_ShouldNotDependOn_StackExchangeRedis()
    {
        var result = Types.InAssembly(CachingAssembly)
            .ShouldNot()
            .HaveDependencyOn("StackExchange.Redis")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void CoreCaching_ShouldNotDependOn_CachingRedis()
    {
        var result = Types.InAssembly(CachingAssembly)
            .ShouldNot()
            .HaveDependencyOn("EricksonLopez.Caching.Redis")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void AllInterfaces_ShouldStartWithI_AndResideInCorrectNamespace()
    {
        var result = Types.InAssembly(CachingAssembly)
            .That()
            .AreInterfaces()
            .Should()
            .HaveNameStartingWith("I")
            .And()
            .ResideInNamespace("EricksonLopez.Caching")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Classes_ShouldBeSealed_ExceptExplicitlyDesignedForInheritance()
    {
        // CacheEntryOptions is intentionally open for extension by HybridCacheEntryOptions
        var allowedUnsealed = new[]
        {
            typeof(CacheEntryOptions).FullName
        };

        var types = Types.InAssembly(CachingAssembly)
            .That()
            .AreClasses()
            .And()
            .AreNotAbstract()
            .And()
            .DoNotHaveNameEndingWith("Attribute")
            .GetTypes()
            .Where(t => !t.IsSealed && !allowedUnsealed.Contains(t.FullName) && !t.Name.StartsWith('<'))
            .ToList();

        types.Should().BeEmpty();
    }

    [Fact]
    public void CacheProviders_ShouldImplement_ICacheProvider()
    {
        var result = Types.InAssembly(CachingAssembly)
            .That()
            .AreClasses()
            .And()
            .HaveNameEndingWith("CacheProvider")
            .Should()
            .ImplementInterface(typeof(ICacheProvider))
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void PublicTypes_ShouldResideIn_RootNamespace()
    {
        var result = Types.InAssembly(CachingAssembly)
            .That()
            .ArePublic()
            .Should()
            .ResideInNamespace("EricksonLopez.Caching")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
