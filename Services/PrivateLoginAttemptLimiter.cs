namespace MuhasebeTakip2.App.Services;

// One reserved login name, two passwords: share a bounded server-side counter.
// Serializing verification also prevents concurrent attempts from bypassing the limit.
// In-memory, per application instance; a restart clears the counter and lockout.
public sealed class PrivateLoginAttemptLimiter(TimeProvider clock)
{
    private readonly object gate = new();
    private int failures;
    private DateTimeOffset windowEnd;
    private DateTimeOffset blockedUntil;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    public PrivateAccessResult Authenticate(Func<PrivateAccessResult> verify)
    {
        lock (gate)
        {
            var now = clock.GetUtcNow();
            if (now < blockedUntil) return new(true, null);
            if (now >= windowEnd) failures = 0;
            var result = verify();
            if (result.Person is not null)
            {
                failures = 0;
                blockedUntil = windowEnd = default;
            }
            else
            {
                if (failures == 0) windowEnd = now + Window;
                if (++failures >= 5) blockedUntil = windowEnd = clock.GetUtcNow() + Window;
            }
            return result;
        }
    }
}
