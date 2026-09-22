// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Caching.Redis.Tests;

public sealed class CacheErrorsAndLogTests
{
    private readonly ILogger _logger = Substitute.For<ILogger>();

    [Fact]
    public void CacheErrors_ConnectionFailed_ReturnsFailureWithCorrectCodeAndDetail()
    {
        var error = CacheErrors.ConnectionFailed("Socket reset by peer");

        error.Code.Should().Be("Cache.Redis.ConnectionFailed");
        error.Description.Should().Contain("Socket reset by peer");
    }

    [Fact]
    public void CacheErrors_FactoryFailed_ReturnsFailureWithCorrectCodeKeyAndDetail()
    {
        var error = CacheErrors.FactoryFailed("user:123", "Database timeout");

        error.Code.Should().Be("Cache.Redis.FactoryFailed");
        error.Description.Should().Contain("user:123");
        error.Description.Should().Contain("Database timeout");
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging argument evaluation")]
    public void Log_LoggerMessageMethods_ExecuteWithoutThrowing()
    {
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var ex = new InvalidOperationException("Test fault");

        Log.GetFailed(_logger, "k1", ex);
        Log.SetFailed(_logger, "k2", ex);
        Log.DeleteFailed(_logger, "k3", ex);
        Log.PrefixDeletionFailed(_logger, "p1", ex);
        Log.PrefixDeletionCompleted(_logger, 5, "p1");
        Log.FactoryFailed(_logger, "k4", ex);
        Log.RetryAttempt(_logger, "GET", 1, 3, ex);
        Log.LockAcquired(_logger, "k5");
        Log.LockWait(_logger, "k5");
        Log.LockReleased(_logger, "k5");
        Log.TagDeletionFailed(_logger, "t1", ex);
        Log.TagDeletionCompleted(_logger, 10, "t1");

        _logger.Received().Log(
            Arg.Any<LogLevel>(),
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}
