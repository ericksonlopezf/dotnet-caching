// Copyright © Erickson Lopez. MIT License.
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EricksonLopez.Caching;

/// <summary>
/// Extension methods for configuring caching providers and services in Microsoft Dependency Injection.
/// </summary>
public static class CachingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="MemoryCacheProvider"/> as the default singleton <see cref="ICacheProvider"/>
    /// and <see cref="ITaggedCacheProvider"/>.
    /// </summary>
    public static IServiceCollection AddMemoryCacheProvider(this IServiceCollection services) =>
        AddMemoryCacheProvider(services, null);

    /// <summary>
    /// Registers the <see cref="MemoryCacheProvider"/> as the default singleton <see cref="ICacheProvider"/>
    /// and <see cref="ITaggedCacheProvider"/> with optional custom configuration options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration action for <see cref="MemoryCacheOptions"/>.</param>
    public static IServiceCollection AddMemoryCacheProvider(
        this IServiceCollection services,
        Action<MemoryCacheOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<MemoryCacheProvider>();
        services.TryAddSingleton<ICacheProvider>(sp => sp.GetRequiredService<MemoryCacheProvider>());
        services.TryAddSingleton<ITaggedCacheProvider>(sp => sp.GetRequiredService<MemoryCacheProvider>());
        return services;
    }

    /// <summary>
    /// Registers the canonical <see cref="CacheInvalidator"/> as the default scoped <see cref="ICacheInvalidator"/>.
    /// </summary>
    public static IServiceCollection AddCacheInvalidator(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<ICacheInvalidator, CacheInvalidator>();
        return services;
    }

    /// <summary>
    /// Registers a custom implementation of <see cref="ICacheInvalidator"/> in the service collection.
    /// </summary>
    /// <typeparam name="TInvalidator">The invalidator implementation type.</typeparam>
    public static IServiceCollection AddCacheInvalidator<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TInvalidator>(this IServiceCollection services)
        where TInvalidator : class, ICacheInvalidator
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<ICacheInvalidator, TInvalidator>();
        return services;
    }

    /// <summary>
    /// Registers a <see cref="HybridCacheProvider"/> that coordinates an L1 local cache with an L2 distributed cache.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="localCacheFactory">Factory to resolve the L1 local in-memory cache.</param>
    /// <param name="distributedCacheFactory">Factory to resolve the L2 distributed cache.</param>
    public static IServiceCollection AddHybridCache(
        this IServiceCollection services,
        Func<IServiceProvider, ICacheProvider> localCacheFactory,
        Func<IServiceProvider, ICacheProvider> distributedCacheFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(localCacheFactory);
        ArgumentNullException.ThrowIfNull(distributedCacheFactory);

        services.AddSingleton<ICacheProvider>(sp =>
        {
            var local = localCacheFactory(sp);
            var distributed = distributedCacheFactory(sp);
            return new HybridCacheProvider(local, distributed);
        });

        services.AddSingleton<ITaggedCacheProvider>(sp =>
            (ITaggedCacheProvider)sp.GetRequiredService<ICacheProvider>());

        return services;
    }

    /// <summary>
    /// Decorates an existing <see cref="ICacheProvider"/> registration with <see cref="TenantPartitionedCacheProvider"/>
    /// using the specified tenant resolution delegate.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="tenantIdResolver">Delegate resolving the current tenant identifier from the service provider.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTenantPartitionedCache(
        this IServiceCollection services,
        Func<IServiceProvider, string?> tenantIdResolver)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tenantIdResolver);

        var existingRegistration = services.LastOrDefault(d => d.ServiceType == typeof(ICacheProvider));
        Func<IServiceProvider, ICacheProvider> innerFactory;
        if (existingRegistration is not null)
        {
            if (existingRegistration.ImplementationInstance is not null)
            {
                var instance = (ICacheProvider)existingRegistration.ImplementationInstance;
                innerFactory = _ => instance;
            }
            else if (existingRegistration.ImplementationFactory is not null)
            {
                var factory = existingRegistration.ImplementationFactory;
                innerFactory = sp => (ICacheProvider)factory(sp);
            }
            else
            {
                var implType = existingRegistration.ImplementationType!;
                innerFactory = sp => (ICacheProvider)ActivatorUtilities.GetServiceOrCreateInstance(sp, implType);
            }
        }
        else
        {
            innerFactory = sp => sp.GetRequiredService<MemoryCacheProvider>();
        }

        services.AddScoped<TenantPartitionedCacheProvider>(sp =>
        {
            var inner = innerFactory(sp);
            return new TenantPartitionedCacheProvider(inner, () => tenantIdResolver(sp));
        });

        services.AddScoped<ICacheProvider>(sp => sp.GetRequiredService<TenantPartitionedCacheProvider>());
        services.AddScoped<ITaggedCacheProvider>(sp => sp.GetRequiredService<TenantPartitionedCacheProvider>());

        return services;
    }
}
