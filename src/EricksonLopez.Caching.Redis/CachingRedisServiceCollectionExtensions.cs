// Copyright © Erickson Lopez. MIT License.
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace EricksonLopez.Caching.Redis;

/// <summary>
/// Extension methods for registering the Redis cache provider in the DI container.
/// </summary>
public static class CachingRedisServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Redis-backed L2 cache provider as the <see cref="ICacheProvider"/> and <see cref="ITaggedCacheProvider"/> implementation.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Delegate to configure <see cref="RedisCacheOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRedisCaching(
        this IServiceCollection services,
        Action<RedisCacheOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);

        services.TryAddSingleton<ICacheSerializer>(SystemTextJsonCacheSerializer.Default);

        // Register connection multiplexer as singleton (connection pooling)
        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RedisCacheOptions>>().Value;
            return ConnectionMultiplexer.Connect(options.Configuration);
        });

        services.AddSingleton<RedisCacheProvider>();
        services.AddSingleton<ICacheProvider>(sp => sp.GetRequiredService<RedisCacheProvider>());
        services.AddSingleton<ITaggedCacheProvider>(sp => sp.GetRequiredService<RedisCacheProvider>());

        return services;
    }

    /// <summary>
    /// Adds the Redis-backed L2 cache provider using an existing <see cref="IConnectionMultiplexer"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionMultiplexer">An existing Redis connection multiplexer.</param>
    /// <param name="configure">Optional delegate to configure <see cref="RedisCacheOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRedisCaching(
        this IServiceCollection services,
        IConnectionMultiplexer connectionMultiplexer,
        Action<RedisCacheOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionMultiplexer);

        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.Configure<RedisCacheOptions>(_ => { });
        }

        services.TryAddSingleton<ICacheSerializer>(SystemTextJsonCacheSerializer.Default);
        services.TryAddSingleton(connectionMultiplexer);

        services.AddSingleton<RedisCacheProvider>();
        services.AddSingleton<ICacheProvider>(sp => sp.GetRequiredService<RedisCacheProvider>());
        services.AddSingleton<ITaggedCacheProvider>(sp => sp.GetRequiredService<RedisCacheProvider>());

        return services;
    }

    /// <summary>
    /// Adds the Redis-backed L2 cache provider using a factory to resolve or pre-warm <see cref="IConnectionMultiplexer"/>.
    /// Enables non-blocking, deferred, or custom multiplexer lifecycle management.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionMultiplexerFactory">Factory resolving the connection multiplexer.</param>
    /// <param name="configure">Optional delegate to configure <see cref="RedisCacheOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRedisCaching(
        this IServiceCollection services,
        Func<IServiceProvider, IConnectionMultiplexer> connectionMultiplexerFactory,
        Action<RedisCacheOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionMultiplexerFactory);

        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.Configure<RedisCacheOptions>(_ => { });
        }

        services.TryAddSingleton<ICacheSerializer>(SystemTextJsonCacheSerializer.Default);
        services.TryAddSingleton(connectionMultiplexerFactory);

        services.AddSingleton<RedisCacheProvider>();
        services.AddSingleton<ICacheProvider>(sp => sp.GetRequiredService<RedisCacheProvider>());
        services.AddSingleton<ITaggedCacheProvider>(sp => sp.GetRequiredService<RedisCacheProvider>());

        return services;
    }
}
