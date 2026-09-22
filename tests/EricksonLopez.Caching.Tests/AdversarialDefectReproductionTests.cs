// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Result;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Caching.Tests;

/// <summary>
/// Adversarial defect reproduction test suite proving real failure modes identified during audit.
/// </summary>
public sealed class AdversarialDefectReproductionTests
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Defect_TagLeakageAndPhantomEviction_Reproduce()
    {
        // Arrange
        var provider = new MemoryCacheProvider(_timeProvider);
        const string key = "order:999";

        // Step 1: Add key with two tags: tagA and tagB
        await provider.SetWithTagsAsync(key, "OriginalOrderData", ["tagA", "tagB"]);

        // Step 2: Remove by tagA
        var removeTagAResult = await provider.RemoveByTagAsync("tagA");
        removeTagAResult.Value.Should().BeTrue();

        // Step 3: Verify whether key was leaked in tagB's bucket
        var tagKeysField = typeof(MemoryCacheProvider).GetField("_tagKeys", BindingFlags.NonPublic | BindingFlags.Instance);
        var tagKeysDict = tagKeysField!.GetValue(provider) as IDictionary;
        tagKeysDict.Should().NotBeNull();

        var hasTagB = tagKeysDict!.Contains("tagB");
        hasTagB.Should().BeTrue();
        var tagBBucket = tagKeysDict["tagB"] as IDictionary;
        tagBBucket.Should().NotBeNull();

        // Check if key is still in tagB's bucket:
        var isLeakedInTagB = tagBBucket!.Contains(key);

        // Step 4: Re-insert the same key with an unrelated tagC (e.g. key is re-used or updated)
        await provider.SetWithTagsAsync(key, "NewOrderDataWithTagC", ["tagC"]);

        // Step 5: Now evict tagB. tagB should have NO relationship with the new order data!
        await provider.RemoveByTagAsync("tagB");

        // Step 6: Query the key. In fixed implementation, tagB invalidation does NOT evict key!
        var readResult = await provider.GetAsync<string>(key);

        // VERIFIED REMEDIATION ASSERTIONS:
        isLeakedInTagB.Should().BeFalse("VERIFIED REMEDIATION: Key was cleaned up from tagB bucket upon removing tagA");
        readResult.Value.Should().Be("NewOrderDataWithTagC", "VERIFIED REMEDIATION: Re-inserted key was NOT phantom-evicted by tagB");
    }

    [Fact]
    public void Defect_TenantPartitionedOptions_TypeSlicing_VerifiedRemediation()
    {
        // Arrange
        var underlying = Substitute.For<ICacheProvider>();
        var partitioned = new TenantPartitionedCacheProvider(underlying, () => "tenant_test");

        var hybridOptions = new HybridCacheEntryOptions
        {
            LocalCacheDuration = TimeSpan.FromSeconds(30),
            DistributedCacheDuration = TimeSpan.FromHours(2),
            Tags = ["catalog_tag"]
        };

        // Act - inspect PartitionOptions via reflection
        var partitionOptionsMethod = typeof(TenantPartitionedCacheProvider)
            .GetMethod("PartitionOptions", BindingFlags.NonPublic | BindingFlags.Instance);

        var resultOptions = partitionOptionsMethod!.Invoke(partitioned, [hybridOptions]) as CacheEntryOptions;

        // Assert - VERIFIED REMEDIATION: The resulting options retains HybridCacheEntryOptions with local & distributed durations intact!
        resultOptions.Should().NotBeNull();
        (resultOptions is HybridCacheEntryOptions).Should().BeTrue(
            "VERIFIED REMEDIATION: PartitionOptions preserves HybridCacheEntryOptions when Tags are present");

        var hybridResult = (HybridCacheEntryOptions)resultOptions!;
        hybridResult.LocalCacheDuration.Should().Be(TimeSpan.FromSeconds(30));
        hybridResult.DistributedCacheDuration.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public async Task Defect_HybridCacheProvider_RemoveAsync_StrictInvalidation_FailsWhenL2Fails()
    {
        // Arrange
        var local = Substitute.For<ICacheProvider, ITaggedCacheProvider>();
        var distributed = Substitute.For<ICacheProvider, ITaggedCacheProvider>();
        // Explicit strict invalidation (failOpenOnRemoval = false)
        var hybrid = new HybridCacheProvider(local, distributed, failOpenOnRemoval: false);

        // Distributed cache fails to remove (e.g. Redis exception or network failure)
        distributed.RemoveAsync("critical_key", Arg.Any<System.Threading.CancellationToken>())
            .Returns<Task<Result<bool>>>(_ => throw new InvalidOperationException("Redis connection timed out"));

        local.RemoveAsync("critical_key", Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(Result<bool>.Success(true)));

        // Act
        var removeResult = await hybrid.RemoveAsync("critical_key");

        // Assert: VERIFIED REMEDIATION: With failOpenOnRemoval = false, RemoveAsync reports failure when L2 fails!
        removeResult.IsFailure.Should().BeTrue(
            "VERIFIED REMEDIATION: RemoveAsync fails when L2 removal fails, preventing silent lost deletion");
        removeResult.Error.Code.Should().Be("Cache.Hybrid.L2RemovalFailed");
        await local.Received(1).RemoveAsync("critical_key", Arg.Any<System.Threading.CancellationToken>());
    }

    [Fact]
    public async Task Defect_MemoryCacheProvider_EmptyTagBuckets_ArePrunedOnKeyRemoval()
    {
        // Arrange
        var provider = new MemoryCacheProvider(_timeProvider);
        const string ephemeralTag = "session_ephemeral_tag";
        const string ephemeralKey = "session:user:123";

        await provider.SetWithTagsAsync(ephemeralKey, "UserData", [ephemeralTag]);

        // Inspect internal _tagKeys
        var tagKeysField = typeof(MemoryCacheProvider).GetField("_tagKeys", BindingFlags.NonPublic | BindingFlags.Instance);
        var tagKeysDict = tagKeysField!.GetValue(provider) as IDictionary;
        tagKeysDict.Should().NotBeNull();
        tagKeysDict!.Contains(ephemeralTag).Should().BeTrue();

        // Act: remove the sole key tagged with ephemeralTag and sweep expired/empty tag buckets
        var removeRes = await provider.RemoveAsync(ephemeralKey);
        removeRes.IsSuccess.Should().BeTrue();

        provider.RemoveExpiredEntries();

        // Assert: VERIFIED REMEDIATION (MED-02): the empty tag dictionary is purged from _tagKeys during sweep!
        tagKeysDict.Contains(ephemeralTag).Should().BeFalse(
            "VERIFIED REMEDIATION (MED-02): Empty tag dictionaries are purged to prevent unbounded dictionary growth");
    }

    [Fact]
    public async Task Defect_TagAssociation_RetainedOnKeyOverwrite_DemonstrateFix()
    {
        // Arrange
        var provider = new MemoryCacheProvider(_timeProvider);
        const string key = "user:42";

        // Step 1: User 42 is tagged with "role:admin"
        await provider.SetWithTagsAsync(key, "AdminData", ["role:admin"]);

        // Step 2: User 42 is updated with an unrelated tag "role:member" (or without tags)
        await provider.SetWithTagsAsync(key, "MemberData", ["role:member"]);

        // Step 3: Evict all entries tagged with "role:admin"
        await provider.RemoveByTagAsync("role:admin");

        // Step 4: Query User 42. It should NOT be phantom-evicted because it is no longer tagged with "role:admin"!
        var result = await provider.GetAsync<string>(key);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("MemberData",
            "VERIFIED REMEDIATION: Key overwritten with new tags must disassociate from prior tags to prevent phantom eviction");
    }
}

