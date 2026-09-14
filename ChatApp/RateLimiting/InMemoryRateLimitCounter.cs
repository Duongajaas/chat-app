using Microsoft.Extensions.Caching.Memory;

namespace ChatApp.RateLimiting;

// Single-process fallback for local development with Redis disabled.
public sealed class InMemoryRateLimitCounter(IMemoryCache cache) : IRateLimitCounter
{
    private sealed class Window(DateTimeOffset end) { public int Count; public DateTimeOffset End = end; }
    private readonly object gate = new();
    public Task<RateLimitDecision> AcquireAsync(string policy, string partition, int limit, TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var now = DateTimeOffset.UtcNow;
            var counter = cache.GetOrCreate((policy, partition), entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = window;
                return new Window(now + window);
            })!;
            var allowed = counter.Count < limit;
            if (allowed) counter.Count++;
            return Task.FromResult(new RateLimitDecision(allowed, counter.End - now));
        }
    }
}
