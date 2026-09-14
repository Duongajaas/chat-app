namespace ChatApp.RateLimiting;

public readonly record struct RateLimitDecision(bool Allowed, TimeSpan RetryAfter);

public interface IRateLimitCounter
{
    Task<RateLimitDecision> AcquireAsync(string policy, string partition, int limit, TimeSpan window,
        CancellationToken cancellationToken = default);
}
