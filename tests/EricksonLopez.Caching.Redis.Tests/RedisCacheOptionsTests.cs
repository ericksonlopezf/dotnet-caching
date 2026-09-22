// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Caching.Redis.Tests;

public sealed class RedisCacheOptionsTests
{
    [Fact]
    public void Defaults_AreProperlyConfigured()
    {
        var options = new RedisCacheOptions();

        options.Configuration.Should().Be("localhost:6379,abortConnect=false");
        options.InstanceName.Should().BeNull();
        options.DefaultAbsoluteExpiration.Should().Be(TimeSpan.FromMinutes(5));
        options.Database.Should().Be(0);
        options.MaxRetries.Should().Be(3);
        options.RetryDelay.Should().Be(TimeSpan.FromMilliseconds(100));
        options.LockTimeout.Should().Be(TimeSpan.FromSeconds(30));
        options.LockRetryInterval.Should().Be(TimeSpan.FromMilliseconds(50));
        options.MaxLockWaitTime.Should().Be(TimeSpan.FromSeconds(30));
        options.Serializer.Should().BeNull();
    }

    [Fact]
    public void Properties_CanBeConfiguredAndRetrieved()
    {
        var serializer = Substitute.For<ICacheSerializer>();
        var options = new RedisCacheOptions
        {
            Configuration = "redis-cluster:6379,ssl=true",
            InstanceName = "tenant-prod",
            DefaultAbsoluteExpiration = TimeSpan.FromHours(1),
            Database = 2,
            MaxRetries = 5,
            RetryDelay = TimeSpan.FromMilliseconds(250),
            LockTimeout = TimeSpan.FromMinutes(1),
            LockRetryInterval = TimeSpan.FromMilliseconds(100),
            MaxLockWaitTime = TimeSpan.FromMinutes(2),
            Serializer = serializer
        };

        options.Configuration.Should().Be("redis-cluster:6379,ssl=true");
        options.InstanceName.Should().Be("tenant-prod");
        options.DefaultAbsoluteExpiration.Should().Be(TimeSpan.FromHours(1));
        options.Database.Should().Be(2);
        options.MaxRetries.Should().Be(5);
        options.RetryDelay.Should().Be(TimeSpan.FromMilliseconds(250));
        options.LockTimeout.Should().Be(TimeSpan.FromMinutes(1));
        options.LockRetryInterval.Should().Be(TimeSpan.FromMilliseconds(100));
        options.MaxLockWaitTime.Should().Be(TimeSpan.FromMinutes(2));
        options.Serializer.Should().BeSameAs(serializer);
    }
}
