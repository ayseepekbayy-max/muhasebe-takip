namespace MuhasebeTakip2.App.Services;

public sealed class AccountingSessionLifecycle(IAccountingContextStateStore store)
{
    internal const string EpochKey = "AccountingLoginEpoch";

    public Task EndAsync(HttpContext http) => ReplaceAsync(http, async () =>
    {
        http.Session.Clear();
        await http.Session.CommitAsync(http.RequestAborted);
    });

    public async Task ReplaceAsync(HttpContext http, Func<Task> replace, bool establishAccounting = false)
    {
        var session = http.Session;
        await session.LoadAsync(http.RequestAborted);
        await store.ExecuteAsync(session.Id, async (_, publish) =>
        {
            // Keep a revocation marker so a request with a previously loaded
            // session snapshot cannot recreate the old context after logout.
            publish(new AccountingContextState(0, 0, Guid.Empty));
            await replace();
            if (establishAccounting)
            {
                var epoch = Guid.NewGuid().ToString("N");
                session.SetString(EpochKey, epoch);
                await session.CommitAsync(http.RequestAborted);
                publish(new AccountingContextState(
                    session.GetInt32("KullaniciId")!.Value,
                    session.GetInt32("FirmaId")!.Value,
                    Guid.NewGuid(), epoch));
            }
            return true;
        }, http.RequestAborted);
    }
}
