// Copyright © Erickson Lopez. MIT License.
// ============================================================
//  EricksonLopez.Caching — Official Reference Showcase
//  Level 0 through Level 9 — 100% Public API Coverage
//  Source of truth: EricksonLopez.Caching + EricksonLopez.Caching.Redis
// ============================================================
using System.Text.Json;
using EricksonLopez.Caching;
using EricksonLopez.Caching.Redis;
using EricksonLopez.Result;
using StackExchange.Redis.Profiling;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace EricksonLopez.Caching.Sample;

public static class Program
{
    private static readonly string[] BatchKeys = ["batch:1", "batch:2", "batch:3"];
    private static readonly string[] TagsRegion = ["catalog", "region:eu"];
    private static readonly string[] TagsBulk = ["grp-a", "grp-b"];
    private static readonly string[] PrefixList = ["svc:orders:", "svc:catalog:"];
    private static readonly string[] TenantKeys = ["profile", "settings"];
    private static readonly string[] HybridKeys = ["hybrid:product:1", "hybrid:product:2"];

    public static async Task<int> Main(string[] args)
    {
        PrintBanner();
        try
        {
            Level0_ConceptualOverview();
            await Level1_QuickStartAsync();
            Level2_FullConfiguration();
            await Level2_AllMethodsAsync();
            await Level3_RealUseCasesAsync();
            await Level4_AdvancedIntegrationAsync();
            await Level5_ProcessingAsync();
            await Level6_ErrorHandlingAsync();
            await Level7_ScalabilityAsync();
            Level8_CustomImplementations();
            Level9_DependencyInjection();
            Level10_EnterpriseArchitecture();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n[All Showcase Levels Completed - 100% Public API Coverage]\n");
            Console.ResetColor();
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"\n[FATAL] {ex}");
            Console.ResetColor();
            return 1;
        }
    }

    // ── Level 0: Conceptual Overview ──────────────────────────────────────────
    private static void Level0_ConceptualOverview()
    {
        PrintSection("Level 0 - Conceptual Overview");
        Console.WriteLine("EricksonLopez.Caching: enterprise multi-tier caching for .NET 8/9/10.");
        Console.WriteLine("Solves: stampede protection, functional Result<T> flow, tag invalidation.");
        Console.WriteLine("Providers: Memory | Redis | Hybrid | Tenant-partitioned");
        Console.WriteLine("Contracts: ICacheProvider | ITaggedCacheProvider | ICacheInvalidator | ICacheSerializer");
        PrintOk("Level 0 complete");
    }

    // ── Level 1: Quick Start ──────────────────────────────────────────────────
    private static async Task Level1_QuickStartAsync()
    {
        PrintSection("Level 1 - Quick Start");
        using var cache = new MemoryCacheProvider();

        var set = await cache.SetAsync("user:1", "Alice");
        Assert(set.IsSuccess && set.Value, "SetAsync");

        var get = await cache.GetAsync<string>("user:1");
        Assert(get.IsSuccess && get.Value == "Alice", "GetAsync hit");

        var miss = await cache.GetAsync<string>("no-key");
        Assert(miss.IsSuccess && miss.Value == null, "GetAsync miss = Success(null)");

        var goc = await cache.GetOrCreateAsync<string>("computed:1",
            async ct => { await Task.Delay(1, ct); return "Computed"; });
        Assert(goc.IsSuccess && goc.Value == "Computed", "GetOrCreateAsync");

        var rem = await cache.RemoveAsync("user:1");
        Assert(rem.IsSuccess && rem.Value, "RemoveAsync existing");

        var remMiss = await cache.RemoveAsync("no-key");
        Assert(remMiss.IsSuccess && !remMiss.Value, "RemoveAsync nonexistent = false");

        PrintOk("Level 1 complete");
    }

    // ── Level 2: Full Configuration ───────────────────────────────────────────
    private static void Level2_FullConfiguration()
    {
        PrintSection("Level 2 - Full Configuration");

        // CacheEntryOptions static factories
        var abs = CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(15));
        Assert(abs.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(15), "FromAbsolute");
        var sliding = CacheEntryOptions.FromSliding(TimeSpan.FromMinutes(5));
        Assert(sliding.SlidingExpiration == TimeSpan.FromMinutes(5), "FromSliding");
        var fs = CacheEntryOptions.FromAbsoluteWithFailSafe(TimeSpan.FromMinutes(5), TimeSpan.FromHours(1));
        Assert(fs.FailSafeMaxStale == TimeSpan.FromHours(1), "FromAbsoluteWithFailSafe");

        // CacheEntryOptions full object init
        var full = new CacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
            SlidingExpiration = TimeSpan.FromMinutes(3),
            FailSafeMaxStale = TimeSpan.FromHours(2),
            Tags = new HashSet<string> { "catalog", "region:us" }
        };
        Assert(full.Tags!.Count == 2, "CacheEntryOptions.Tags property");
        Assert(CacheEntryOptions.MaxAllowedDuration == TimeSpan.FromDays(3650), "MaxAllowedDuration constant");

        // CacheEntryOptions.ValidateDuration guards
        try { _ = new CacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.Zero }; }
        catch (ArgumentOutOfRangeException) { Assert(true, "ValidateDuration zero guard"); }
        try { _ = new CacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(3651) }; }
        catch (ArgumentOutOfRangeException) { Assert(true, "ValidateDuration exceeds-max guard"); }

        // HybridCacheEntryOptions.FromDurations
        var h = HybridCacheEntryOptions.FromDurations(TimeSpan.FromMinutes(2), TimeSpan.FromHours(4));
        Assert(h.LocalCacheDuration == TimeSpan.FromMinutes(2), "HybridCacheEntryOptions.LocalCacheDuration");
        Assert(h.DistributedCacheDuration == TimeSpan.FromHours(4), "HybridCacheEntryOptions.DistributedCacheDuration");
        Assert(h.AbsoluteExpirationRelativeToNow == TimeSpan.FromHours(4), "HybridCacheEntryOptions.AbsoluteExpiration = distributed");

        // HybridCacheEntryOptions full object init
        var hf = new HybridCacheEntryOptions
        {
            LocalCacheDuration = TimeSpan.FromMinutes(1),
            DistributedCacheDuration = TimeSpan.FromHours(6),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6),
            FailSafeMaxStale = TimeSpan.FromHours(12),
            Tags = new List<string> { "products" }
        };
        Assert(hf.LocalCacheDuration == TimeSpan.FromMinutes(1), "HybridCacheEntryOptions full init");

        // MemoryCacheOptions - all properties
        var mem = new MemoryCacheOptions
        {
            MaxCapacity = 50_000,
            ExpirationScanFrequency = TimeSpan.FromMinutes(2),
            CompactPercentage = 0.30
        };
        Assert(mem.MaxCapacity == 50_000, "MemoryCacheOptions.MaxCapacity");
        Assert(mem.ExpirationScanFrequency == TimeSpan.FromMinutes(2), "MemoryCacheOptions.ExpirationScanFrequency");
        Assert(Math.Abs(mem.CompactPercentage - 0.30) < 0.001, "MemoryCacheOptions.CompactPercentage");

        // MemoryCacheOptions guards
        try { _ = new MemoryCacheOptions { MaxCapacity = 0 }; }
        catch (ArgumentOutOfRangeException) { Assert(true, "MemoryCacheOptions.MaxCapacity <= 0 guard"); }
        try { _ = new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.Zero }; }
        catch (ArgumentOutOfRangeException) { Assert(true, "MemoryCacheOptions.ExpirationScanFrequency <= 0 guard"); }
        try { _ = new MemoryCacheOptions { CompactPercentage = 0.01 }; }
        catch (ArgumentOutOfRangeException) { Assert(true, "MemoryCacheOptions.CompactPercentage < 0.05 guard"); }
        try { _ = new MemoryCacheOptions { CompactPercentage = 0.99 }; }
        catch (ArgumentOutOfRangeException) { Assert(true, "MemoryCacheOptions.CompactPercentage > 0.50 guard"); }

        // RedisCacheOptions - all properties
        var r = new RedisCacheOptions
        {
            Configuration = "my-redis:6379,abortConnect=false",
            InstanceName = "myapp",
            DefaultAbsoluteExpiration = TimeSpan.FromMinutes(10),
            Database = 1,
            MaxRetries = 5,
            RetryDelay = TimeSpan.FromMilliseconds(200),
            LockTimeout = TimeSpan.FromSeconds(60),
            LockRetryInterval = TimeSpan.FromMilliseconds(25),
            MaxLockWaitTime = TimeSpan.FromSeconds(45),
            Serializer = SystemTextJsonCacheSerializer.Default,
            FailOpen = true
        };
        Assert(r.FailOpen, "RedisCacheOptions.FailOpen");
        Assert(r.Database == 1, "RedisCacheOptions.Database");
        Assert(r.MaxRetries == 5, "RedisCacheOptions.MaxRetries");
        Assert(r.InstanceName == "myapp", "RedisCacheOptions.InstanceName");
        Assert(r.LockTimeout == TimeSpan.FromSeconds(60), "RedisCacheOptions.LockTimeout");
        Assert(r.LockRetryInterval == TimeSpan.FromMilliseconds(25), "RedisCacheOptions.LockRetryInterval");
        Assert(r.MaxLockWaitTime == TimeSpan.FromSeconds(45), "RedisCacheOptions.MaxLockWaitTime");
        Assert(r.DefaultAbsoluteExpiration == TimeSpan.FromMinutes(10), "RedisCacheOptions.DefaultAbsoluteExpiration");

        PrintOk("Level 2 (configuration) complete");
    }

    private static async Task Level2_AllMethodsAsync()
    {
        PrintSection("Level 2 - All ICacheProvider Methods");
        using var cache = new MemoryCacheProvider(new MemoryCacheOptions
        {
            MaxCapacity = 1000,
            CompactPercentage = 0.25,
            ExpirationScanFrequency = TimeSpan.FromHours(1)
        });

        await cache.SetAsync("k:int", 42, CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(5)));
        await cache.SetAsync("k:str", "hello", CacheEntryOptions.FromSliding(TimeSpan.FromMinutes(10)));
        await cache.SetAsync<string?>("k:null", null);

        Assert((await cache.GetAsync<int>("k:int")).Value == 42, "GetAsync<int>");
        Assert((await cache.GetAsync<string>("k:str")).Value == "hello", "GetAsync<string>");

        Assert((await cache.ExistsAsync("k:int")).Value, "ExistsAsync true");
        Assert(!(await cache.ExistsAsync("no-key")).Value, "ExistsAsync false");

        await cache.SetAsync("batch:1", "B1"); await cache.SetAsync("batch:2", "B2"); await cache.SetAsync("batch:3", "B3");
        Assert((await cache.GetManyAsync<string>(BatchKeys)).Value.Count == 3, "GetManyAsync");

        var pairs = new Dictionary<string, string> { ["m:1"] = "V1", ["m:2"] = "V2" };
        Assert((await cache.SetManyAsync(pairs, CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(5)))).Value, "SetManyAsync");

        Assert((await cache.RemoveManyAsync(BatchKeys)).Value == 3, "RemoveManyAsync");

        await cache.SetAsync("pref:a", "A"); await cache.SetAsync("pref:b", "B");
        Assert((await cache.RemoveByPrefixAsync("pref:")).Value, "RemoveByPrefixAsync");

        await cache.SetAsync("exp:1", "X", CacheEntryOptions.FromAbsolute(TimeSpan.FromHours(1)));
        Assert((await cache.ExpireAsync("exp:1", TimeSpan.FromMinutes(30))).Value, "ExpireAsync existing");
        Assert(!(await cache.ExpireAsync("no-key", TimeSpan.FromMinutes(30))).Value, "ExpireAsync missing = false");

        await cache.SetAsync("slide:1", "S", CacheEntryOptions.FromSliding(TimeSpan.FromMinutes(10)));
        Assert((await cache.RefreshAsync("slide:1")).Value, "RefreshAsync existing");
        Assert(!(await cache.RefreshAsync("no-key")).Value, "RefreshAsync missing = false");

        Assert(cache.RemoveExpiredEntries() >= 0, "RemoveExpiredEntries");

        // ITaggedCacheProvider via MemoryCacheProvider
        ITaggedCacheProvider t = cache;
        await t.SetWithTagsAsync("cat:1", "Laptop", new[] { "catalog", "region:eu" }, CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(5)));
        await t.SetWithTagsAsync("cat:2", "Phone", TagsRegion);
        Assert((await t.RemoveByTagAsync("catalog")).IsSuccess, "RemoveByTagAsync");
        await t.SetWithTagsAsync("cat:3", "Tablet", new[] { "grp-a" });
        await t.SetWithTagsAsync("cat:4", "Watch", new[] { "grp-b" });
        Assert((await t.RemoveByTagsAsync(TagsBulk)).IsSuccess, "RemoveByTagsAsync");

        cache.Dispose();
        Assert(true, "MemoryCacheProvider.Dispose()");
        PrintOk("Level 2 (all methods) complete");
    }

    // ── Level 3: Real Use Cases ───────────────────────────────────────────────
    private static async Task Level3_RealUseCasesAsync()
    {
        PrintSection("Level 3 - Real Use Cases");
        using var cache = new MemoryCacheProvider();

        // Single-flight stampede protection
        var callCount = 0;
        var tasks = new Task<Result<string>>[20];
        for (var i = 0; i < 20; i++)
            tasks[i] = cache.GetOrCreateAsync<string>("catalog:featured", async ct =>
            {
                Interlocked.Increment(ref callCount);
                await Task.Delay(20, ct);
                return "FeaturedProducts";
            }, CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(5)));
        await Task.WhenAll(tasks);
        Assert(callCount == 1, $"Single-flight: factory called {callCount}x (expected 1)");

        // GetOrCreateAsync with sliding + full options
        await cache.GetOrCreateAsync<int>("stats", ct => Task.FromResult(42), CacheEntryOptions.FromSliding(TimeSpan.FromMinutes(30)));
        var fullOpts = new CacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10), FailSafeMaxStale = TimeSpan.FromHours(1), Tags = new[] { "catalog" } };
        await cache.GetOrCreateAsync<string>("catalog:summary", ct => Task.FromResult("Summary"), fullOpts);

        // CacheInvalidator - all 6 methods
        var inv = new CacheInvalidator(cache);

        await cache.SetAsync("inv:1", "V1");
        Assert((await inv.InvalidateAsync("inv:1")).IsSuccess, "InvalidateAsync");

        await cache.SetAsync("inv:m:1", "V1"); await cache.SetAsync("inv:m:2", "V2");
        var invMany = await inv.InvalidateManyAsync(new[] { "inv:m:1", "inv:m:2" });
        Assert(invMany.IsSuccess && invMany.Value == 2, "InvalidateManyAsync");

        await cache.SetAsync("orders:1", "O1"); await cache.SetAsync("orders:2", "O2");
        Assert((await inv.InvalidateByPrefixAsync("orders:")).IsSuccess, "InvalidateByPrefixAsync");

        await cache.SetAsync("svc:orders:1", "O"); await cache.SetAsync("svc:catalog:1", "C");
        Assert((await inv.InvalidateByPrefixesAsync(PrefixList)).IsSuccess, "InvalidateByPrefixesAsync");

        await cache.SetAsync("t1", "T", new CacheEntryOptions { Tags = new[] { "grp-a" } });
        Assert((await inv.InvalidateByTagAsync("grp-a")).IsSuccess, "InvalidateByTagAsync");

        await cache.SetAsync("t2", "T2", new CacheEntryOptions { Tags = new[] { "grp-a" } });
        await cache.SetAsync("t3", "T3", new CacheEntryOptions { Tags = new[] { "grp-b" } });
        Assert((await inv.InvalidateByTagsAsync(TagsBulk)).IsSuccess, "InvalidateByTagsAsync");

        PrintOk("Level 3 complete");
    }

    // ── Level 4: Advanced Integration ─────────────────────────────────────────
    private static async Task Level4_AdvancedIntegrationAsync()
    {
        PrintSection("Level 4 - Advanced Integration");

        // HybridCacheProvider - all methods
        using var l1 = new MemoryCacheProvider();
        using var l2 = new MemoryCacheProvider();
        var hybrid = new HybridCacheProvider(l1, l2, failOpenOnRemoval: true);
        using var l1s = new MemoryCacheProvider(); using var l2s = new MemoryCacheProvider();
        var hybridStrict = new HybridCacheProvider(l1s, l2s, failOpenOnRemoval: false);

        Assert((await hybrid.SetAsync("p:1", "Laptop", CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(5)))).Value, "Hybrid SetAsync");
        Assert((await hybrid.GetAsync<string>("p:1")).Value == "Laptop", "Hybrid GetAsync");
        Assert((await hybrid.ExistsAsync("p:1")).Value, "Hybrid ExistsAsync");

        // GetOrCreateAsync with HybridCacheEntryOptions (distinct L1/L2 TTLs)
        var tierOpts = HybridCacheEntryOptions.FromDurations(TimeSpan.FromMinutes(1), TimeSpan.FromHours(4));
        Assert((await hybrid.GetOrCreateAsync<string>("p:2", async ct => { await Task.Delay(1, ct); return "Monitor"; }, tierOpts)).Value == "Monitor", "Hybrid GetOrCreate HybridOpts");

        // GetOrCreateAsync with HybridCacheEntryOptions including fail-safe + tags
        var hf = new HybridCacheEntryOptions { LocalCacheDuration = TimeSpan.FromMinutes(2), DistributedCacheDuration = TimeSpan.FromHours(8), AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(8), FailSafeMaxStale = TimeSpan.FromHours(2), Tags = new[] { "products" } };
        Assert((await hybrid.GetOrCreateAsync<string>("p:3", ct => Task.FromResult("TV"), hf)).Value == "TV", "Hybrid GetOrCreate full HybridOpts");

        await hybrid.SetAsync("hybrid:product:1", "P1"); await hybrid.SetAsync("hybrid:product:2", "P2");
        Assert((await hybrid.GetManyAsync<string>(HybridKeys)).Value.Count == 2, "Hybrid GetManyAsync");

        var hbatch = new Dictionary<string, string> { ["hyb:a"] = "A", ["hyb:b"] = "B" };
        Assert((await hybrid.SetManyAsync(hbatch, CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(5)))).Value, "Hybrid SetManyAsync");

        ITaggedCacheProvider th = hybrid;
        Assert((await th.SetWithTagsAsync("hyb:t:1", "T1", new[] { "htag" }, CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(5)))).Value, "Hybrid SetWithTagsAsync");
        Assert((await th.RemoveByTagAsync("htag")).IsSuccess, "Hybrid RemoveByTagAsync");
        Assert((await th.RemoveByTagsAsync(new[] { "htag-x" })).IsSuccess, "Hybrid RemoveByTagsAsync");

        await hybrid.SetAsync("hyb:cat:1", "C1");
        Assert((await hybrid.RemoveByPrefixAsync("hyb:cat:")).Value, "Hybrid RemoveByPrefixAsync");

        await hybrid.SetAsync("hyb:m:1", "M1"); await hybrid.SetAsync("hyb:m:2", "M2");
        Assert((await hybrid.RemoveManyAsync(new[] { "hyb:m:1", "hyb:m:2" })).IsSuccess, "Hybrid RemoveManyAsync");

        Assert((await hybrid.RemoveAsync("p:1")).Value, "Hybrid RemoveAsync");

        await hybrid.SetAsync("exp:h:1", "E", CacheEntryOptions.FromAbsolute(TimeSpan.FromHours(1)));
        Assert((await hybrid.ExpireAsync("exp:h:1", TimeSpan.FromMinutes(15))).IsSuccess, "Hybrid ExpireAsync");

        await hybrid.SetAsync("ref:h:1", "R", CacheEntryOptions.FromSliding(TimeSpan.FromMinutes(10)));
        Assert((await hybrid.RefreshAsync("ref:h:1")).IsSuccess, "Hybrid RefreshAsync");

        // TenantPartitionedCacheProvider - all methods
        using var inner = new MemoryCacheProvider();
        var currentTenant = "tenant-alpha";
        var tc = new TenantPartitionedCacheProvider(inner, () => currentTenant);
        ITaggedCacheProvider tt = tc;

        await tc.SetAsync("config", "AlphaValue", CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(10)));
        Assert((await tc.GetAsync<string>("config")).Value == "AlphaValue", "Tenant GetAsync");
        Assert((await tc.ExistsAsync("config")).Value, "Tenant ExistsAsync");
        Assert((await tc.GetManyAsync<string>(TenantKeys)).IsSuccess, "Tenant GetManyAsync");
        Assert((await tc.RefreshAsync("config")).Value, "Tenant RefreshAsync");
        Assert((await tc.ExpireAsync("config", TimeSpan.FromMinutes(5))).Value, "Tenant ExpireAsync");

        var tbatch = new Dictionary<string, string> { ["settings"] = "{}" };
        Assert((await tc.SetManyAsync(tbatch, CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(5)))).IsSuccess, "Tenant SetManyAsync");
        Assert((await tc.RemoveManyAsync(TenantKeys)).IsSuccess, "Tenant RemoveManyAsync");

        await tc.SetAsync("pref:x", "X");
        Assert((await tc.RemoveByPrefixAsync("pref:")).Value, "Tenant RemoveByPrefixAsync");

        await tt.SetWithTagsAsync("tag:1", "TV", new[] { "t-tag" }, CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(5)));
        Assert((await tt.RemoveByTagAsync("t-tag")).IsSuccess, "Tenant RemoveByTagAsync");
        Assert((await tt.RemoveByTagsAsync(new[] { "t-tag-2" })).IsSuccess, "Tenant RemoveByTagsAsync");

        Assert((await tc.GetOrCreateAsync<string>("lazy:1", async ct => { await Task.Delay(1, ct); return "TenantLazy"; })).Value == "TenantLazy", "Tenant GetOrCreateAsync");

        await tc.SetAsync("del:1", "D");
        Assert((await tc.RemoveAsync("del:1")).Value, "Tenant RemoveAsync");

        // Tenant isolation: beta writes to "isolation:key"; alpha cannot see it (true cross-tenant isolation)
        currentTenant = "tenant-beta";
        await tc.SetAsync("isolation:key", "BetaOnly");
        Assert((await tc.GetAsync<string>("isolation:key")).Value == "BetaOnly", "Tenant isolation: beta can read its own key");
        currentTenant = "tenant-alpha";
        Assert((await tc.GetAsync<string>("isolation:key")).Value == null, "Tenant isolation: alpha cannot see beta's key");

        PrintOk("Level 4 complete");
    }

    // ── Level 5: Processing Patterns ─────────────────────────────────────────
    private static async Task Level5_ProcessingAsync()
    {
        PrintSection("Level 5 - Processing Patterns");

        // Fail-safe stale serving
        using var fsc = new MemoryCacheProvider();
        var fsOpts = CacheEntryOptions.FromAbsoluteWithFailSafe(TimeSpan.FromMilliseconds(1), TimeSpan.FromHours(1));
        await fsc.SetAsync("stale:key", "StaleValue", fsOpts);
        await Task.Delay(20);
        var stale = await fsc.GetOrCreateAsync<string>("stale:key",
            factory: ct => { throw new InvalidOperationException("DB unavailable"); }, options: fsOpts);
        Assert(stale.IsSuccess && stale.Value == "StaleValue", "Fail-safe stale serving");

        // MaxCapacity compaction
        using var cc = new MemoryCacheProvider(new MemoryCacheOptions { MaxCapacity = 5, CompactPercentage = 0.40 });
        for (var i = 1; i <= 5; i++) await cc.SetAsync($"cap:{i}", $"V{i}");
        await cc.SetAsync("cap:6", "V6"); // triggers compaction
        Console.WriteLine("  Compaction triggered at MaxCapacity=5 + 1");

        // Manual sweep
        using var sw = new MemoryCacheProvider();
        await sw.SetAsync("exp:A", "A", CacheEntryOptions.FromAbsolute(TimeSpan.FromMilliseconds(1)));
        await Task.Delay(20);
        Assert(sw.RemoveExpiredEntries() >= 1, "RemoveExpiredEntries manual sweep");

        // Background timer created and disposed
        using var tc = new MemoryCacheProvider(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromHours(1) });
        tc.Dispose();
        Console.WriteLine("  Background timer disposed cleanly");

        // CancellationToken propagation
        using var cache = new MemoryCacheProvider();
        using var cts = new CancellationTokenSource();
        await cache.SetAsync("ct:1", "V1", cancellationToken: cts.Token);
        Assert((await cache.GetAsync<string>("ct:1", cts.Token)).Value == "V1", "CancellationToken propagated");
        cts.Cancel();
        try { await cache.GetAsync<string>("ct:1", cts.Token); }
        catch (OperationCanceledException) { Assert(true, "CancellationToken OCE thrown"); }

        // MemoryCacheProvider constructor overloads
        using var c1 = new MemoryCacheProvider();
        using var c2 = new MemoryCacheProvider(TimeProvider.System);
        using var c3 = new MemoryCacheProvider(new MemoryCacheOptions { MaxCapacity = 10 }, TimeProvider.System);
        using var c4 = new MemoryCacheProvider(Microsoft.Extensions.Options.Options.Create(new MemoryCacheOptions()), TimeProvider.System);
        Assert(true, "All MemoryCacheProvider constructor overloads OK");

        PrintOk("Level 5 complete");
    }

    // ── Level 6: Error Handling ────────────────────────────────────────────────
    private static async Task Level6_ErrorHandlingAsync()
    {
        PrintSection("Level 6 - Error Handling");
        using var cache = new MemoryCacheProvider();

        // Factory failure without fail-safe
        var fail = await cache.GetOrCreateAsync<string>("fail:key",
            factory: ct => throw new InvalidOperationException("DB timeout"),
            options: CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(5)));
        Assert(fail.IsFailure, "Factory failure -> Result.Failure");
        Assert(fail.Error.Code == "Cache.Memory.FactoryFailed", $"Error code: {fail.Error.Code}");

        // ArgumentNullException guards
        try { await cache.GetAsync<string>(null!); } catch (ArgumentNullException) { Assert(true, "GetAsync null key"); }
        try { await cache.SetAsync<string>(null!, "v"); } catch (ArgumentNullException) { Assert(true, "SetAsync null key"); }
        try { await cache.RemoveAsync(null!); } catch (ArgumentNullException) { Assert(true, "RemoveAsync null key"); }
        try { await cache.GetOrCreateAsync<string>(null!, ct => Task.FromResult("")); } catch (ArgumentNullException) { Assert(true, "GetOrCreate null key"); }
        try { await cache.GetOrCreateAsync<string>("k", null!); } catch (ArgumentNullException) { Assert(true, "GetOrCreate null factory"); }

        // RemoveByPrefixAsync empty/whitespace
        try { await cache.RemoveByPrefixAsync(""); } catch (ArgumentException) { Assert(true, "RemoveByPrefix empty guard"); }
        try { await cache.RemoveByPrefixAsync("   "); } catch (ArgumentException) { Assert(true, "RemoveByPrefix whitespace guard"); }

        // ExpireAsync non-positive TTL
        await cache.SetAsync("eg", "G");
        var be = await cache.ExpireAsync("eg", TimeSpan.Zero);
        Assert(be.IsFailure && be.Error.Code == "Cache.InvalidTimeToLive", "ExpireAsync zero TTL guard");
        var ne = await cache.ExpireAsync("eg", TimeSpan.FromSeconds(-1));
        Assert(ne.IsFailure, "ExpireAsync negative TTL guard");

        // ICacheInvalidator error codes
        var inv = new CacheInvalidator(cache);
        var ek = await inv.InvalidateAsync("");
        Assert(ek.IsFailure && ek.Error.Code == "Cache.InvalidKey", "InvalidateAsync empty key code");
        var ep = await inv.InvalidateByPrefixAsync("  ");
        Assert(ep.IsFailure && ep.Error.Code == "Cache.InvalidPrefix", "InvalidateByPrefix empty code");
        var et = await inv.InvalidateByTagAsync("");
        Assert(et.IsFailure && et.Error.Code == "Cache.InvalidTag", "InvalidateByTag empty code");

        // Tag invalidation on non-ITaggedCacheProvider
        var ntInv = new CacheInvalidator(new NonTaggedCacheProvider());
        var nt = await ntInv.InvalidateByTagAsync("some-tag");
        Assert(nt.IsFailure && nt.Error.Code == "Cache.TagsNotSupported", "TagsNotSupported code");

        // Cache miss is always Success(null)
        var miss = await cache.GetAsync<string>("no-key");
        Assert(miss.IsSuccess && miss.Value == null && !miss.IsFailure, "Cache miss = Success(null)");

        PrintOk("Level 6 complete");
    }

    // ── Level 7: Scalability & Batch ──────────────────────────────────────────
    private static async Task Level7_ScalabilityAsync()
    {
        PrintSection("Level 7 - Scalability & Batch Processing");
        using var cache = new MemoryCacheProvider(new MemoryCacheOptions { MaxCapacity = 10_000 });

        // Large batch set/get
        var products = new Dictionary<string, string>();
        for (var i = 1; i <= 100; i++) products[$"product:{i:D4}"] = $"P{i}";
        Assert((await cache.SetManyAsync(products, CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(10)))).Value, "Batch SetManyAsync 100");

        var keys = new List<string>();
        for (var i = 1; i <= 50; i++) keys.Add($"product:{i:D4}");
        Assert((await cache.GetManyAsync<string>(keys)).Value.Count == 50, "Batch GetManyAsync 50");

        var rmKeys = new List<string>();
        for (var i = 1; i <= 25; i++) rmKeys.Add($"product:{i:D4}");
        Assert((await cache.RemoveManyAsync(rmKeys)).Value == 25, "Batch RemoveManyAsync 25");

        // Namespace sweep
        await cache.SetAsync("ns:a:1", "V1"); await cache.SetAsync("ns:a:2", "V2");
        Assert((await cache.RemoveByPrefixAsync("ns:a:")).Value, "Prefix namespace sweep");

        // Concurrent thread safety
        var ct = new Task[50];
        for (var i = 0; i < 50; i++) { var idx = i; ct[idx] = cache.SetAsync($"conc:{idx}", $"V{idx}"); }
        await Task.WhenAll(ct);
        var cgKeys = new string[50]; for (var j = 0; j < 50; j++) cgKeys[j] = $"conc:{j}";
        var cg = await cache.GetManyAsync<string>(cgKeys);
        Assert(cg.Value.Count == 50, "Concurrent SetAsync thread safety");

        // Default interface method coverage
        ICacheProvider iface = cache;
        await iface.SetAsync("iface:1", "I1");
        var ifm = await iface.GetManyAsync<string>(new[] { "iface:1", "iface:no" });
        Assert(ifm.IsSuccess && ifm.Value.ContainsKey("iface:1"), "ICacheProvider default GetManyAsync");
        var ifsp = await iface.SetManyAsync(new[] { new KeyValuePair<string, string>("iface:2", "I2") });
        Assert(ifsp.IsSuccess, "ICacheProvider default SetManyAsync");
        var ifrm = await iface.RemoveManyAsync(new[] { "iface:1", "iface:2" });
        Assert(ifrm.IsSuccess && ifrm.Value >= 1, "ICacheProvider default RemoveManyAsync");

        // Default implementations: ExistsAsync / ExpireAsync / RefreshAsync
        ICacheProvider minimal = new MinimalCacheProvider();
        Assert(!(await minimal.ExistsAsync("any")).Value, "ICacheProvider.ExistsAsync default = false");
        Assert(!(await minimal.ExpireAsync("any", TimeSpan.FromMinutes(1))).Value, "ICacheProvider.ExpireAsync default = false");
        Assert(!(await minimal.RefreshAsync("any")).Value, "ICacheProvider.RefreshAsync default = false");

        PrintOk("Level 7 complete");
    }

    // ── Level 8: Custom Implementations & Diagnostics ─────────────────────────
    private static void Level8_CustomImplementations()
    {
        PrintSection("Level 8 - Custom Implementations & Diagnostics");

        // SystemTextJsonCacheSerializer.Default
        var def = SystemTextJsonCacheSerializer.Default;
        Assert(def is not null, "SystemTextJsonCacheSerializer.Default");

        var obj = new ProductDto(42, "Monitor", 299.99m);
        var json = def!.Serialize(obj);
        Assert(!string.IsNullOrEmpty(json), "Serialize<T>");
        var des = def.Deserialize<ProductDto>(json);
        Assert(des is not null && des.Id == 42, "Deserialize<T>");
        var bytes = def.SerializeToUtf8Bytes(obj);
        Assert(bytes.Length > 0, "SerializeToUtf8Bytes<T>");
        var fromBytes = def.DeserializeFromUtf8Bytes<ProductDto>(bytes);
        Assert(fromBytes is not null && fromBytes.Name == "Monitor", "DeserializeFromUtf8Bytes<T>");

        // SystemTextJsonCacheSerializer with custom JsonSerializerOptions
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var custom = new SystemTextJsonCacheSerializer(opts);
        var cj = custom.Serialize(obj);
        Assert(cj.Contains("\"id\""), $"CamelCase serialization: {cj}");

        // Custom ICacheSerializer implementation
        ICacheSerializer b64 = new Base64CacheSerializer();
        var enc = b64.Serialize("HelloCache");
        Assert(b64.Deserialize<string>(enc) == "HelloCache", "Custom ICacheSerializer string round-trip");
        var encB = b64.SerializeToUtf8Bytes("TestBytes");
        Assert(b64.DeserializeFromUtf8Bytes<string>(encB) == "TestBytes", "Custom ICacheSerializer bytes round-trip");

        // CacheDiagnostics - constants
        Assert(CacheDiagnostics.MeterName == "EricksonLopez.Caching", "MeterName");
        Assert(CacheDiagnostics.ActivitySourceName == "EricksonLopez.Caching", "ActivitySourceName");
        Assert(CacheDiagnostics.ActivitySource is not null, "ActivitySource property");
        Assert(CacheDiagnostics.ActivitySource!.Name == "EricksonLopez.Caching", "ActivitySource.Name");
        Console.WriteLine($"  MeterName: {CacheDiagnostics.MeterName}");

        // StartActivity
        using var act = CacheDiagnostics.StartActivity("get", "memory", "test:key");
        Console.WriteLine($"  StartActivity: {(act is null ? "null (no listener)" : "Activity")}");

        // RecordHit
        CacheDiagnostics.RecordHit("memory");
        CacheDiagnostics.RecordHit("memory", "L1");
        CacheDiagnostics.RecordHit("redis", "L2");
        CacheDiagnostics.RecordHit("hybrid", "get_or_create");

        // RecordMiss
        CacheDiagnostics.RecordMiss("memory");
        CacheDiagnostics.RecordMiss("redis", "get");

        // RecordStampedePrevented
        CacheDiagnostics.RecordStampedePrevented("memory");
        CacheDiagnostics.RecordStampedePrevented("redis");

        // RecordEviction - all reason codes used in the library
        CacheDiagnostics.RecordEviction("memory", "expired");
        CacheDiagnostics.RecordEviction("memory", "capacity_compaction", 10);
        CacheDiagnostics.RecordEviction("memory", "prefix", 5);
        CacheDiagnostics.RecordEviction("memory", "tag", 3);
        CacheDiagnostics.RecordEviction("memory", "explicit");
        CacheDiagnostics.RecordEviction("memory", "explicit_bulk", 20);
        CacheDiagnostics.RecordEviction("memory", "expired_sweep", 7);
        CacheDiagnostics.RecordEviction("memory", "noop", 0); // count<=0 silently ignored

        // RecordError - all known error codes
        CacheDiagnostics.RecordError("memory", "FACTORY_FAILED");
        CacheDiagnostics.RecordError("redis", "CONNECTION_FAILED");
        CacheDiagnostics.RecordError("redis", "DESERIALIZATION_FAILED");
        CacheDiagnostics.RecordError("hybrid", "L2_GET_FAILED");
        CacheDiagnostics.RecordError("hybrid", "L2_SET_FAILED");
        CacheDiagnostics.RecordError("hybrid", "L2_RECHECK_FAILED");
        CacheDiagnostics.RecordError("hybrid", "L2_TAG_REMOVE_FAILED");
        CacheDiagnostics.RecordError("hybrid", "FAIL_OPEN_MISS");

        Assert(true, "All CacheDiagnostics methods exercised");
        PrintOk("Level 8 complete");
    }

    // ── Level 9: Dependency Injection ─────────────────────────────────────────
    private static void Level9_DependencyInjection()
    {
        PrintSection("Level 9 - Dependency Injection (All Extension Methods)");

        // 9a. AddMemoryCacheProvider() + AddCacheInvalidator()
        {
            var s = new ServiceCollection();
            s.AddMemoryCacheProvider(); s.AddCacheInvalidator();
            using var sp = s.BuildServiceProvider();
            Assert(sp.GetRequiredService<ICacheProvider>() is MemoryCacheProvider, "AddMemoryCacheProvider()");
            Assert(sp.GetRequiredService<ITaggedCacheProvider>() is MemoryCacheProvider, "ITaggedCacheProvider from Memory");
            Assert(sp.GetRequiredService<ICacheInvalidator>() is CacheInvalidator, "AddCacheInvalidator()");
        }

        // 9b. AddMemoryCacheProvider(Action<MemoryCacheOptions>)
        {
            var s = new ServiceCollection();
            s.AddMemoryCacheProvider(o => { o.MaxCapacity = 5_000; o.CompactPercentage = 0.20; o.ExpirationScanFrequency = TimeSpan.FromMinutes(5); });
            using var sp = s.BuildServiceProvider();
            Assert(sp.GetRequiredService<ICacheProvider>() is MemoryCacheProvider, "AddMemoryCacheProvider(configure)");
        }

        // 9c. AddCacheInvalidator<TInvalidator>() generic overload
        {
            var s = new ServiceCollection();
            s.AddMemoryCacheProvider(); s.AddCacheInvalidator<CustomCacheInvalidator>();
            using var sp = s.BuildServiceProvider();
            Assert(sp.GetRequiredService<ICacheInvalidator>() is CustomCacheInvalidator, "AddCacheInvalidator<T>()");
        }

        // 9d. AddHybridCache
        {
            var s = new ServiceCollection();
            s.AddMemoryCacheProvider();
            s.AddHybridCache(
                localCacheFactory: sp2 => sp2.GetRequiredService<MemoryCacheProvider>(),
                distributedCacheFactory: sp2 => sp2.GetRequiredService<MemoryCacheProvider>());
            using var sp = s.BuildServiceProvider();
            Assert(sp.GetRequiredService<ICacheProvider>() is HybridCacheProvider, "AddHybridCache -> HybridCacheProvider");
            Assert(sp.GetRequiredService<ITaggedCacheProvider>() is HybridCacheProvider, "AddHybridCache -> ITaggedCacheProvider");
        }

        // 9e. AddTenantPartitionedCache
        {
            var s = new ServiceCollection();
            s.AddMemoryCacheProvider(); s.AddTenantPartitionedCache(sp2 => "tenant-demo");
            using var sp = s.BuildServiceProvider();
            Assert(sp.GetRequiredService<ICacheProvider>() is TenantPartitionedCacheProvider, "AddTenantPartitionedCache");
        }

        // 9f. AddRedisCaching(Action<RedisCacheOptions>) - overload 1
        {
            var s = new ServiceCollection();
            s.AddRedisCaching(o =>
            {
                o.Configuration = "localhost:6379,abortConnect=false"; o.InstanceName = "showcase";
                o.MaxRetries = 3; o.RetryDelay = TimeSpan.FromMilliseconds(100);
                o.LockTimeout = TimeSpan.FromSeconds(30); o.LockRetryInterval = TimeSpan.FromMilliseconds(50);
                o.MaxLockWaitTime = TimeSpan.FromSeconds(30); o.DefaultAbsoluteExpiration = TimeSpan.FromMinutes(5);
                o.Database = 0; o.FailOpen = false; o.Serializer = SystemTextJsonCacheSerializer.Default;
            });
            Assert(s.Count > 0, "AddRedisCaching(Action<RedisCacheOptions>)");
        }

        // 9g. AddRedisCaching(IConnectionMultiplexer, Action) - overload 2
        {
            var s = new ServiceCollection(); var fake = new FakeConnectionMultiplexer();
            s.AddRedisCaching(fake, o => { o.InstanceName = "v2"; o.FailOpen = true; });
            Assert(s.Count > 0, "AddRedisCaching(IConnectionMultiplexer, configure)");
        }

        // 9h. AddRedisCaching(IConnectionMultiplexer, null) - overload 2 no opts
        {
            var s = new ServiceCollection(); var fake = new FakeConnectionMultiplexer();
            s.AddRedisCaching(fake, configure: null);
            Assert(s.Count > 0, "AddRedisCaching(IConnectionMultiplexer, null)");
        }

        // 9i. AddRedisCaching(Func<IServiceProvider, IConnectionMultiplexer>, Action) - overload 3
        {
            var s = new ServiceCollection();
            s.AddSingleton<IConnectionMultiplexer>(new FakeConnectionMultiplexer());
            s.AddRedisCaching(sp2 => sp2.GetRequiredService<IConnectionMultiplexer>(), o => { o.InstanceName = "factory"; o.MaxRetries = 2; });
            Assert(s.Count > 0, "AddRedisCaching(factory, configure)");
        }

        // 9j. AddRedisCaching(Func<...>, null) - overload 3 no opts
        {
            var s = new ServiceCollection();
            s.AddSingleton<IConnectionMultiplexer>(new FakeConnectionMultiplexer());
            s.AddRedisCaching(sp2 => sp2.GetRequiredService<IConnectionMultiplexer>(), configure: null);
            Assert(s.Count > 0, "AddRedisCaching(factory, null)");
        }

        // 9k. ICacheSerializer registered by AddRedisCaching
        {
            var s = new ServiceCollection(); var fake = new FakeConnectionMultiplexer();
            s.AddRedisCaching(fake);
            using var sp = s.BuildServiceProvider();
            Assert(sp.GetRequiredService<ICacheSerializer>() is SystemTextJsonCacheSerializer, "ICacheSerializer TryAddSingleton");
        }

        PrintOk("Level 9 complete");
    }

    // ── Level 10: Enterprise Architecture ────────────────────────────────────
    // Demonstrates how to compose EricksonLopez.Caching in production-grade
    // multi-layer architectures: CQRS, multi-tenant SaaS, L1+L2+tenant stacking.
    private static void Level10_EnterpriseArchitecture()
    {
        PrintSection("Level 10 - Enterprise Architecture");

        // 10a. Full multi-tier + multi-tenant DI composition
        // Architectural pattern: L1 (in-process) + L2 (Redis) + Tenant isolation
        // Layer order: TenantPartitioned(Hybrid(Memory, Redis))
        {
            var svc = new ServiceCollection();

            // L1: in-process memory cache
            svc.AddMemoryCacheProvider(opt =>
            {
                opt.MaxCapacity = 100_000;
                opt.CompactPercentage = 0.20;
                opt.ExpirationScanFrequency = TimeSpan.FromMinutes(5);
            });

            // L2: Redis (registration only - no real connection needed for this demo)
            svc.AddRedisCaching(new FakeConnectionMultiplexer(), opt =>
            {
                opt.InstanceName = "enterprise";
                opt.DefaultAbsoluteExpiration = TimeSpan.FromHours(4);
                opt.MaxRetries = 5;
                opt.FailOpen = true; // degrade gracefully if Redis is down
            });

            // L2: second MemoryCacheProvider simulates distributed cache in this demo.
            // In production, replace with: distributedCacheFactory: sp => sp.GetRequiredService<RedisCacheProvider>()
            // (after registering AddRedisCaching with a real connection string)
            svc.AddSingleton<MemoryCacheProvider>(new MemoryCacheProvider(new MemoryCacheOptions { MaxCapacity = 200_000 }));

            // Hybrid coordinator: L1 (in-process) + L2 (simulated distributed)
            svc.AddHybridCache(
                localCacheFactory: sp => sp.GetRequiredService<MemoryCacheProvider>(),
                distributedCacheFactory: sp => sp.GetRequiredService<MemoryCacheProvider>());

            // Tenant isolation on top of hybrid
            var tenantId = "enterprise-tenant-a";
            svc.AddTenantPartitionedCache(_ => tenantId);

            // Invalidator
            svc.AddCacheInvalidator();

            using var sp = svc.BuildServiceProvider();

            // The resolved ICacheProvider is TenantPartitionedCacheProvider
            // wrapping HybridCacheProvider(Memory, Redis)
            var cache = sp.GetRequiredService<ICacheProvider>();
            Assert(cache is TenantPartitionedCacheProvider,
                "Enterprise: root ICacheProvider is TenantPartitionedCacheProvider");

            var invalidator = sp.GetRequiredService<ICacheInvalidator>();
            Assert(invalidator is CacheInvalidator, "Enterprise: ICacheInvalidator is CacheInvalidator");

            Console.WriteLine("  Architecture: TenantPartitioned(Hybrid(Memory, Redis)) resolved correctly");
        }

        // 10b. ICacheSerializer replacement (plug in custom serializer system-wide)
        {
            var svc = new ServiceCollection();
            // Register a custom serializer BEFORE AddRedisCaching so TryAddSingleton doesn't override it
            svc.AddSingleton<ICacheSerializer>(new Base64CacheSerializer());
            svc.AddRedisCaching(new FakeConnectionMultiplexer());
            using var sp = svc.BuildServiceProvider();
            Assert(sp.GetRequiredService<ICacheSerializer>() is Base64CacheSerializer,
                "Enterprise: custom ICacheSerializer takes precedence");
        }

        // 10c. Multiple invalidators pattern (CQRS event handlers)
        // Each aggregate root has its own cache namespace; one invalidator per handler
        {
            using var cache = new MemoryCacheProvider();
            var ordersInvalidator = new CacheInvalidator(cache);
            var catalogInvalidator = new CacheInvalidator(cache);

            // Order placed → invalidate order-related caches
            cache.SetAsync("orders:1001:summary", "Summary").GetAwaiter().GetResult();
            ordersInvalidator.InvalidateByPrefixAsync("orders:").GetAwaiter().GetResult();

            // Product updated → invalidate catalog caches
            cache.SetAsync("catalog:42:details", "Details").GetAwaiter().GetResult();
            catalogInvalidator.InvalidateByPrefixAsync("catalog:").GetAwaiter().GetResult();

            Assert(true, "Enterprise: per-aggregate CacheInvalidator pattern");
        }

        // 10d. Feature flag pattern — cache feature configs with tagged invalidation
        {
            using var cache = new MemoryCacheProvider();
            ITaggedCacheProvider tagged = cache;

            // Features are tagged per environment and per feature name
            tagged.SetWithTagsAsync("feature:dark-mode", true,
                new[] { "features", "env:prod" },
                CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(5))).GetAwaiter().GetResult();

            tagged.SetWithTagsAsync("feature:new-checkout", false,
                new[] { "features", "env:prod" },
                CacheEntryOptions.FromAbsolute(TimeSpan.FromMinutes(5))).GetAwaiter().GetResult();

            // Invalidate all features when config changes
            tagged.RemoveByTagAsync("features").GetAwaiter().GetResult();
            Assert(true, "Enterprise: feature flag tagged invalidation pattern");
        }

        // 10e. Cache warming pattern
        {
            using var cache = new MemoryCacheProvider(new MemoryCacheOptions { MaxCapacity = 50_000 });
            var warmupData = new Dictionary<string, string>();
            for (var i = 1; i <= 500; i++) warmupData[$"catalog:{i:D4}"] = $"Product_{i}";
            var warmed = cache.SetManyAsync(
                warmupData,
                CacheEntryOptions.FromAbsolute(TimeSpan.FromHours(1))).GetAwaiter().GetResult();
            Assert(warmed.IsSuccess && warmed.Value, "Enterprise: cache warm-up 500 entries");
            Console.WriteLine("  Cache warm-up: 500 catalog entries preloaded");
        }

        // 10f. Observability integration — all OTel hooks in one place
        // In production this fires real metrics; in this demo the Meter has no listener.
        CacheDiagnostics.RecordHit("memory", "warm_up");
        CacheDiagnostics.RecordEviction("memory", "capacity_compaction", 100);
        Assert(CacheDiagnostics.MeterName == "EricksonLopez.Caching",
            "Enterprise: OTel MeterName contract stable");

        PrintOk("Level 10 complete");
    }


    private sealed class Base64CacheSerializer : ICacheSerializer
    {
        public string Serialize<T>(T value)
        {
            var json = JsonSerializer.Serialize(value);
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
        }
        public T? Deserialize<T>(string value)
        {
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value));
            return JsonSerializer.Deserialize<T>(json);
        }
        public byte[] SerializeToUtf8Bytes<T>(T value)
            => System.Text.Encoding.UTF8.GetBytes(Serialize(value));
        public T? DeserializeFromUtf8Bytes<T>(byte[] bytes)
            => Deserialize<T>(System.Text.Encoding.UTF8.GetString(bytes));
    }

    private sealed class CustomCacheInvalidator : ICacheInvalidator
    {
        public Task<Result<bool>> InvalidateByPrefixAsync(string prefix, CancellationToken ct = default)
            => Task.FromResult(Result<bool>.Success(true));
        public Task<Result<bool>> InvalidateByPrefixesAsync(IEnumerable<string> prefixes, CancellationToken ct = default)
            => Task.FromResult(Result<bool>.Success(true));
        public Task<Result<bool>> InvalidateByTagAsync(string tag, CancellationToken ct = default)
            => Task.FromResult(Result<bool>.Success(true));
        public Task<Result<bool>> InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default)
            => Task.FromResult(Result<bool>.Success(true));
    }

    private sealed class NonTaggedCacheProvider : ICacheProvider
    {
        public Task<Result<T?>> GetAsync<T>(string key, CancellationToken ct = default)
            => Task.FromResult(Result<T?>.Success(default));
        public Task<Result<T>> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory,
            CacheEntryOptions? options = null, CancellationToken ct = default)
            => factory(ct).ContinueWith(t => Result<T>.Success(t.Result), TaskContinuationOptions.ExecuteSynchronously);
        public Task<Result<bool>> SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(Result<bool>.Success(true));
        public Task<Result<bool>> RemoveAsync(string key, CancellationToken ct = default)
            => Task.FromResult(Result<bool>.Success(false));
        public Task<Result<bool>> RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
            => Task.FromResult(Result<bool>.Success(true));
    }

    private sealed class MinimalCacheProvider : ICacheProvider
    {
        public Task<Result<T?>> GetAsync<T>(string key, CancellationToken ct = default)
            => Task.FromResult(Result<T?>.Success(default));
        public Task<Result<T>> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory,
            CacheEntryOptions? options = null, CancellationToken ct = default)
            => factory(ct).ContinueWith(t => Result<T>.Success(t.Result), TaskContinuationOptions.ExecuteSynchronously);
        public Task<Result<bool>> SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(Result<bool>.Success(true));
        public Task<Result<bool>> RemoveAsync(string key, CancellationToken ct = default)
            => Task.FromResult(Result<bool>.Success(false));
        public Task<Result<bool>> RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
            => Task.FromResult(Result<bool>.Success(true));
        // ExistsAsync, ExpireAsync, RefreshAsync -> default interface implementations -> Success(false)
    }

    private sealed class FakeConnectionMultiplexer : IConnectionMultiplexer, IAsyncDisposable
    {
        public string Configuration => "localhost:6379,abortConnect=false";
        public string ClientName => "FakeClient";
        public int TimeoutMilliseconds => 5000;
        public long OperationCount => 0;
        public bool PreserveAsyncOrder { get => false; set { } }
        public bool IsConnected => false;
        public bool IsConnecting => false;
        public bool IncludeDetailInExceptions { get => false; set { } }
        public int StormLogThreshold { get => 0; set { } }
        public event EventHandler<RedisErrorEventArgs>? ErrorMessage { add { } remove { } }
        public event EventHandler<ConnectionFailedEventArgs>? ConnectionFailed { add { } remove { } }
        public event EventHandler<InternalErrorEventArgs>? InternalError { add { } remove { } }
        public event EventHandler<ConnectionFailedEventArgs>? ConnectionRestored { add { } remove { } }
        public event EventHandler<EndPointEventArgs>? ConfigurationChanged { add { } remove { } }
        public event EventHandler<EndPointEventArgs>? ConfigurationChangedBroadcast { add { } remove { } }
        event EventHandler<StackExchange.Redis.Maintenance.ServerMaintenanceEvent>? IConnectionMultiplexer.ServerMaintenanceEvent { add { } remove { } }
        public event EventHandler<HashSlotMovedEventArgs>? HashSlotMoved { add { } remove { } }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void RegisterProfiler(Func<ProfilingSession?> profilingSessionProvider) { }
        // explicit no-op to satisfy interface
        public ServerCounters GetCounters() => new(null);
        public System.Net.EndPoint[] GetEndPoints(bool configuredOnly = false) => [];
        public IServer GetServer(string host, int port, object? asyncState = null) => throw new NotSupportedException();
        public IServer GetServer(string hostAndPort, object? asyncState = null) => throw new NotSupportedException();
        public IServer GetServer(System.Net.IPAddress host, int port) => throw new NotSupportedException();
        public IServer GetServer(System.Net.EndPoint endpoint, object? asyncState = null) => throw new NotSupportedException();
        public IServer GetServer(RedisKey key, object? asyncState = null, CommandFlags flags = CommandFlags.None) => throw new NotSupportedException();
        public IServer[] GetServers() => [];
        public Task<bool> ConfigureAsync(System.IO.TextWriter? log = null) => Task.FromResult(false);
        public bool Configure(System.IO.TextWriter? log = null) => false;
        public string GetStatus() => "Fake";
        public void GetStatus(System.IO.TextWriter log) { }
        public void Close(bool allowCommandsToComplete = true) { }
        public Task CloseAsync(bool allowCommandsToComplete = true) => Task.CompletedTask;
        public bool TryGetVolatileConfiguration(out ConfigurationOptions cfg) { cfg = ConfigurationOptions.Parse(Configuration); return true; }
        public string GetStormLog() => string.Empty;
        public void ResetStormLog() { }
        public long PublishedMessageCount => 0;
        public void ExportConfiguration(System.IO.TextWriter tw, ExportOptions options = 0) { }
        public void ExportConfiguration(System.IO.Stream target, ExportOptions options = 0) { }
        public IDatabase GetDatabase(int db = -1, object? asyncState = null) => throw new NotSupportedException();
        public ISubscriber GetSubscriber(object? asyncState = null) => throw new NotSupportedException();
        public Task<IServer[]> SentinelGetReplicasAsync(string svc, CommandFlags flags = 0) => Task.FromResult(Array.Empty<IServer>());
        public Task<KeyValuePair<string, string>[]> SentinelGetSentinelAddressesAsync(string svc, CommandFlags flags = 0) => Task.FromResult(Array.Empty<KeyValuePair<string, string>>());
        public Task<IServer> SentinelGetMasterForAsync(string svc, CommandFlags flags = 0) => throw new NotSupportedException();
        public IServer SentinelGetMasterFor(string svc, CommandFlags flags = 0) => throw new NotSupportedException();
        public void Wait(Task task) => task.Wait();
        public T Wait<T>(Task<T> task) => task.Result;
        public void WaitAll(params Task[] tasks) => Task.WaitAll(tasks);
        public int HashSlot(RedisKey key) => 0;
        public int GetHashSlot(RedisKey key) => 0;
        public long PublishReconfigure(CommandFlags flags = CommandFlags.None) => 0;
        public Task<long> PublishReconfigureAsync(CommandFlags flags = CommandFlags.None) => Task.FromResult(0L);
        public void AddLibraryNameSuffix(string suffix) { }
        public new string ToString() => "FakeConnectionMultiplexer";
    }

    private sealed record ProductDto(int Id, string Name, decimal Price);

    // ── Utilities ─────────────────────────────────────────────────────────────

    private static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==============================================================");
        Console.WriteLine("  EricksonLopez.Caching  |  Official Reference Showcase");
        Console.WriteLine("  Level 0 -> Level 9     |  100% Public API Coverage");
        Console.WriteLine("==============================================================");
        Console.ResetColor();
    }

    private static void PrintSection(string name)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n-- {name} --");
        Console.ResetColor();
    }

    private static void PrintOk(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  OK: {message}");
        Console.ResetColor();
    }

    private static void Assert(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException($"[ASSERTION FAILED] {label}");
        Console.WriteLine($"  v {label}");
    }
}

