// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Result;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Caching.Tests;

public sealed class CacheInvalidatorTests
{
    private readonly ITaggedCacheProvider _taggedProvider = Substitute.For<ITaggedCacheProvider>();
    private readonly ICacheProvider _nonTaggedProvider = Substitute.For<ICacheProvider>();
    private readonly ILogger<CacheInvalidator> _logger = Substitute.For<ILogger<CacheInvalidator>>();

    [Fact]
    public void Constructor_WhenCacheProviderIsNull_ThrowsArgumentNullException()
    {
        var act = () => new CacheInvalidator(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("cacheProvider");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InvalidateByPrefixAsync_WhenPrefixIsNullOrWhitespace_ReturnsFailure(string? prefix)
    {
        var invalidator = new CacheInvalidator(_taggedProvider, _logger);

        var result = await invalidator.InvalidateByPrefixAsync(prefix!);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cache.InvalidPrefix");
    }

    [Fact]
    public async Task InvalidateByPrefixAsync_WhenValidPrefix_DelegatesToProviderAndLogs()
    {
        _taggedProvider.RemoveByPrefixAsync("tenant:1:", Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));
        _logger.IsEnabled(LogLevel.Debug).Returns(true);

        var invalidator = new CacheInvalidator(_taggedProvider, _logger);

        var result = await invalidator.InvalidateByPrefixAsync("tenant:1:");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        await _taggedProvider.Received(1).RemoveByPrefixAsync("tenant:1:", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateByPrefixAsync_WithoutLogger_SucceedsWithoutLogging()
    {
        _taggedProvider.RemoveByPrefixAsync("tenant:2:", Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        var invalidator = new CacheInvalidator(_taggedProvider, null);

        var result = await invalidator.InvalidateByPrefixAsync("tenant:2:");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task InvalidateByPrefixAsync_WhenProviderReturnsFailure_PropagatesFailureAndDoesNotLog()
    {
        _taggedProvider.RemoveByPrefixAsync("fail_prefix:", Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Failure(new Error("Provider.Fail", "Error removing prefix")));

        var invalidator = new CacheInvalidator(_taggedProvider, _logger);

        var result = await invalidator.InvalidateByPrefixAsync("fail_prefix:");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Provider.Fail");
        _logger.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task InvalidateByPrefixesAsync_WhenPrefixesIsNull_ThrowsArgumentNullException()
    {
        var invalidator = new CacheInvalidator(_taggedProvider);

        var act = () => invalidator.InvalidateByPrefixesAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InvalidateByPrefixesAsync_SkipsWhitespaceAndEvaluatesAllPrefixes()
    {
        _taggedProvider.RemoveByPrefixAsync("prefixA:", Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));
        _taggedProvider.RemoveByPrefixAsync("prefixB:", Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        var invalidator = new CacheInvalidator(_taggedProvider);

        var result = await invalidator.InvalidateByPrefixesAsync(["prefixA:", "", "   ", "prefixB:"]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        await _taggedProvider.Received(1).RemoveByPrefixAsync("prefixA:", Arg.Any<CancellationToken>());
        await _taggedProvider.Received(1).RemoveByPrefixAsync("prefixB:", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateByPrefixesAsync_WhenOnePrefixFails_ReturnsSuccessWithFalse()
    {
        _taggedProvider.RemoveByPrefixAsync("ok:", Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));
        _taggedProvider.RemoveByPrefixAsync("fail:", Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Failure(new Error("Cache.Error", "Fail")));

        var invalidator = new CacheInvalidator(_taggedProvider);

        var result = await invalidator.InvalidateByPrefixesAsync(["ok:", "fail:"]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InvalidateByTagAsync_WhenTagIsNullOrWhitespace_ReturnsFailure(string? tag)
    {
        var invalidator = new CacheInvalidator(_taggedProvider, _logger);

        var result = await invalidator.InvalidateByTagAsync(tag!);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cache.InvalidTag");
    }

    [Fact]
    public async Task InvalidateByTagAsync_WhenProviderDoesNotImplementITaggedCacheProvider_ReturnsFailure()
    {
        var invalidator = new CacheInvalidator(_nonTaggedProvider, _logger);

        var result = await invalidator.InvalidateByTagAsync("catalog");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cache.TagsNotSupported");
    }

    [Fact]
    public async Task InvalidateByTagAsync_WhenValidTagAndTaggedProvider_DelegatesAndLogs()
    {
        _taggedProvider.RemoveByTagAsync("catalog", Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));
        _logger.IsEnabled(LogLevel.Debug).Returns(true);

        var invalidator = new CacheInvalidator(_taggedProvider, _logger);

        var result = await invalidator.InvalidateByTagAsync("catalog");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        await _taggedProvider.Received(1).RemoveByTagAsync("catalog", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateByTagAsync_WhenProviderReturnsFailure_PropagatesFailureAndDoesNotLog()
    {
        _taggedProvider.RemoveByTagAsync("fail_tag", Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Failure(new Error("Provider.Fail", "Error removing tag")));

        var invalidator = new CacheInvalidator(_taggedProvider, _logger);

        var result = await invalidator.InvalidateByTagAsync("fail_tag");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Provider.Fail");
        _logger.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task InvalidateByTagsAsync_WhenTagsIsNull_ThrowsArgumentNullException()
    {
        var invalidator = new CacheInvalidator(_taggedProvider);

        var act = () => invalidator.InvalidateByTagsAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InvalidateByTagsAsync_SkipsWhitespaceAndRemovesAllTags()
    {
        _taggedProvider.RemoveByTagAsync("tag1", Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));
        _taggedProvider.RemoveByTagAsync("tag2", Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        var invalidator = new CacheInvalidator(_taggedProvider);

        var result = await invalidator.InvalidateByTagsAsync(["tag1", "", "   ", "tag2"]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        await _taggedProvider.Received(1).RemoveByTagAsync("tag1", Arg.Any<CancellationToken>());
        await _taggedProvider.Received(1).RemoveByTagAsync("tag2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateByTagsAsync_WhenOneTagFails_ReturnsSuccessWithFalse()
    {
        _taggedProvider.RemoveByTagAsync("good", Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));
        _taggedProvider.RemoveByTagAsync("bad", Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Failure(new Error("Cache.Fail", "Bad tag")));

        var invalidator = new CacheInvalidator(_taggedProvider);

        var result = await invalidator.InvalidateByTagsAsync(["good", "bad"]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InvalidateAsync_WhenKeyIsNullOrWhitespace_ReturnsFailure(string? key)
    {
        var invalidator = new CacheInvalidator(_taggedProvider, _logger);

        var result = await invalidator.InvalidateAsync(key!);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cache.InvalidKey");
    }

    [Fact]
    public async Task InvalidateAsync_WhenValidKey_DelegatesToProviderAndLogs()
    {
        _taggedProvider.RemoveAsync("item:100", Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));
        _logger.IsEnabled(LogLevel.Debug).Returns(true);

        var invalidator = new CacheInvalidator(_taggedProvider, _logger);

        var result = await invalidator.InvalidateAsync("item:100");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        await _taggedProvider.Received(1).RemoveAsync("item:100", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateManyAsync_WhenKeysIsNull_ThrowsArgumentNullException()
    {
        var invalidator = new CacheInvalidator(_taggedProvider);

        var act = () => invalidator.InvalidateManyAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InvalidateManyAsync_WhenValidKeys_DelegatesToProviderAndLogs()
    {
        _taggedProvider.RemoveManyAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<long>.Success(3));
        _logger.IsEnabled(LogLevel.Debug).Returns(true);

        var invalidator = new CacheInvalidator(_taggedProvider, _logger);

        var result = await invalidator.InvalidateManyAsync(["k1", "k2", "k3"]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(3);
        await _taggedProvider.Received(1).RemoveManyAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }
}
