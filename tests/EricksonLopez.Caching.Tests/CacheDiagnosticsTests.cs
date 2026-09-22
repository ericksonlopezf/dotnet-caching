// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.Caching.Tests;

public sealed class CacheDiagnosticsTests
{
    [Fact]
    public void StartActivity_WhenNoListeners_ReturnsNull()
    {
        var activity = CacheDiagnostics.StartActivity("get", "memory", "test:key");
        activity.Should().BeNull();
    }

    [Fact]
    public void StartActivity_WhenListenerSubscribed_EmitsConfiguredActivity()
    {
        var activities = new System.Collections.Concurrent.ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CacheDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = a => activities.Add(a)
        };
        ActivitySource.AddActivityListener(listener);

        using (var activity = CacheDiagnostics.StartActivity("get_or_create", "memory", "user:42"))
        {
            activity.Should().NotBeNull();
            activity!.OperationName.Should().Be("Cache get_or_create");
            activity.GetTagItem("cache.operation").Should().Be("get_or_create");
            activity.GetTagItem("cache.provider").Should().Be("memory");
            activity.GetTagItem("cache.key").Should().Be("user:42");
        }

        activities.ToArray().Should().Contain(a => a.OperationName == "Cache get_or_create" && a.GetTagItem("cache.key") as string == "user:42");
    }

    [Fact]
    public void MetricsMethods_DoNotThrow_WhenInvoked()
    {
        var act = () =>
        {
            CacheDiagnostics.RecordHit("memory", "get");
            CacheDiagnostics.RecordMiss("memory", "get");
            CacheDiagnostics.RecordStampedePrevented("memory");
            CacheDiagnostics.RecordEviction("memory", "expired", 5);
            CacheDiagnostics.RecordEviction("memory", "expired", 0);
            CacheDiagnostics.RecordError("memory", "FACTORY_FAILED");
        };

        act.Should().NotThrow();
    }
}
