using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Filters;
using MuhasebeTakip2.App.Data;

namespace MuhasebeTakip2.App.Services;

public sealed class CariContextFilter(IAccountingContextService contexts, AppDbContext db)
    : AccountingPageContextFilter(contexts, db)
{
    protected override async Task<PageHandlerExecutedContext> RunWriteAsync(PageHandlerExecutingContext context,
        Func<Task<PageHandlerExecutedContext>> execute)
    {
        // Ekstre upload owns its transaction so it can compensate the new file on failure.
        if (context.HandlerInstance is not Pages.CariKartlar.IndexModel)
            return await execute();

        var ct = context.HttpContext.RequestAborted;
        await using var transaction = await InvoicePaymentTransaction.BeginAsync(Db, ct);
        var result = await execute();
        if (result.Exception == null && !result.Canceled)
            await transaction.CommitAsync(ct);
        return result;
    }
}

internal static class CariScope
{
    public static Task<bool> ForeignCardAsync(AppDbContext db, int company, int id, CancellationToken ct) =>
        db.CariKartlar.AsNoTracking().AnyAsync(x => x.Id == id && x.FirmaId != company, ct);

    public static async Task<bool> RelationsValidAsync(AppDbContext db, int company, int? id, CancellationToken ct)
    {
        var invoices = db.Faturalar.AsNoTracking().Where(x =>
            (x.CariKart != null && x.CariKart.FirmaId == company && (id == null || x.CariKartId == id)) ||
            (x.FirmaId == company && (id == null || x.CariKartId == id)));
        if (await invoices.AnyAsync(x => x.FirmaId != company ||
            (x.CariKartId != null && (x.CariKart == null || x.CariKart.FirmaId != company)), ct))
            return false;

        var moves = db.KasaHareketleri.AsNoTracking().Where(x =>
            (x.CariKart != null && x.CariKart.FirmaId == company && (id == null || x.CariKartId == id)) ||
            (x.FirmaId == company && x.CariKartId != null && (id == null || x.CariKartId == id)) ||
            (x.Fatura != null && x.Fatura.CariKart != null && x.Fatura.CariKart.FirmaId == company &&
             (id == null || x.Fatura.CariKartId == id)));
        if (await moves.AnyAsync(x => x.FirmaId != company ||
            (x.CariKartId != null && (x.CariKart == null || x.CariKart.FirmaId != company)) ||
            (x.KasaHesapId != null && (x.KasaHesap == null || x.KasaHesap.FirmaId != company)) ||
            (x.FaturaId != null && (x.Fatura == null || x.Fatura.FirmaId != company ||
             (x.Fatura.CariKartId != null && (x.Fatura.CariKart == null || x.Fatura.CariKart.FirmaId != company)))), ct))
            return false;

        return !await db.EkDosyalar.AsNoTracking().AnyAsync(x =>
            ((x.CariKart != null && x.CariKart.FirmaId == company && (id == null || x.CariKartId == id)) ||
             (x.FirmaId == company && x.CariKartId != null && (id == null || x.CariKartId == id))) &&
            (x.FirmaId != company || x.CariKart == null || x.CariKart.FirmaId != company || x.FaturaId != null), ct);
    }
}
