using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Services;

internal static class CustomerScope
{
    public static Task<bool> ForeignCustomerAsync(AppDbContext db, int company, int id, CancellationToken ct) =>
        db.Musteriler.AsNoTracking().AnyAsync(x => x.Id == id && x.FirmaId != company, ct);

    public static async Task<bool> RelationsValidAsync(AppDbContext db, int company, int? customer, CancellationToken ct)
    {
        var jobs = db.MusteriIsler.AsNoTracking()
            .Where(x => (customer == null || x.MusteriId == customer) &&
                        (x.FirmaId == company || (x.Musteri != null && x.Musteri.FirmaId == company)));
        if (await jobs.AnyAsync(x => x.FirmaId != company || x.Musteri == null || x.Musteri.FirmaId != company, ct))
            return false;

        var jobIds = jobs.Select(x => x.Id);
        return !await db.MusteriMasraflar.AsNoTracking().AnyAsync(x =>
            (jobIds.Contains(x.MusteriIsId) ||
             (customer == null && x.FirmaId == company)) &&
            (x.FirmaId != company || x.MusteriIs == null || x.MusteriIs.FirmaId != company ||
             x.MusteriIs.Musteri == null || x.MusteriIs.Musteri.FirmaId != company), ct);
    }

    public static async Task<IActionResult> RunWriteAsync(AppDbContext db, Func<Task<IActionResult>> operation, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql())
            return await InvoicePaymentTransaction.RunAsync(db, operation, ct);

        // Read each statement after taking the parent lock, including any children
        // committed by the previous lock holder. All customer mutations use this order.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var result = await operation();
        await transaction.CommitAsync(ct);
        return result;
    }

    // Every customer write acquires the parent first. SQLite already holds a writer
    // transaction. PostgreSQL takes the same row lock for archive and detail writes.
    public static async Task LockCustomerAsync(AppDbContext db, int company, int id, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT 1 FROM "Musteriler" WHERE "FirmaId" = {company} AND "Id" = {id} FOR UPDATE""", ct);
    }

    public static async Task LockAccountAsync(AppDbContext db, int company, int? id, CancellationToken ct)
    {
        if (id.HasValue && db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT 1 FROM "KasaHesaplar" WHERE "FirmaId" = {company} AND "Id" = {id.Value} FOR SHARE""", ct);
    }

    public static object ExpenseSnapshot(MusteriMasraf x) =>
        new { x.Id, x.FirmaId, x.MusteriIsId, x.Tarih, x.Aciklama, x.Tutar };

    public static object JobSnapshot(MusteriIs x) =>
        new { x.Id, x.FirmaId, x.MusteriId, x.Tarih, x.IsAdi, x.Gelir, Masraflar = x.Masraflar.Select(ExpenseSnapshot).ToArray() };
}
