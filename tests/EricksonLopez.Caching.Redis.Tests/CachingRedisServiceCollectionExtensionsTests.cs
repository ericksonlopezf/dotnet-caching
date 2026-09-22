// Copyright © Erickson Lopez. MIT License.
using System;
using System.Linq;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace EricksonLopez.Caching.Redis.Tests;

public sealed class CachingRedisServiceCollectionExtensionsTests
{
    private readonly IConnectionMultiplexer _multiplexer = Substitute.For<IConnectionMultiplexer>();

    [Fact]
    public void AddRedisCaching_WithExistingMultiplexer_RegistersAllServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddRedisCaching(_multiplexer, options =>
        {
            options.InstanceName = "app";
            options.Database = 1;
        });

        var provider = services.BuildServiceProvider();

        var resolvedMultiplexer = provider.GetService<IConnectionMultiplexer>();
        var serializer = provider.GetService<ICacheSerializer>();
        var redisProvider = provider.GetService<RedisCacheProvider>();
        var cacheProvider = provider.GetService<ICacheProvider>();
        var taggedProvider = provider.GetService<ITaggedCacheProvider>();

        resolvedMultiplexer.Should().BeSameAs(_multiplexer);
        serializer.Should().BeSameAs(SystemTextJsonCacheSerializer.Default);
        redisProvider.Should().NotBeNull();
        cacheProvider.Should().BeSameAs(redisProvider);
        taggedProvider.Should().BeSameAs(redisProvider);
    }

    [Fact]
    public void AddRedisCaching_WithExistingMultiplexer_WithoutConfigureAction_RegistersSuccessfully()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddRedisCaching(_multiplexer);

        var provider = services.BuildServiceProvider();
        var cacheProvider = provider.GetService<ICacheProvider>();

        cacheProvider.Should().NotBeNull();
    }

    [Fact]
    public void AddRedisCaching_WithOptionsAction_RegistersServiceDescriptors()
    {
        var services = new ServiceCollection();

        services.AddRedisCaching(options =>
        {
            options.Configuration = "localhost:6379,abortConnect=false";
        });

        services.Any(s => s.ServiceType == typeof(IConnectionMultiplexer) && s.Lifetime == ServiceLifetime.Singleton).Should().BeTrue();
        services.Any(s => s.ServiceType == typeof(ICacheSerializer) && s.Lifetime == ServiceLifetime.Singleton).Should().BeTrue();
        services.Any(s => s.ServiceType == typeof(RedisCacheProvider) && s.Lifetime == ServiceLifetime.Singleton).Should().BeTrue();
        services.Any(s => s.ServiceType == typeof(ICacheProvider) && s.Lifetime == ServiceLifetime.Singleton).Should().BeTrue();
        services.Any(s => s.ServiceType == typeof(ITaggedCacheProvider) && s.Lifetime == ServiceLifetime.Singleton).Should().BeTrue();
    }

    [Fact]
    public void AddRedisCaching_GuardClauses_ThrowArgumentNullException()
    {
        IServiceCollection nullServices = null!;
        var validServices = new ServiceCollection();
        IConnectionMultiplexer nullMultiplexer = null!;
        Action<RedisCacheOptions> nullAction = null!;

        var act1 = () => nullServices.AddRedisCaching(_ => { });
        var act2 = () => validServices.AddRedisCaching(nullAction);
        var act3 = () => nullServices.AddRedisCaching(_multiplexer);
        var act4 = () => validServices.AddRedisCaching(nullMultiplexer);

        act1.Should().Throw<ArgumentNullException>().WithParameterName("services");
        act2.Should().Throw<ArgumentNullException>().WithParameterName("configure");
        act3.Should().Throw<ArgumentNullException>().WithParameterName("services");
        act4.Should().Throw<ArgumentNullException>().WithParameterName("connectionMultiplexer");
    }
}
