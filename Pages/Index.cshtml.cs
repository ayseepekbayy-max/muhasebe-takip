using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db)
    {
        _db = db;
    }

    public decimal BugunGiris { get; set; }
    public decimal BugunCikis { get; set; }
    public decimal BuAyGiris { get; set; }
    public decimal BuAyCikis { get; set; }
    public decimal KasaBakiye { get; set; }

    public int CariSayisi { get; set; }
    public int CalisanSayisi { get; set; }

    public List<KasaHareket> SonHareketler { get; set; } = new();

    public string? SayfaHata { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        try
        {
            var bugun = DateTime.Today;
            var ayBaslangic = new DateTime(bugun.Year, bugun.Month, 1);
            var yarin = bugun.AddDays(1);

            BugunGiris = await _db.KasaHareketleri
                .Where(x =>
                    x.FirmaId == firmaId.Value &&
                    x.Tarih >= bugun &&
                    x.Tarih < yarin &&
                    x.Tip == HareketTipi.Giris)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            BugunCikis = await _db.KasaHareketleri
                .Where(x =>
                    x.FirmaId == firmaId.Value &&
                    x.Tarih >= bugun &&
                    x.Tarih < yarin &&
                    x.Tip == HareketTipi.Cikis)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            BuAyGiris = await _db.KasaHareketleri
                .Where(x =>
                    x.FirmaId == firmaId.Value &&
                    x.Tarih >= ayBaslangic &&
                    x.Tip == HareketTipi.Giris)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            BuAyCikis = await _db.KasaHareketleri
                .Where(x =>
                    x.FirmaId == firmaId.Value &&
                    x.Tarih >= ayBaslangic &&
                    x.Tip == HareketTipi.Cikis)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            var toplamGiris = await _db.KasaHareketleri
                .Where(x =>
                    x.FirmaId == firmaId.Value &&
                    x.Tip == HareketTipi.Giris)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            var toplamCikis = await _db.KasaHareketleri
                .Where(x =>
                    x.FirmaId == firmaId.Value &&
                    x.Tip == HareketTipi.Cikis)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            KasaBakiye = toplamGiris - toplamCikis;

            CariSayisi = await _db.CariKartlar
                .CountAsync(x => x.FirmaId == firmaId.Value);

            CalisanSayisi = await _db.Calisanlar
                .CountAsync(x => x.FirmaId == firmaId.Value);

            try
            {
                SonHareketler = await _db.KasaHareketleri
                    .Include(x => x.CariKart)
                    .Where(x => x.FirmaId == firmaId.Value)
                    .OrderByDescending(x => x.Tarih)
                    .ThenByDescending(x => x.Id)
                    .Take(10)
                    .ToListAsync();
            }
            catch
            {
                SonHareketler = new List<KasaHareket>();
            }
        }
        catch (Exception ex)
        {
            BugunGiris = 0;
            BugunCikis = 0;
            BuAyGiris = 0;
            BuAyCikis = 0;
            KasaBakiye = 0;
            CariSayisi = 0;
            CalisanSayisi = 0;
            SonHareketler = new List<KasaHareket>();

            SayfaHata = ex.Message;
        }

        return Page();
    }
}