// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EricksonLopez.Caching.Tests;

/// <summary>
/// Adversarial security, memory leak, key injection, and multi-tenancy audit tests.
/// Actively attempts to break the caching engine and verifies bounded resource consumption.
/// </summary>
public sealed class AdversarialSecurityAndMemoryTests
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Audit_MemoryLeak_LocksDictionary_CleansUpSemaphoresAfterCompletion()
    {
        // Arrange
        var provider = new MemoryCacheProvider(_timeProvider);
        const int uniqueKeyCount = 500;

        // Act - Call GetOrCreateAsync on 500 distinct keys
        for (var i = 0; i < uniqueKeyCount; i++)
        {
            var key = $"transient_key_{i}";
            await provider.GetOrCreateAsync(key, _ => Task.FromResult($"val_{i}"));
        }

        // Inspect internal _locks dictionary via reflection
        var locksField = typeof(MemoryCacheProvider).GetField("_locks", BindingFlags.NonPublic | BindingFlags.Instance);
        locksField.Should().NotBeNull();
        var locksDict = locksField!.GetValue(provider) as IDictionary;

        // Assert - Verified: _locks cleans up all semaphores after factories finish (Count == 0)
        locksDict.Should().NotBeNull();
        locksDict!.Count.Should().Be(0,
            "VERIFIED REMEDIATION: MemoryCacheProvider._locks cleans up semaphore instances when no longer in use");
    }

    [Fact]
    public async Task Audit_MemoryLeak_TagKeys_CleansUpKeysOnExplicitRemove()
    {
        // Arrange
        var provider = new MemoryCacheProvider(_timeProvider);
        const string tag = "order_tag";
        const string key = "order:12345";

        await provider.SetWithTagsAsync(key, "order_data", [tag]);

        // Explicitly remove the key from cache
        var removeResult = await provider.RemoveAsync(key);
        removeResult.Value.Should().BeTrue();

        // Inspect internal _tagKeys dictionary via reflection
        var tagKeysField = typeof(MemoryCacheProvider).GetField("_tagKeys", BindingFlags.NonPublic | BindingFlags.Instance);
        tagKeysField.Should().NotBeNull();
        var tagKeysDict = tagKeysField!.GetValue(provider) as IDictionary;

        tagKeysDict.Should().NotBeNull();
        var tagBucket = tagKeysDict![tag] as IDictionary;

        // Assert - Verified: key is removed from tag bucket!
        tagBucket.Should().NotBeNull();
        tagBucket!.Contains(key).Should().BeFalse(
            "VERIFIED REMEDIATION: MemoryCacheProvider cleans up orphaned key references from tags on explicit RemoveAsync");
    }

    [Fact]
    public async Task Audit_CrossTenant_AbsenceOfTenantIsolation_AllowsCrossTenantRead()
    {
        // Arrange - Two tenants accessing the same provider with non-namespaced keys
        var sharedProvider = new MemoryCacheProvider(_timeProvider);
        const string rawKey = "profile:42";

        // Tenant 1 writes sensitive data
        await sharedProvider.SetAsync(rawKey, "Tenant1_Secret_Financial_Data");

        // Tenant 2 reads using the same key
        var tenant2Read = await sharedProvider.GetAsync<string>(rawKey);

        // Assert - Evidence: Tenant 2 reads Tenant 1's cached secret if no prefix/tenant discriminator is enforced
        tenant2Read.IsSuccess.Should().BeTrue();
        tenant2Read.Value.Should().Be("Tenant1_Secret_Financial_Data",
            "EVIDENCE FINDING: Caching layer does not enforce tenant boundary; cross-tenant crossover occurs without application-level prefixing");
    }

    [Fact]
    public async Task TenantPartitionedCacheProvider_GuaranteesStrictTenantIsolation()
    {
        // Arrange
        var underlying = new MemoryCacheProvider(_timeProvider);
        var currentTenant = "tenant_alpha";
        var partitioned = new TenantPartitionedCacheProvider(underlying, () => currentTenant);
        const string rawKey = "account:profile:99";

        // Act 1: Tenant Alpha writes sensitive data
        await partitioned.SetAsync(rawKey, "Alpha_Confidential");

        // Act 2: Tenant Beta attempts to read the same raw key
        currentTenant = "tenant_beta";
        var betaRead = await partitioned.GetAsync<string>(rawKey);

        // Assert
        betaRead.IsSuccess.Should().BeTrue();
        betaRead.Value.Should().BeNull("Tenant Beta must NEVER observe Tenant Alpha's cached data");

        // Act 3: Tenant Alpha reads again
        currentTenant = "tenant_alpha";
        var alphaRead = await partitioned.GetAsync<string>(rawKey);
        alphaRead.Value.Should().Be("Alpha_Confidential", "Tenant Alpha reads its own partitioned data");
    }

    [Fact]
    public async Task TenantPartitionedCacheProvider_PrefixInvalidation_IsStrictlyScopedToTenant()
    {
        // Arrange
        var underlying = new MemoryCacheProvider(_timeProvider);
        var currentTenant = "tenant_alpha";
        var partitioned = new TenantPartitionedCacheProvider(underlying, () => currentTenant);

        // Alpha sets entries
        await partitioned.SetAsync("catalog:item:1", "Alpha Item 1");
        await partitioned.SetAsync("catalog:item:2", "Alpha Item 2");

        // Beta sets entries with same raw keys
        currentTenant = "tenant_beta";
        await partitioned.SetAsync("catalog:item:1", "Beta Item 1");

        // Alpha invalidates its catalog prefix
        currentTenant = "tenant_alpha";
        var removeResult = await partitioned.RemoveByPrefixAsync("catalog:item");
        removeResult.Value.Should().BeTrue();

        // Alpha's entries are gone
        var alphaRead = await partitioned.GetAsync<string>("catalog:item:1");
        alphaRead.Value.Should().BeNull();

        // Beta's entries are completely preserved
        currentTenant = "tenant_beta";
        var betaRead = await partitioned.GetAsync<string>("catalog:item:1");
        betaRead.Value.Should().Be("Beta Item 1", "Tenant Beta's keys must NOT be invalidated by Tenant Alpha");
    }

    [Fact]
    public async Task TenantPartitionedCacheProvider_TagInvalidation_IsStrictlyScopedToTenant()
    {
        // Arrange
        var underlying = new MemoryCacheProvider(_timeProvider);
        var currentTenant = "tenant_alpha";
        var partitioned = new TenantPartitionedCacheProvider(underlying, () => currentTenant);

        // Alpha sets item with tag
        await partitioned.SetWithTagsAsync("item:100", "Alpha 100", ["promo_tag"]);

        // Beta sets item with same tag
        currentTenant = "tenant_beta";
        await partitioned.SetWithTagsAsync("item:100", "Beta 100", ["promo_tag"]);

        // Alpha invalidates promo_tag
        currentTenant = "tenant_alpha";
        var removeResult = await partitioned.RemoveByTagAsync("promo_tag");
        removeResult.Value.Should().BeTrue();

        // Alpha tag item removed
        var alphaRead = await partitioned.GetAsync<string>("item:100");
        alphaRead.Value.Should().BeNull();

        // Beta tag item still intact
        currentTenant = "tenant_beta";
        var betaRead = await partitioned.GetAsync<string>("item:100");
        betaRead.Value.Should().Be("Beta 100", "Tenant Beta's tagged entry must remain intact");
    }

    [Theory]
    [InlineData("key:with:many:colons:::nested")]
    [InlineData("key/with/forward/slashes/and\\backslashes")]
    [InlineData("key.with.dots..and...many")]
    [InlineData("key?with*glob[brackets]and(parentheses)")]
    [InlineData("key_with_unicode_ñ_ç_ü_漢字_🚀")]
    [InlineData("key_with_control_chars\t\r\n")]
    public async Task KeyInjection_SpecialCharacters_PreservesExactValueWithoutCorruption(string hostileKey)
    {
        // Arrange
        var provider = new MemoryCacheProvider(_timeProvider);
        const string expectedValue = "payload_content";

        // Act
        var setResult = await provider.SetAsync(hostileKey, expectedValue);
        setResult.IsSuccess.Should().BeTrue();

        var getResult = await provider.GetAsync<string>(hostileKey);

        // Assert
        getResult.IsSuccess.Should().BeTrue();
        getResult.Value.Should().Be(expectedValue);
    }

    [Fact]
    public async Task KeyInjection_EmptyOrWhitespaceKey_BehaviorDocumentation()
    {
        // Arrange
        var provider = new MemoryCacheProvider(_timeProvider);

        // Act & Assert: Empty string is accepted by MemoryCacheProvider.SetAsync
        var setResult = await provider.SetAsync("", "empty_key_val");
        setResult.IsSuccess.Should().BeTrue();

        var getResult = await provider.GetAsync<string>("");
        getResult.IsSuccess.Should().BeTrue();
        getResult.Value.Should().Be("empty_key_val");
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenFactoryFails_ReturnsFailureResultGracefully()
    {
        // Arrange
        var provider = new MemoryCacheProvider(_timeProvider);
        const string key = "failing_factory_key";

        // Act - factory throws non-cancellation exception and NO stale value exists
        var result = await provider.GetOrCreateAsync<string>(key, _ => throw new InvalidOperationException("DB connection timed out"));

        // Assert - VERIFIED REMEDIATION: returns Result.Failure matching ICacheProvider contract
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cache.Memory.FactoryFailed");
        result.Error.Description.Should().Contain("DB connection timed out");
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenNullValueCached_HitsCacheAndDoesNotReExecuteFactory()
    {
        // Arrange
        var provider = new MemoryCacheProvider(_timeProvider);
        const string key = "null_entity_key";
        var factoryExecutionCount = 0;

        // Act 1: Initial call caches null (negative caching)
        var firstResult = await provider.GetOrCreateAsync<string?>(key, async ct =>
        {
            Interlocked.Increment(ref factoryExecutionCount);
            await Task.Yield();
            return null;
        });

        firstResult.IsSuccess.Should().BeTrue();
        firstResult.Value.Should().BeNull();
        factoryExecutionCount.Should().Be(1);

        // Act 2: Subsequent call on same key must return cached null without re-executing factory
        var secondResult = await provider.GetOrCreateAsync<string?>(key, _ =>
        {
            Interlocked.Increment(ref factoryExecutionCount);
            return Task.FromResult<string?>("should_not_run");
        });

        // Assert - VERIFIED REMEDIATION: single-flight stampede protection works for cached nulls!
        secondResult.IsSuccess.Should().BeTrue();
        secondResult.Value.Should().BeNull();
        factoryExecutionCount.Should().Be(1, "Factory must NOT re-execute when cached value is null!");
    }

    [Fact]
    public async Task TenantPartitionedCacheProvider_ColonDelimiter_DoesNotCollideWithOtherTenants()
    {
        // Arrange
        var memory = new MemoryCacheProvider(_timeProvider);
        var currentTenant = "tenant_alpha:subaccount";
        var partitioned = new TenantPartitionedCacheProvider(memory, () => currentTenant);

        // Attacker with colon in tenant slug writes to "profile"
        await partitioned.SetAsync("profile", "Attacker_Data");

        // Legitimate tenant "tenant_alpha" queries "subaccount:profile"
        currentTenant = "tenant_alpha";
        var victimResult = await partitioned.GetAsync<string>("subaccount:profile");

        // Assert - VERIFIED REMEDIATION: Colon is sanitized (%3A), so keys do not collide!
        victimResult.IsSuccess.Should().BeTrue();
        victimResult.Value.Should().BeNull("Tenant keys must not collide across delimiter boundaries");
    }

    [Fact]
    public async Task AddTenantPartitionedCache_ResolvesICacheProviderAsTenantPartitioned()
    {
        // Arrange
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddMemoryCacheProvider();
        services.AddTenantPartitionedCache(_ => "tenant_42");
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        // Act
        var resolvedCache = scope.ServiceProvider.GetRequiredService<ICacheProvider>();
        var resolvedTaggedCache = scope.ServiceProvider.GetRequiredService<ITaggedCacheProvider>();

        // Assert - VERIFIED REMEDIATION: DI injects TenantPartitionedCacheProvider!
        resolvedCache.Should().BeOfType<TenantPartitionedCacheProvider>();
        resolvedTaggedCache.Should().BeOfType<TenantPartitionedCacheProvider>();
    }

    [Fact]
    public async Task CancellationToken_MemoryProviderOperations_ThrowsWhenPreCancelled()
    {
        // Arrange
        var provider = new MemoryCacheProvider(_timeProvider);
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled token

        // Act & Assert: All operations observe cancellation token and throw OperationCanceledException
        var getAct = () => provider.GetAsync<string>("any_key", cts.Token);
        await getAct.Should().ThrowAsync<OperationCanceledException>();

        var setAct = () => provider.SetAsync("any_key", "val", cancellationToken: cts.Token);
        await setAct.Should().ThrowAsync<OperationCanceledException>();

        var removeAct = () => provider.RemoveAsync("any_key", cts.Token);
        await removeAct.Should().ThrowAsync<OperationCanceledException>();

        var existsAct = () => provider.ExistsAsync("any_key", cts.Token);
        await existsAct.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task TenantPartitionedCacheProvider_ExistsAsync_EnforcesTenantPartitioning()
    {
        var memory = new MemoryCacheProvider(_timeProvider);
        var currentTenant = "tenant_a";
        var partitioned = new TenantPartitionedCacheProvider(memory, () => currentTenant);

        await partitioned.SetAsync("doc:1", "data");

        // Tenant A checks exists -> true
        var aExists = await partitioned.ExistsAsync("doc:1");
        aExists.IsSuccess.Should().BeTrue();
        aExists.Value.Should().BeTrue();

        // Switch to Tenant B -> false
        currentTenant = "tenant_b";
        var bExists = await partitioned.ExistsAsync("doc:1");
        bExists.IsSuccess.Should().BeTrue();
        bExists.Value.Should().BeFalse();
    }
}
