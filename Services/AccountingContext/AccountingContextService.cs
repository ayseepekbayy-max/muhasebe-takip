using System.Text.Json;
using Microsoft.Extensions.Primitives;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Services;

public sealed class AccountingContextDeniedException : Exception
{
    public AccountingContextDeniedException() : base("İşlem bağlamı doğrulanamadı. Sayfayı yeniden yükleyin.") { }
}
public sealed record AccountingActor(int UserId, string UserName, string Role, int HomeCompanyId);
public sealed record AccountingContextStamp(int CompanyId, Guid Version);
public sealed record AccountingRequestContext(AccountingActor Actor, int ActiveCompanyId, Guid Version, Guid? SwitchGeneration = null)
{
    public AccountingContextStamp Stamp => new(ActiveCompanyId, Version);
}
// A proposal alone cannot grant authority to read or write the target.
public sealed record CompanySelectionProposal(AccountingActor Actor, int TargetCompanyId, AccountingContextStamp Expected);

public sealed class AccountingCompanySwitchPolicy(IWebHostEnvironment environment, IConfiguration configuration)
{
    private readonly object gate = new();
    private IChangeToken? observedReload;
    private Guid? generation;

    public bool Enabled => Generation.HasValue;

    internal Guid? Generation
    {
        get
        {
            lock (gate)
            {
                var reload = configuration.GetReloadToken();
                var enabled = (environment.IsProduction() || environment.IsEnvironment("Testing")) &&
                    bool.TryParse(configuration["AdminPortal:EnableCompanySwitch"], out var configured) && configured;
                // Invalidate previously issued access on reload, even if no request saw the disabled value.
                if (!ReferenceEquals(observedReload, reload) || enabled != generation.HasValue)
                {
                    observedReload = reload;
                    generation = enabled ? Guid.NewGuid() : null;
                }
                return generation;
            }
        }
    }
}

public enum AccountingSwitchResult { Applied, Denied, Stale, AuditFailed, Uncertain }
public sealed record AccountingSwitchControl(AccountingRequestContext Context, bool ActiveCompanyAvailable);

