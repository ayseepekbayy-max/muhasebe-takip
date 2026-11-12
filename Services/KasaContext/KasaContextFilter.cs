using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Services;

public static class KasaRequestContext
{
    private static readonly object Key = new();
    public static AccountingRequestContext? Get(HttpContext http) => http.Items.TryGetValue(Key, out var value) ? value as AccountingRequestContext : null;
    internal static void Set(HttpContext http, AccountingRequestContext? context) { if (context == null) http.Items.Remove(Key); else http.Items[Key] = context; }
    public static int? CompanyId(HttpContext http) => Get(http)?.ActiveCompanyId ??
        ((HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)) && http.Session.GetString("DemoMode") == "1" ? http.Session.GetInt32("FirmaId") : throw new AccountingContextDeniedException());
}

public sealed class KasaScopeException : Exception { }

/// <summary>Pilot filter: hold context gate through the complete Kasa handler and its awaited commits.</summary>
public sealed class KasaContextFilter(IAccountingContextService contexts, AppDbContext db) : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;
    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var http = context.HttpContext;
        var home = context.HandlerInstance is Pages.IndexModel;
        var get = HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method);
        PageHandlerExecutedContext? executedContext = null;
        // The dashboard has unrelated notification handlers; those remain outside this pilot.
        if (home && !get && context.HandlerMethod?.Name != "Sil") { await next(); return; }
        if (get && (http.Session.GetString("DemoMode") == "1" || (home && http.Session.GetInt32("FirmaId") == null)))
        { await next(); return; }
        try
        {
            if (get)
            {
                var current = await contexts.GetCurrentAsync(http, http.RequestAborted);
                await KasaRelationGuard.ValidateStoredAsync(db, current.ActiveCompanyId, http.RequestAborted);
                Publish(current); await ExecuteNext();
            }
            else
            {
                var form = await http.Request.ReadFormAsync(http.RequestAborted);
                if (form["AccountingCompanyId"].Count != 1 || form["AccountingVersion"].Count != 1 ||
                    !int.TryParse(form["AccountingCompanyId"], NumberStyles.None, CultureInfo.InvariantCulture, out var company) ||
                    !Guid.TryParse(form["AccountingVersion"], out var version))
                { context.Result = new StatusCodeResult(409); return; }
                await contexts.WithValidatedWriteAsync(http, new(company, version), async (current, ct) =>
                {
                    if (context.HandlerInstance is Pages.CariKartlar.Detay.IndexModel &&
                        context.HandlerArguments.TryGetValue("id", out var argument) && argument is int id &&
                        (await CariScope.ForeignCardAsync(db, current.ActiveCompanyId, id, ct) ||
                         !await CariScope.RelationsValidAsync(db, current.ActiveCompanyId, id, ct)))
                        throw new KasaScopeException();

                    await KasaRelationGuard.ValidateStoredAsync(db, current.ActiveCompanyId, ct);
                    Publish(current);
                    void Validate(object? sender, SavingChangesEventArgs args) => KasaRelationGuard.ValidateTracked(db, current);
                    db.SavingChanges += Validate;
                    try { return await ExecuteNext(); }
                    finally { db.SavingChanges -= Validate; }
                }, http.RequestAborted);
            }
        }
        catch (AccountingContextDeniedException) { Reject(409); }
        catch (KasaScopeException) { Reject(403); }
        catch (Exception ex) when (InvoicePaymentTransaction.IsConflict(ex)) { Reject(409); }
        finally { KasaRequestContext.Set(http, null); }

        async Task<PageHandlerExecutedContext> ExecuteNext()
        {
            var executed = await next();
            executedContext = executed;
            if (executed.Exception != null && !executed.ExceptionHandled)
            {
                executed.ExceptionHandled = true;
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(executed.Exception).Throw();
            }
            return executed;
        }
        void Reject(int status)
        {
            var result = new StatusCodeResult(status);
            context.Result = result;
            if (executedContext != null)
            {
                executedContext.ExceptionHandled = true;
                executedContext.Exception = null;
                executedContext.Result = result;
            }
        }

        void Publish(AccountingRequestContext current)
        {
            KasaRequestContext.Set(http, current);
            ((PageModel)context.HandlerInstance).ViewData["KasaContextStamp"] = current.Stamp;
        }
    }
}

