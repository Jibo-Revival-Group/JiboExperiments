using Jibo.Cloud.Infrastructure.Persistence;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class PostgreSqlCloudStateHelperTests
{
    [Fact]
    public void TokenHasher_IsDeterministicAndDoesNotRetainRawToken()
    {
        const string rawToken = "hub-secret-token";

        var first = CloudAuthTokenHasher.Hash(rawToken);
        var second = CloudAuthTokenHasher.Hash(rawToken);

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.DoesNotContain(rawToken, first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BoundedCache_EvictsLeastRecentlyUsedEntry()
    {
        var time = new TestTimeProvider(DateTimeOffset.Parse("2026-08-21T12:00:00Z"));
        var cache = new BoundedExpiringCache<string, string>(2, TimeSpan.FromMinutes(5),
            StringComparer.OrdinalIgnoreCase, time);
        cache.Set("first", "one");
        time.Advance(TimeSpan.FromSeconds(1));
        cache.Set("second", "two");
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(cache.TryGet("first", out _));
        time.Advance(TimeSpan.FromSeconds(1));

        cache.Set("third", "three");

        Assert.True(cache.TryGet("first", out var first));
        Assert.Equal("one", first);
        Assert.False(cache.TryGet("second", out _));
        Assert.True(cache.TryGet("third", out var third));
        Assert.Equal("three", third);
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void BoundedCache_ExpiresAndCanBeInvalidated()
    {
        var time = new TestTimeProvider(DateTimeOffset.Parse("2026-08-21T12:00:00Z"));
        var cache = new BoundedExpiringCache<string, string>(2, TimeSpan.FromMinutes(1),
            timeProvider: time);
        cache.Set("expired", "value");
        time.Advance(TimeSpan.FromMinutes(2));
        Assert.False(cache.TryGet("expired", out _));

        cache.Set("removed", "value");
        cache.Remove("removed");
        Assert.False(cache.TryGet("removed", out _));
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan duration) => _now += duration;
    }
}
