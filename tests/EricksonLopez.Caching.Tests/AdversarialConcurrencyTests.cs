// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EricksonLopez.Caching.Tests;

/// <summary>
/// Adversarial concurrency and race condition test suite designed to expose synchronization defects,
/// deadlocks, stampede leaks, and torn state under high contention.
/// </summary>
public sealed class AdversarialConcurrencyTests
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task GetOrCreateAsync_1000ConcurrentRequests_ExecutesFactoryExactlyOnce()
    {
        // Arrange
        var provider = new MemoryCacheProvider(_timeProvider);
        const string key = "stampede:hot_account";
        var factoryExecutionCount = 0;
        var startSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, 1000).Select(async _ =>
        {
            await startSignal.Task;
            return await provider.GetOrCreateAsync(key, async ct =>
            {
                Interlocked.Increment(ref factoryExecutionCount);
                await Task.Delay(20, ct); // Simulate expensive computation
                return "computed_data";
            });
        }).ToList();

        // Act - unleash 1,000 tasks concurrently
        startSignal.SetResult();
        var results = await Task.WhenAll(tasks);

        // Assert
        factoryExecutionCount.Should().Be(1, "single-flight guarantee must prevent duplicate factory executions");
        results.Should().AllSatisfy(r =>
        {
            r.IsSuccess.Should().BeTrue();
            r.Value.Should().Be("computed_data");
        });
    }

    [Fact]
    public async Task HighContention_MixedReadsAndWrites_DoesNotDeadlockOrCorrupt()
    {
        // Arrange
        var provider = new MemoryCacheProvider(_timeProvider);
        const int readerCount = 200;
        const int writerCount = 50;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = new ConcurrentBag<Exception>();

        var writers = Enumerable.Range(0, writerCount).Select(w => Task.Run(async () =>
        {
            await startGate.Task;
            for (var i = 0; i < 30 && !cts.Token.IsCancellationRequested; i++)
            {
                try
                {
                    var key = $"item:{i % 10}";
                    await provider.SetAsync(key, $"value_{w}_{i}", cancellationToken: cts.Token);
                    if (i % 5 == 0)
                    {
                        await provider.RemoveAsync(key, cts.Token);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add(ex);
                }
            }
        }));

        var readers = Enumerable.Range(0, readerCount).Select(r => Task.Run(async () =>
        {
            await startGate.Task;
            for (var i = 0; i < 30 && !cts.Token.IsCancellationRequested; i++)
            {
                try
                {
                    var key = $"item:{i % 10}";
                    var res = await provider.GetAsync<string>(key, cts.Token);
                    res.IsSuccess.Should().BeTrue();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add(ex);
                }
            }
        }));

        // Act - unleash concurrent readers and writers asynchronously
        startGate.SetResult();
        await Task.WhenAll(writers.Concat(readers));

        // Assert
        errors.Should().BeEmpty("mixed concurrent reads and writes must never throw exceptions or corrupt state");
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenFactoryCancelled_ReleasesLockForSubsequentCallers()
    {
        // Arrange
        var provider = new MemoryCacheProvider(_timeProvider);
        const string key = "lock_cancellation_test";
        using var cancelledCts = new CancellationTokenSource();
        cancelledCts.Cancel();

        // Act 1: Calling with already cancelled token should throw OperationCanceledException
        var act = () => provider.GetOrCreateAsync(key, async ct =>
        {
            await Task.Yield();
            return "never";
        }, cancellationToken: cancelledCts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        // Act 2: Immediate subsequent call with valid token MUST succeed (lock must not remain held)
        var secondResult = await provider.GetOrCreateAsync(key, _ => Task.FromResult("recovered"));

        // Assert
        secondResult.IsSuccess.Should().BeTrue();
        secondResult.Value.Should().Be("recovered");
    }

    [Fact]
    public async Task GetOrCreateAsync_ConcurrentDifferentKeys_DoNotBlockEachOther()
    {
        // Arrange
        var provider = new MemoryCacheProvider(_timeProvider);
        var gateA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Key A is blocked inside factory waiting on gateA
        var taskA = Task.Run(() => provider.GetOrCreateAsync("key:A", async _ =>
        {
            await gateA.Task;
            return "val_A";
        }));

        // Key B should complete immediately without being blocked by Key A's lock
        var taskB = Task.Run(() => provider.GetOrCreateAsync("key:B", _ => Task.FromResult("val_B")));

        // Key B must complete within 2 seconds even though Key A is stuck
        var completed = await Task.WhenAny(taskB, Task.Delay(2000));
        completed.Should().Be(taskB, "different keys must use independent locks and never block each other");

        var resultB = await taskB;
        resultB.Value.Should().Be("val_B");

        // Release Key A
        gateA.SetResult();
        var resultA = await taskA;
        resultA.Value.Should().Be("val_A");
    }

    [Fact]
    public async Task MemoryCacheEntry_LastAccessedAt_ConcurrentReads_VerifyStability()
    {
        // Arrange
        var provider = new MemoryCacheProvider(_timeProvider);
        const string key = "sliding_key";
        await provider.SetAsync(key, "sliding_value", CacheEntryOptions.FromSliding(TimeSpan.FromMinutes(10)));

        const int iterations = 1000;
        var exceptions = new ConcurrentBag<Exception>();

        // Act - 100 tasks repeatedly reading the sliding key concurrently
        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < iterations; i++)
            {
                try
                {
                    var res = await provider.GetAsync<string>(key);
                    if (!res.IsSuccess || res.Value != "sliding_value")
                    {
                        exceptions.Add(new InvalidOperationException("Failed to read sliding value correctly"));
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        }));

        await Task.WhenAll(tasks);

        // Assert
        exceptions.Should().BeEmpty("concurrent reads updating LastAccessedAt must not throw or tear");
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenCancellationOccursWhileWaitingOnSemaphore_DoesNotReleaseUnacquiredLock()
    {
        // Arrange
        var provider = new MemoryCacheProvider(_timeProvider);
        const string key = "cancel_while_waiting_key";
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryExecutedCount = 0;

        // Caller 1 acquires lock and enters factory
        var caller1Task = Task.Run(() => provider.GetOrCreateAsync(key, async ct =>
        {
            Interlocked.Increment(ref factoryExecutedCount);
            factoryStarted.SetResult();
            await factoryRelease.Task;
            return "val1";
        }));

        await factoryStarted.Task;

        // Caller 2 waits on the lock, but gets cancelled while waiting
        using var cts2 = new CancellationTokenSource();
        var caller2Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var caller2Task = Task.Run(async () =>
        {
            caller2Started.SetResult();
            return await provider.GetOrCreateAsync(key, _ => Task.FromResult("val2"), cancellationToken: cts2.Token);
        });

        await caller2Started.Task;
        await Task.Delay(50); // Ensure Caller 2 is blocked in WaitAsync
        cts2.Cancel();

        // Caller 2 should throw OperationCanceledException
        var act2 = () => caller2Task;
        await act2.Should().ThrowAsync<OperationCanceledException>();

        // ADVERSARIAL EVIDENCE ASSERTION:
        // Caller 1 is STILL executing factory!
        // If Caller 2's finally block prematurely released the semaphore, Caller 3 will enter factory concurrently!
        var caller3FactoryExecuted = false;
        var caller3Task = Task.Run(() => provider.GetOrCreateAsync(key, _ =>
        {
            caller3FactoryExecuted = true;
            return Task.FromResult("val3");
        }));

        await Task.Delay(50);
        // Caller 3 must NOT have executed its factory because Caller 1 is still running!
        caller3FactoryExecuted.Should().BeFalse("single-flight lock must NOT be prematurely released by a cancelled waiter!");

        // Release Caller 1
        factoryRelease.SetResult();
        var result1 = await caller1Task;
        var result3 = await caller3Task;

        result1.Value.Should().Be("val1");
        result3.Value.Should().Be("val1", "Caller 3 should receive Caller 1's computed value");
        factoryExecutedCount.Should().Be(1, "Factory must only be executed once");

        // Inspect internal _locks dictionary via reflection to verify cleanup
        var locksField = typeof(MemoryCacheProvider).GetField("_locks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var locksDict = locksField!.GetValue(provider) as System.Collections.IDictionary;
        locksDict.Should().NotBeNull();
        locksDict!.Count.Should().Be(0, "Lock dictionary must be cleaned up and not leak semaphores when callers are cancelled!");
    }
}
