// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EricksonLopez.Caching.Tests;

public sealed class CachingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddMemoryCacheProvider_RegistersSingletonCacheProvider()
    {
        var services = new ServiceCollection();
        services.AddMemoryCacheProvider();

        var provider = services.BuildServiceProvider();

        var cache = provider.GetService<ICacheProvider>();
        var taggedCache = provider.GetService<ITaggedCacheProvider>();

        cache.Should().NotBeNull();
        taggedCache.Should().NotBeNull();
        cache.Should().BeOfType<MemoryCacheProvider>();
        cache.Should().BeSameAs(taggedCache);
    }

    [Fact]
    public void AddCacheInvalidator_RegistersScopedCacheInvalidator()
    {
        var services = new ServiceCollection();
        services.AddMemoryCacheProvider();
        services.AddCacheInvalidator();

        var provider = services.BuildServiceProvider();

        using var scope1 = provider.CreateScope();
        var invalidator1 = scope1.ServiceProvider.GetService<ICacheInvalidator>();
        invalidator1.Should().NotBeNull();
        invalidator1.Should().BeOfType<CacheInvalidator>();

        using var scope2 = provider.CreateScope();
        var invalidator2 = scope2.ServiceProvider.GetService<ICacheInvalidator>();
        invalidator2.Should().NotBeNull();
        invalidator1.Should().NotBeSameAs(invalidator2);
    }

    [Fact]
    public void AddCacheInvalidator_Generic_RegistersCustomInvalidator()
    {
        var services = new ServiceCollection();
        services.AddMemoryCacheProvider();
        services.AddCacheInvalidator<CustomCacheInvalidator>();

        var provider = services.BuildServiceProvider();
        var invalidator = provider.GetService<ICacheInvalidator>();

        invalidator.Should().NotBeNull();
        invalidator.Should().BeOfType<CustomCacheInvalidator>();
    }

    [Fact]
    public void AddHybridCache_RegistersHybridCacheProvider()
    {
        var services = new ServiceCollection();
        services.AddHybridCache(
            sp => new MemoryCacheProvider(),
            sp => new MemoryCacheProvider());

        var provider = services.BuildServiceProvider();

        var cache = provider.GetService<ICacheProvider>();
        var taggedCache = provider.GetService<ITaggedCacheProvider>();

        cache.Should().NotBeNull();
        taggedCache.Should().NotBeNull();
        cache.Should().BeOfType<HybridCacheProvider>();
    }

    [Fact]
    public void Extensions_GuardClauses_ThrowArgumentNullException()
    {
        IServiceCollection nullServices = null!;
        var validServices = new ServiceCollection();

        var act1 = () => nullServices.AddMemoryCacheProvider();
        var act2 = () => nullServices.AddCacheInvalidator();
        var act3 = () => nullServices.AddCacheInvalidator<CustomCacheInvalidator>();
        var act4 = () => nullServices.AddHybridCache(sp => new MemoryCacheProvider(), sp => new MemoryCacheProvider());
        var act5 = () => validServices.AddHybridCache(null!, sp => new MemoryCacheProvider());
        var act6 = () => validServices.AddHybridCache(sp => new MemoryCacheProvider(), null!);

        act1.Should().Throw<ArgumentNullException>().WithParameterName("services");
        act2.Should().Throw<ArgumentNullException>().WithParameterName("services");
        act3.Should().Throw<ArgumentNullException>().WithParameterName("services");
        act4.Should().Throw<ArgumentNullException>().WithParameterName("services");
        act5.Should().Throw<ArgumentNullException>().WithParameterName("localCacheFactory");
        act6.Should().Throw<ArgumentNullException>().WithParameterName("distributedCacheFactory");
    }

    private sealed class CustomCacheInvalidator : ICacheInvalidator
    {
        public Task<Result<bool>> InvalidateAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<bool>.Success(true));

        public Task<Result<long>> InvalidateManyAsync(System.Collections.Generic.IEnumerable<string> keys, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<long>.Success(0));

        public Task<Result<bool>> InvalidateByPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<bool>.Success(true));

        public Task<Result<bool>> InvalidateByPrefixesAsync(System.Collections.Generic.IEnumerable<string> prefixes, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<bool>.Success(true));

        public Task<Result<bool>> InvalidateByTagAsync(string tag, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<bool>.Success(true));

        public Task<Result<bool>> InvalidateByTagsAsync(System.Collections.Generic.IEnumerable<string> tags, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<bool>.Success(true));
    }
}
