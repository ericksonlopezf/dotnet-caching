// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading.Tasks;
using EricksonLopez.Caching;

namespace EricksonLopez.Caching.AotSmokeTest;

public static class Program
{
    public static async Task<int> Main()
    {
        Console.WriteLine("[AOT Smoke Test] Starting Native AOT validation for EricksonLopez.Caching...");

        // 1. In-Memory Cache Smoke Test
        using var cache = new MemoryCacheProvider();
        const string testKey = "aot:smoke:key";
        const string testValue = "aot-payload-success";

        var setResult = await cache.SetAsync(testKey, testValue, CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(5)));
        if (!setResult.IsSuccess)
        {
            Console.Error.WriteLine("[AOT Smoke Test] SetAsync failed: " + setResult.Error.Description);
            return 1;
        }

        var getResult = await cache.GetAsync<string>(testKey);
        if (!getResult.IsSuccess || getResult.Value != testValue)
        {
            Console.Error.WriteLine("[AOT Smoke Test] GetAsync verification failed.");
            return 2;
        }

        // 2. Single-Flight Stampede Protection Smoke Test
        var stampedeResult = await cache.GetOrCreateAsync(
            "aot:stampede:key",
            ct => Task.FromResult(new TestPayload(42, "StampedeProtected")),
            CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(10)));

        if (!stampedeResult.IsSuccess || stampedeResult.Value.Id != 42)
        {
            Console.Error.WriteLine("[AOT Smoke Test] Stampede single-flight verification failed.");
            return 3;
        }

        // 3. Multi-Tenant Key Partitioning Smoke Test
        string currentTenant = "tenant-alpha";
        var tenantCache = new TenantPartitionedCacheProvider(cache, () => currentTenant);

        var tenantSet = await tenantCache.SetAsync("profile", "alpha-data");
        if (!tenantSet.IsSuccess)
        {
            Console.Error.WriteLine("[AOT Smoke Test] TenantPartitionedCache SetAsync failed.");
            return 4;
        }

        var tenantGet = await tenantCache.GetAsync<string>("profile");
        if (!tenantGet.IsSuccess || tenantGet.Value != "alpha-data")
        {
            Console.Error.WriteLine("[AOT Smoke Test] TenantPartitionedCache GetAsync failed.");
            return 5;
        }

        // 4. Source-Generated Serialization Context Smoke Test
        var serializer = new SystemTextJsonCacheSerializer(TestJsonContext.Default.Options);
        var serializedBytes = serializer.Serialize(new TestPayload(100, "AotSerialization"));
        var deserialized = serializer.Deserialize<TestPayload>(serializedBytes);
        if (deserialized is null || deserialized.Id != 100 || deserialized.Name != "AotSerialization")
        {
            Console.Error.WriteLine("[AOT Smoke Test] Source-generated serialization failed.");
            return 6;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[AOT Smoke Test] All Native AOT invariants successfully verified. PASS.");
        Console.ResetColor();
        return 0;
    }
}
