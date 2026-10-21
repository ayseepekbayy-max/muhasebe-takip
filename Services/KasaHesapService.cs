using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
namespace MuhasebeTakip2.App.Services;

public static class KasaHesapService
{
    public const string SecimHatasi = "Geçerli bir nakit kasa veya banka hesabı seçin. Hesap yoksa Kasa/Banka Hesapları bölümünden ekleyin.";
    public static Task<bool> GecerliMiAsync(AppDbContext db, int firmaId, int? hesapId) =>
        db.KasaHesaplar.AnyAsync(x => x.Id == hesapId && x.FirmaId == firmaId && x.AktifMi);
    public static async Task<List<SelectListItem>> SeceneklerAsync(AppDbContext db, int firmaId)
    {
        var rows = await db.KasaHesaplar.AsNoTracking().Where(x => x.FirmaId == firmaId && x.AktifMi).OrderBy(x => x.Ad).ToListAsync();
        return new[] { new SelectListItem("Hesap seçin", "") }.Concat(rows.Select(x =>
            new SelectListItem($"{x.Ad} ({(x.Tur == KasaHesapTuru.Nakit ? "Nakit" : "Banka")})", x.Id.ToString()))).ToList();
    }
    public sealed record BakiyeSatiri(KasaHesap Hesap, decimal Giris, decimal Cikis, decimal GelenTransfer, decimal GidenTransfer)
    {
        public decimal Bakiye => Hesap.AcilisBakiyesi + Giris - Cikis + GelenTransfer - GidenTransfer;
    }
    public static async Task<List<BakiyeSatiri>> BakiyelerAsync(AppDbContext db, int firmaId)
    {
        var accounts = await db.KasaHesaplar.AsNoTracking().Where(x => x.FirmaId == firmaId).OrderBy(x => x.Ad).ToListAsync();
        var cash = await db.KasaHareketleri.AsNoTracking().Where(x => x.FirmaId == firmaId && x.KasaHesapId != null)
            .GroupBy(x => new { x.KasaHesapId, x.Tip }).Select(g => new { g.Key.KasaHesapId, g.Key.Tip, Total = g.Sum(x => x.Tutar) }).ToListAsync();
        var incoming = await db.KasaTransferler.AsNoTracking().Where(x => x.FirmaId == firmaId).GroupBy(x => x.HedefHesapId)
            .Select(g => new { Id = g.Key, Total = g.Sum(x => x.Tutar) }).ToDictionaryAsync(x => x.Id, x => x.Total);
        var outgoing = await db.KasaTransferler.AsNoTracking().Where(x => x.FirmaId == firmaId).GroupBy(x => x.KaynakHesapId)
            .Select(g => new { Id = g.Key, Total = g.Sum(x => x.Tutar) }).ToDictionaryAsync(x => x.Id, x => x.Total);
        return accounts.Select(a => new BakiyeSatiri(a,
            cash.Where(x => x.KasaHesapId == a.Id && x.Tip == HareketTipi.Giris).Sum(x => x.Total),
            cash.Where(x => x.KasaHesapId == a.Id && x.Tip == HareketTipi.Cikis).Sum(x => x.Total),
            incoming.GetValueOrDefault(a.Id), outgoing.GetValueOrDefault(a.Id))).ToList();
    }
    public static async Task<decimal> AtanmamisBakiyeAsync(AppDbContext db, int firmaId) =>
        (await db.KasaHareketleri.Where(x => x.FirmaId == firmaId && x.KasaHesapId == null && x.Tip == HareketTipi.Giris).SumAsync(x => (decimal?)x.Tutar) ?? 0) -
        (await db.KasaHareketleri.Where(x => x.FirmaId == firmaId && x.KasaHesapId == null && x.Tip == HareketTipi.Cikis).SumAsync(x => (decimal?)x.Tutar) ?? 0);
}
