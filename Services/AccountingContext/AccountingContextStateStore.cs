using System.Collections.Concurrent;

namespace MuhasebeTakip2.App.Services;

public sealed record AccountingContextState(int UserId, int CompanyId, Guid Version, string? LoginEpoch = null, Guid? PortalSwitchGeneration = null);

/// <summary>All readers, writers and future transitions must use the same atomic session scope.</summary>
public interface IAccountingContextStateStore
{
    Task<T> ExecuteAsync<T>(string sessionId,
        Func<AccountingContextState?, Action<AccountingContextState>, Task<T>> operation,
        CancellationToken cancellationToken = default);
}

/// <summary>Single-process foundation only. Never enable cross-company writes across replicas with this store.</summary>
public sealed class LocalAccountingContextStateStore : IAccountingContextStateStore
{
    // Bounded lock count. Hash collisions serialize unrelated sessions but cannot bypass validation.
    private readonly SemaphoreSlim[] gates = Enumerable.Range(0, 256).Select(_ => new SemaphoreSlim(1)).ToArray();
    private readonly ConcurrentDictionary<string, Entry> entries = new();
    private long operations;
    private sealed record Entry(AccountingContextState State, DateTimeOffset Expires);

    public async Task<T> ExecuteAsync<T>(string sessionId,
        Func<AccountingContextState?, Action<AccountingContextState>, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) throw new AccountingContextDeniedException();
        var gate = gates[(uint)StringComparer.Ordinal.GetHashCode(sessionId) % (uint)gates.Length];
        await gate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var state = entries.TryGetValue(sessionId, out var entry) && entry.Expires > now ? entry.State : null;
            return await operation(state, next => entries[sessionId] = new Entry(next, DateTimeOffset.UtcNow.AddHours(8)));
        }
        finally
        {
            gate.Release();
            // Remove only the exact expired entry observed; never remove a concurrently renewed value.
            if (Interlocked.Increment(ref operations) % 256 == 0)
                foreach (var pair in entries)
                    if (pair.Value.Expires <= DateTimeOffset.UtcNow)
                        ((ICollection<KeyValuePair<string, Entry>>)entries).Remove(pair);
        }
    }
}
