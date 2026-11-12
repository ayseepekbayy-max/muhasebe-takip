using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Services;

internal static class InvoiceScope
{
    public static Task<bool> ForeignInvoiceAsync(AppDbContext db, int company, int id, CancellationToken ct) =>
        db.Faturalar.AsNoTracking().AnyAsync(x => x.Id == id && x.FirmaId != company, ct);

    public static Task<bool> ForeignAccountAsync(AppDbContext db, int company, int? id, CancellationToken ct) =>
        db.KasaHesaplar.AsNoTracking().AnyAsync(x => x.Id == id && x.FirmaId != company, ct);

    public static async Task<bool> RelationsValidAsync(AppDbContext db, IQueryable<Fatura> invoices, int company, CancellationToken ct)
    {
        if (await invoices.AnyAsync(x => x.CariKartId != null &&
            (x.CariKart == null || x.CariKart.FirmaId != company), ct))
            return false;

        var ids = invoices.Select(x => x.Id);
        if (await db.KasaHareketleri.AsNoTracking().AnyAsync(x =>
            x.FaturaId != null && ids.Contains(x.FaturaId.Value) &&
            (x.FirmaId != company ||
             (x.CariKartId != null && (x.CariKart == null || x.CariKart.FirmaId != company)) ||
             (x.KasaHesapId != null && (x.KasaHesap == null || x.KasaHesap.FirmaId != company))), ct))
            return false;

        return !await db.EkDosyalar.AsNoTracking().AnyAsync(x =>
            x.FaturaId != null && ids.Contains(x.FaturaId.Value) &&
            (x.FirmaId != company || x.CariKartId != null), ct);
    }

    public static async Task<bool> CanAccessAsync(AppDbContext db, int company, int id, CancellationToken ct) =>
        !await ForeignInvoiceAsync(db, company, id, ct) &&
        await RelationsValidAsync(db, db.Faturalar.AsNoTracking().Where(x => x.Id == id && x.FirmaId == company), company, ct);
}
