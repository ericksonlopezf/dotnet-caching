// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;

namespace EricksonLopez.Caching;

internal sealed class MemoryCacheEntry
{
    public object? Value { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? AbsoluteExpiration { get; internal set; }
    public TimeSpan? SlidingExpiration { get; internal set; }
    public TimeSpan? FailSafeMaxStale { get; }
    private long _lastAccessedAtTicks;

    public DateTimeOffset LastAccessedAt
    {
        get => new(Interlocked.Read(ref _lastAccessedAtTicks), TimeSpan.Zero);
        set => Interlocked.Exchange(ref _lastAccessedAtTicks, value.UtcTicks);
    }

    public MemoryCacheEntry(
        object? value,
        DateTimeOffset createdAt,
        DateTimeOffset? absoluteExpiration = null,
        TimeSpan? slidingExpiration = null,
        TimeSpan? failSafeMaxStale = null)
    {
        Value = value;
        CreatedAt = createdAt;
        AbsoluteExpiration = absoluteExpiration;
        SlidingExpiration = slidingExpiration;
        FailSafeMaxStale = failSafeMaxStale;
        _lastAccessedAtTicks = createdAt.UtcTicks;
    }

    public bool IsExpired(DateTimeOffset now)
    {
        if (AbsoluteExpiration.HasValue && now >= AbsoluteExpiration.Value)
        {
            return true;
        }

        if (SlidingExpiration.HasValue && (now - LastAccessedAt) >= SlidingExpiration.Value)
        {
            return true;
        }

        return false;
    }

    public bool IsWithinStaleWindow(DateTimeOffset now)
    {
        if (!FailSafeMaxStale.HasValue)
        {
            return false;
        }

        DateTimeOffset expirationBase;
        if (AbsoluteExpiration.HasValue && SlidingExpiration.HasValue)
        {
            var slidingExp = LastAccessedAt + SlidingExpiration.Value;
            expirationBase = AbsoluteExpiration.Value < slidingExp ? AbsoluteExpiration.Value : slidingExp;
        }
        else if (AbsoluteExpiration.HasValue)
        {
            expirationBase = AbsoluteExpiration.Value;
        }
        else if (SlidingExpiration.HasValue)
        {
            expirationBase = LastAccessedAt + SlidingExpiration.Value;
        }
        else
        {
            expirationBase = CreatedAt;
        }

        return now <= expirationBase + FailSafeMaxStale.Value;
    }
}
