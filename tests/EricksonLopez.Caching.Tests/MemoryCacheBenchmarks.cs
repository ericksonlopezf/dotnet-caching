// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;
using Xunit.Abstractions;

namespace EricksonLopez.Caching.Tests;

/// <summary>
/// Performance and contention benchmarks measuring latency (p50, p95, p99), throughput,
/// memory allocations, and scalability across 1, 2, 4, 8, 16, 32, 64, 128 concurrent threads.
/// </summary>
public sealed class MemoryCacheBenchmarks
{
    private readonly ITestOutputHelper _output;

    public MemoryCacheBenchmarks(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Benchmark_MemoryCache_Operations_ThroughputAndLatency()
    {
        var provider = new MemoryCacheProvider();
        const int iterations = 50_000;
        var latencies = new double[iterations];

        // 1. Benchmark: SetAsync
        var setWatch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var opWatch = Stopwatch.StartNew();
            await provider.SetAsync($"bench:{i}", $"value_{i}");
            latencies[i] = opWatch.Elapsed.TotalMicroseconds;
        }
        setWatch.Stop();

        Array.Sort(latencies);
        var setP50 = latencies[(int)(iterations * 0.50)];
        var setP95 = latencies[(int)(iterations * 0.95)];
        var setP99 = latencies[(int)(iterations * 0.99)];
        var setThroughput = iterations / setWatch.Elapsed.TotalSeconds;

        _output.WriteLine($"[BENCHMARK] SetAsync: {setThroughput:N0} ops/sec | p50: {setP50:F2}µs | p95: {setP95:F2}µs | p99: {setP99:F2}µs");

        // 2. Benchmark: GetAsync (Hit)
        var hitWatch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var opWatch = Stopwatch.StartNew();
            await provider.GetAsync<string>($"bench:{i}");
            latencies[i] = opWatch.Elapsed.TotalMicroseconds;
        }
        hitWatch.Stop();

        Array.Sort(latencies);
        var hitP50 = latencies[(int)(iterations * 0.50)];
        var hitP95 = latencies[(int)(iterations * 0.95)];
        var hitP99 = latencies[(int)(iterations * 0.99)];
        var hitThroughput = iterations / hitWatch.Elapsed.TotalSeconds;

        _output.WriteLine($"[BENCHMARK] GetAsync (Hit): {hitThroughput:N0} ops/sec | p50: {hitP50:F2}µs | p95: {hitP95:F2}µs | p99: {hitP99:F2}µs");

        // 3. Benchmark: GetAsync (Miss)
        var missWatch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var opWatch = Stopwatch.StartNew();
            await provider.GetAsync<string>($"missing:{i}");
            latencies[i] = opWatch.Elapsed.TotalMicroseconds;
        }
        missWatch.Stop();

        Array.Sort(latencies);
        var missP50 = latencies[(int)(iterations * 0.50)];
        var missP95 = latencies[(int)(iterations * 0.95)];
        var missP99 = latencies[(int)(iterations * 0.99)];
        var missThroughput = iterations / missWatch.Elapsed.TotalSeconds;

        _output.WriteLine($"[BENCHMARK] GetAsync (Miss): {missThroughput:N0} ops/sec | p50: {missP50:F2}µs | p95: {missP95:F2}µs | p99: {missP99:F2}µs");

        // Asserts to ensure healthy throughput
        hitThroughput.Should().BeGreaterThan(50_000, "GetAsync hit throughput should exceed 50k ops/sec in-memory");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    public async Task Benchmark_Contention_Scaling(int threadCount)
    {
        var provider = new MemoryCacheProvider();
        // Pre-populate 1,000 keys
        for (var i = 0; i < 1_000; i++)
        {
            await provider.SetAsync($"scale:{i}", $"value_{i}");
        }

        const int opsPerThread = 2_000;
        var totalOps = threadCount * opsPerThread;
        var latencies = new ConcurrentBag<double>();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        var memBefore = GC.GetAllocatedBytesForCurrentThread();

        var watch = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, threadCount).Select(t => Task.Run(async () =>
        {
            var rnd = new Random(t);
            for (var i = 0; i < opsPerThread; i++)
            {
                var key = $"scale:{rnd.Next(1_000)}";
                var opWatch = Stopwatch.StartNew();
                if (i % 10 == 0)
                {
                    await provider.SetAsync(key, $"updated_{t}_{i}");
                }
                else
                {
                    await provider.GetAsync<string>(key);
                }
                latencies.Add(opWatch.Elapsed.TotalMicroseconds);
            }
        }));

        await Task.WhenAll(tasks);
        watch.Stop();

        var sorted = latencies.OrderBy(x => x).ToArray();
        var p50 = sorted[(int)(sorted.Length * 0.50)];
        var p95 = sorted[(int)(sorted.Length * 0.95)];
        var p99 = sorted[(int)(sorted.Length * 0.99)];
        var throughput = totalOps / watch.Elapsed.TotalSeconds;

        _output.WriteLine($"[CONTENTION] Threads: {threadCount,3} | Total Ops: {totalOps,7} | Duration: {watch.ElapsedMilliseconds,5}ms | Throughput: {throughput,10:N0} ops/s | p50: {p50,6:F2}µs | p95: {p95,6:F2}µs | p99: {p99,6:F2}µs");

        throughput.Should().BeGreaterThan(10_000);
    }
}