public static class KasaRelationGuard
{
    public static async Task ValidateStoredAsync(AppDbContext db, int company, CancellationToken ct)
    {
        var moves = db.KasaHareketleri.AsNoTracking().Where(x => x.FirmaId == company);
        var badCari = await moves.AnyAsync(x => x.CariKartId != null && (x.CariKart == null || x.CariKart.FirmaId != company), ct);
        var badAccount = await moves.AnyAsync(x => x.KasaHesapId != null && (x.KasaHesap == null || x.KasaHesap.FirmaId != company), ct);
        var badInvoice = await moves.AnyAsync(x => x.FaturaId != null && (x.Fatura == null || x.Fatura.FirmaId != company ||
            (x.Fatura.CariKartId != null && (x.Fatura.CariKart == null || x.Fatura.CariKart.FirmaId != company))), ct);
        var badTransfer = await db.KasaTransferler.AsNoTracking().AnyAsync(x => x.FirmaId == company &&
            (x.KaynakHesap == null || x.KaynakHesap.FirmaId != company || x.HedefHesap == null || x.HedefHesap.FirmaId != company), ct);
        if (badCari || badAccount || badInvoice || badTransfer) throw new KasaScopeException();
    }
    public static void ValidateTracked(AppDbContext db, AccountingRequestContext context)
    {
        db.ChangeTracker.DetectChanges();
        var company = context.ActiveCompanyId;
        foreach (var entry in db.ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray())
        {
            switch (entry.Entity)
            {
                case KasaHareket x:
                    Require(x.FirmaId == company);
                    if (entry.State != EntityState.Added) Require(entry.OriginalValues.GetValue<int?>("FirmaId") == company);
                    Require(x.CariKartId == null || db.CariKartlar.AsNoTracking().Any(c => c.Id == x.CariKartId && c.FirmaId == company));
                    Require(x.KasaHesapId == null || db.KasaHesaplar.AsNoTracking().Any(c => c.Id == x.KasaHesapId && c.FirmaId == company));
                    Require(x.FaturaId == null || db.Faturalar.AsNoTracking().Any(c => c.Id == x.FaturaId && c.FirmaId == company &&
                        (c.CariKartId == null || (c.CariKart != null && c.CariKart.FirmaId == company))));
                    break;
                case KasaHesap x:
                    Require(x.FirmaId == company);
                    if (entry.State != EntityState.Added) Require(entry.OriginalValues.GetValue<int>("FirmaId") == company);
                    break;
                case KasaTransfer x:
                    Require(x.FirmaId == company);
                    if (entry.State != EntityState.Added) Require(entry.OriginalValues.GetValue<int>("FirmaId") == company);
                    Require(db.KasaHesaplar.AsNoTracking().Any(c => c.Id == x.KaynakHesapId && c.FirmaId == company));
                    Require(db.KasaHesaplar.AsNoTracking().Any(c => c.Id == x.HedefHesapId && c.FirmaId == company));
                    break;
                case Fatura x when entry.State == EntityState.Modified:
                    Require(x.FirmaId == company && entry.OriginalValues.GetValue<int?>("FirmaId") == company);
                    Require(x.CariKartId == null || db.CariKartlar.AsNoTracking().Any(c => c.Id == x.CariKartId && c.FirmaId == company));
                    break;
                case IslemGecmisi x when entry.State == EntityState.Added:
                    Require(x.FirmaId == company && x.KullaniciId == context.Actor.UserId && x.KullaniciAdi == context.Actor.UserName[..Math.Min(100, context.Actor.UserName.Length)]);
                    break;
                default: throw new KasaScopeException(); // No posted navigation graph may introduce other mutations.
            }
        }
    }
    private static void Require(bool allowed) { if (!allowed) throw new KasaScopeException(); }
}