public interface IAccountingContextService
{
    Task<AccountingRequestContext> GetCurrentAsync(HttpContext http, CancellationToken cancellationToken = default);
    Task<AccountingSwitchControl> GetSwitchControlAsync(HttpContext http, CancellationToken cancellationToken = default);
    Task<AccountingSwitchResult> SwitchAsync(HttpContext http, int? targetCompanyId, AccountingContextStamp expected, string operationToken, CancellationToken cancellationToken = default);
    Task<CompanySelectionProposal> PrepareSelectionAsync(HttpContext http, int targetCompanyId, AccountingContextStamp expected, CancellationToken cancellationToken = default);
    Task<CompanySelectionProposal> PrepareReturnHomeAsync(HttpContext http, AccountingContextStamp expected, CancellationToken cancellationToken = default);
    Task<T> WithValidatedWriteAsync<T>(HttpContext http, AccountingContextStamp expected,
        Func<AccountingRequestContext, CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
}

public sealed class AccountingContextService(AppDbContext db, IAdminPortalAuthorizationService portalAuthorization,
    IAccountingContextStateStore store, AccountingCompanySwitchPolicy? switchPolicy = null) : IAccountingContextService
{
    public Task<AccountingRequestContext> GetCurrentAsync(HttpContext http, CancellationToken cancellationToken = default)
        => InScopeAsync(http, (context, _) => Task.FromResult(context), cancellationToken);

    public Task<CompanySelectionProposal> PrepareSelectionAsync(HttpContext http, int targetCompanyId,
        AccountingContextStamp expected, CancellationToken cancellationToken = default)
        => InScopeAsync(http, async (context, ct) =>
        {
            Validate(context, expected);
            if (!await portalAuthorization.IsAuthorizedAsync(http, ct) || targetCompanyId <= 0 ||
                !await db.Firmalar.AsNoTracking().AnyAsync(x => x.Id == targetCompanyId && x.AktifMi, ct))
                throw new AccountingContextDeniedException();
            return new CompanySelectionProposal(context.Actor, targetCompanyId, context.Stamp);
        }, cancellationToken);

    public Task<CompanySelectionProposal> PrepareReturnHomeAsync(HttpContext http, AccountingContextStamp expected,
        CancellationToken cancellationToken = default)
        => InScopeAsync(http, async (context, ct) =>
        {
            Validate(context, expected);
            if (!await portalAuthorization.IsAuthorizedAsync(http, ct)) throw new AccountingContextDeniedException();
            return new CompanySelectionProposal(context.Actor, context.Actor.HomeCompanyId, context.Stamp);
        }, cancellationToken);

    public Task<T> WithValidatedWriteAsync<T>(HttpContext http, AccountingContextStamp expected,
        Func<AccountingRequestContext, CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        => InScopeAsync(http, (context, ct) =>
        {
            Validate(context, expected);
            if (context.ActiveCompanyId != context.Actor.HomeCompanyId)
                throw new AccountingContextDeniedException();
            // Hold the same server-side gate through the complete awaited transaction, not just validation.
            return operation(context, ct);
        }, cancellationToken);

    public Task<AccountingSwitchControl> GetSwitchControlAsync(HttpContext http, CancellationToken cancellationToken = default)
        => InStateAsync(http, async (context, state, _, ct) =>
        {
            if (!IsSwitchEnabled(context.SwitchGeneration) || string.IsNullOrWhiteSpace(state.LoginEpoch) ||
                http.Session.GetInt32("FirmaId") != context.Actor.HomeCompanyId ||
                !await portalAuthorization.IsAuthorizedAsync(http, ct))
                throw new AccountingContextDeniedException();

            var available = await db.Firmalar.AsNoTracking().AnyAsync(x => x.Id == context.ActiveCompanyId && x.AktifMi, ct);
            return new AccountingSwitchControl(context, available);
        }, allowUnavailableCompany: true, cancellationToken);

    public async Task<AccountingSwitchResult> SwitchAsync(HttpContext http, int? targetCompanyId,
        AccountingContextStamp expected, string operationToken, CancellationToken cancellationToken = default)
    {
        if (switchPolicy?.Enabled != true) return AccountingSwitchResult.Denied;
        var ticket = AccountingSwitchTokens.Read(http, operationToken);
        if (ticket == null || ticket.Id == Guid.Empty || ticket.Source != expected.CompanyId ||
            ticket.Version != expected.Version || ticket.ReturnHome == targetCompanyId.HasValue ||
            (targetCompanyId.HasValue && ticket.Target != targetCompanyId.Value))
            return AccountingSwitchResult.Stale;

        try
        {
            return await InStateAsync(http, async (context, state, publish, ct) =>
            {
                var target = targetCompanyId ?? context.Actor.HomeCompanyId;
                if (string.IsNullOrWhiteSpace(state.LoginEpoch) || http.Session.GetInt32("FirmaId") != context.Actor.HomeCompanyId ||
                    !await portalAuthorization.IsAuthorizedAsync(http, ct))
                    return AccountingSwitchResult.Denied;
                if (ticket.UserId != context.Actor.UserId || ticket.Epoch != state.LoginEpoch || ticket.Target != target ||
                    ticket.SwitchGeneration != context.SwitchGeneration)
                    return AccountingSwitchResult.Stale;
                if (target <= 0 || !IsSwitchEnabled(context.SwitchGeneration)) return AccountingSwitchResult.Denied;

                var existing = await db.AdminCompanyTransitions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ticket.Id, ct);
                if (existing != null)
                {
                    if (!Matches(existing, ticket)) return AccountingSwitchResult.Stale;
                    if (existing.Status is AdminCompanyTransitionStatus.Pending or AdminCompanyTransitionStatus.Uncertain)
                        return AccountingSwitchResult.Uncertain;
                    if (existing.Status == AdminCompanyTransitionStatus.Completed && context.Version == ticket.Id &&
                        context.ActiveCompanyId == target &&
                        await db.Firmalar.AsNoTracking().AnyAsync(x => x.Id == target && x.AktifMi, ct))
                        return IsSwitchEnabled(context.SwitchGeneration) ? AccountingSwitchResult.Applied : AccountingSwitchResult.Denied;
                    return AccountingSwitchResult.Stale;
                }
                if (expected.Version == Guid.Empty || expected != context.Stamp)
                    return AccountingSwitchResult.Stale;

                var operation = new AdminCompanyTransition
                {
                    Id = ticket.Id, AdminUserId = ticket.UserId, LoginEpoch = ticket.Epoch,
                    SourceCompanyId = ticket.Source, TargetCompanyId = ticket.Target, SourceVersion = ticket.Version,
                    ReturnHome = ticket.ReturnHome, RequestedAtUtc = DateTime.UtcNow,
                    Status = AdminCompanyTransitionStatus.Pending
                };
                db.AdminCompanyTransitions.Add(operation);
                // A persisted claim is never executed again, including after a restart or a lost reply.
                await db.SaveChangesAsync(ct);

                var next = state with
                {
                    CompanyId = target,
                    Version = ticket.Id,
                    PortalSwitchGeneration = target != context.Actor.HomeCompanyId ? context.SwitchGeneration : null
                };
                var commitStarted = false;
                try
                {
                    await using var transaction = await InvoicePaymentTransaction.BeginAsync(db, ct);
                    if (db.Database.IsNpgsql())
                    {
                        await db.Database.ExecuteSqlInterpolatedAsync(
                            $"""SELECT 1 FROM "Kullanicilar" WHERE "Id" = {context.Actor.UserId} FOR SHARE""", ct);
                        await db.Database.ExecuteSqlInterpolatedAsync(
                            $"""SELECT 1 FROM "Firmalar" WHERE "Id" IN ({context.Actor.HomeCompanyId}, {context.ActiveCompanyId}, {target}) ORDER BY "Id" FOR SHARE""", ct);
                    }
                    if (!await portalAuthorization.IsAuthorizedAsync(http, ct) ||
                        !await db.Kullanicilar.AsNoTracking().AnyAsync(x => x.Id == context.Actor.UserId && x.FirmaId == context.Actor.HomeCompanyId, ct) ||
                        !await db.Firmalar.AsNoTracking().AnyAsync(x => x.Id == target && x.AktifMi, ct) ||
                        (targetCompanyId.HasValue &&
                         !await db.Firmalar.AsNoTracking().AnyAsync(x => x.Id == context.ActiveCompanyId && x.AktifMi, ct)))
                    {
                        operation.Status = AdminCompanyTransitionStatus.Failed;
                        operation.ResolvedAtUtc = DateTime.UtcNow;
                        await db.SaveChangesAsync(ct);
                        await transaction.CommitAsync(ct);
                        return AccountingSwitchResult.Denied;
                    }

                    var actorName = await db.Kullanicilar.AsNoTracking().Where(x => x.Id == context.Actor.UserId)
                        .Select(x => x.KullaniciAdi).SingleAsync(ct);
                    var now = DateTime.UtcNow;
                    operation.Status = AdminCompanyTransitionStatus.Completed;
                    operation.ResolvedAtUtc = now;
                    db.IslemGecmisleri.Add(new IslemGecmisi
                    {
                        FirmaId = target,
                        KullaniciId = context.Actor.UserId,
                        KullaniciAdi = actorName[..Math.Min(actorName.Length, 100)],
                        Modul = "Admin Portalı",
                        IslemTuru = targetCompanyId.HasValue ? "Firma Geçişi" : "Ofise Dönüş",
                        Aciklama = $"Oturum kapsamındaki firma seçimi {context.ActiveCompanyId} → {target} olarak onaylandı (işlem {ticket.Id:D}). Bu kayıt tek başına aktif firma erişimini doğrulamaz.",
                        EskiDeger = JsonSerializer.Serialize(new { firmaId = context.ActiveCompanyId }),
                        YeniDeger = JsonSerializer.Serialize(new { firmaId = target }),
                        Tarih = now
                    });
                    await db.SaveChangesAsync(ct);
                    if (!IsSwitchEnabled(context.SwitchGeneration))
                        throw new AccountingContextDeniedException();

                    // Stage under the session gate. No other request can observe this context
                    // until commit succeeds or the old context is restored before gate release.
                    publish(next);
                    commitStarted = true;
                    await transaction.CommitAsync(ct);
                    if (IsSwitchEnabled(context.SwitchGeneration)) return AccountingSwitchResult.Applied;
                    publish(state);
                    return AccountingSwitchResult.Denied;
                }
                catch (Exception ex)
                {
                    publish(state);
                    db.ChangeTracker.Clear();
                    var conflict = InvoicePaymentTransaction.IsConflict(ex);
                    using var verificationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var verify = verificationTimeout.Token;
                    try
                    {
                        var recorded = await db.AdminCompanyTransitions.AsNoTracking()
                            .SingleOrDefaultAsync(x => x.Id == ticket.Id, verify);
                        if (commitStarted && recorded?.Status == AdminCompanyTransitionStatus.Completed &&
                            Matches(recorded, ticket) && !ct.IsCancellationRequested)
                        {
                            if (!IsSwitchEnabled(context.SwitchGeneration)) return AccountingSwitchResult.Denied;
                            publish(next);
                            return AccountingSwitchResult.Applied;
                        }
                        if (recorded?.Status == AdminCompanyTransitionStatus.Pending)
                        {
                            var status = commitStarted && !conflict
                                ? AdminCompanyTransitionStatus.Uncertain : AdminCompanyTransitionStatus.Failed;
                            await db.AdminCompanyTransitions.Where(x => x.Id == ticket.Id && x.Status == AdminCompanyTransitionStatus.Pending)
                                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, status)
                                    .SetProperty(x => x.ResolvedAtUtc, DateTime.UtcNow), verify);
                        }
                    }
                    catch (Exception verificationError)
                    {
                        http.RequestServices.GetService<ILogger<AccountingContextService>>()?
                            .LogWarning(verificationError, "Admin company transition result could not be verified.");
                        return AccountingSwitchResult.Uncertain;
                    }

                    http.RequestServices.GetService<ILogger<AccountingContextService>>()?
                        .LogWarning(ex, "Admin company transition left the previous context in place.");
                    if (ex is AccountingContextDeniedException) return AccountingSwitchResult.Denied;
                    if (conflict) return AccountingSwitchResult.Stale;
                    return commitStarted ? AccountingSwitchResult.Uncertain : AccountingSwitchResult.AuditFailed;
                }
            }, allowUnavailableCompany: true, cancellationToken);
        }
        catch (AccountingContextDeniedException) { return AccountingSwitchResult.Denied; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            http.RequestServices.GetService<ILogger<AccountingContextService>>()?
                .LogWarning(ex, "Admin company transition claim could not be confirmed.");
            return AccountingSwitchResult.Uncertain;
        }
    }

    private static bool Matches(AdminCompanyTransition operation, AccountingSwitchTokens.Ticket ticket) =>
        operation.AdminUserId == ticket.UserId && operation.LoginEpoch == ticket.Epoch &&
        operation.SourceCompanyId == ticket.Source && operation.SourceVersion == ticket.Version &&
        operation.TargetCompanyId == ticket.Target && operation.ReturnHome == ticket.ReturnHome;

    private Task<T> InScopeAsync<T>(HttpContext http,
        Func<AccountingRequestContext, CancellationToken, Task<T>> operation, CancellationToken ct)
        => InStateAsync(http, (context, _, _, token) =>
        {
            var admitted = SwitchedAccountingBoundary.AdmittedStamp(http);
            if (admitted != null && admitted != context.Stamp)
                throw new AccountingContextDeniedException();
            return operation(context, token);
        }, allowUnavailableCompany: false, ct);

    private async Task<T> InStateAsync<T>(HttpContext http,
        Func<AccountingRequestContext, AccountingContextState, Action<AccountingContextState>, CancellationToken, Task<T>> operation,
        bool allowUnavailableCompany, CancellationToken ct)
    {
        var session = http.Features.Get<ISessionFeature>()?.Session;
        if (session is null) throw new AccountingContextDeniedException();
        await session.LoadAsync(ct);
        if (session.GetString("DemoMode") == "1" || session.GetString("PrivateMode") == "1")
            throw new AccountingContextDeniedException();
        return await store.ExecuteAsync(session.Id, async (state, publish) =>
        {
            var userId = session.GetInt32("KullaniciId");
            var loginEpoch = session.GetString(AccountingSessionLifecycle.EpochKey);
            if (userId is null or <= 0) throw new AccountingContextDeniedException();
            var actor = await db.Kullanicilar.AsNoTracking()
                .Where(x => x.Id == userId && x.Firma != null && x.Firma.AktifMi)
                .Select(x => new AccountingActor(x.Id, x.KullaniciAdi, x.Rol, x.FirmaId)).SingleOrDefaultAsync(ct);
            if (actor is null) throw new AccountingContextDeniedException();
            // Never initialize a foreign company from a Session snapshot.
            if (state is null)
            {
                if (session.GetInt32("FirmaId") != actor.HomeCompanyId) throw new AccountingContextDeniedException();
                state = new AccountingContextState(actor.UserId, actor.HomeCompanyId, Guid.NewGuid(), loginEpoch);
                publish(state);
            }
            var sessionCompany = session.GetInt32("FirmaId");
            var switchGeneration = switchPolicy?.Generation;
            var companyMatches = state.PortalSwitchGeneration is Guid selectedGeneration
                ? selectedGeneration == switchGeneration && sessionCompany == actor.HomeCompanyId
                : sessionCompany == state.CompanyId;
            if (state.UserId != actor.UserId || state.LoginEpoch != loginEpoch || state.CompanyId <= 0 || state.Version == Guid.Empty ||
                !companyMatches ||
                (!allowUnavailableCompany &&
                 !await db.Firmalar.AsNoTracking().AnyAsync(x => x.Id == state.CompanyId && x.AktifMi, ct)))
                throw new AccountingContextDeniedException();
            if (state.CompanyId != actor.HomeCompanyId && !await portalAuthorization.IsAuthorizedAsync(http, ct))
                throw new AccountingContextDeniedException();
            if (state.PortalSwitchGeneration.HasValue && !IsSwitchEnabled(switchGeneration))
                throw new AccountingContextDeniedException();
            return await operation(new AccountingRequestContext(actor, state.CompanyId, state.Version, switchGeneration), state, publish, ct);
        }, ct);
    }

    private bool IsSwitchEnabled(Guid? expected) => expected.HasValue && switchPolicy?.Generation == expected;

    private static void Validate(AccountingRequestContext context, AccountingContextStamp? expected)
    {
        if (expected is null || expected.Version == Guid.Empty || expected.CompanyId != context.ActiveCompanyId || expected.Version != context.Version)
            throw new AccountingContextDeniedException();
    }
}

public static class AccountingContextRegistration
{
    public static IServiceCollection AddAccountingContextFoundation(this IServiceCollection services)
    {
        services.AddSingleton<IAccountingContextStateStore, LocalAccountingContextStateStore>();
        services.AddScoped<IAccountingContextService, AccountingContextService>();
        services.AddScoped<AccountingSessionLifecycle>();
        services.AddScoped<AccountingManagementAccess>();
        services.AddSingleton<AccountingCompanySwitchPolicy>();
        return services;
    }
}
