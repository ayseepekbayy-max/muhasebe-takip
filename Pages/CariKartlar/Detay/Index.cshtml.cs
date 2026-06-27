using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages.CariKartlar.Detay;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IIslemGecmisiService _islemGecmisi;

    public IndexModel(AppDbContext db, IIslemGecmisiService islemGecmisi)
    {
        _db = db;
        _islemGecmisi = islemGecmisi;
    }

    public CariKart? Cari { get; set; }
    public List<KasaHareket> Hareketler { get; set; } = new();
    public decimal ToplamGiris { get; set; }
    public decimal ToplamCikis { get; set; }
    public decimal Bakiye => ToplamGiris - ToplamCikis;

    [BindProperty]
    public decimal Tutar { get; set; }

    [BindProperty]
    public string? Aciklama { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleAsync(id, firmaId.Value);
        return Cari == null ? NotFound() : Page();
    }

    public Task<IActionResult> OnPostTahsilatAsync(int id) =>
        HareketEkleAsync(id, HareketTipi.Giris);

    public Task<IActionResult> OnPostOdemeAsync(int id) =>
        HareketEkleAsync(id, HareketTipi.Cikis);

    private async Task<IActionResult> HareketEkleAsync(int id, HareketTipi tip)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleAsync(id, firmaId.Value);
        if (Cari == null)
            return NotFound();

        if (Tutar <= 0)
        {
            ModelState.AddModelError("", "Tutar 0'dan büyük olmalı.");
            return Page();
        }

        var hareket = new KasaHareket
        {
            FirmaId = firmaId.Value,
            Tarih = DateTime.UtcNow,
            Tip = tip,
            Tutar = Tutar,
            Aciklama = (Aciklama ?? "").Trim(),
            CariKartId = id
        };

        _db.KasaHareketleri.Add(hareket);
        await _db.SaveChangesWithAuditAsync(
            () => _islemGecmisi.KaydetAsync(
                "Kasa",
                "Ekleme",
                $"{Cari.Unvan} için {(tip == HareketTipi.Giris ? "tahsilat" : "ödeme")} eklendi (ID: {hareket.Id}).",
                yeniDeger: IslemGecmisiSnapshots.KasaHareket(hareket)),
            anaKaydiOnceKaydet: true);

        return RedirectToPage(new { id });
    }

    private async Task YukleAsync(int id, int firmaId)
    {
        Cari = await _db.CariKartlar
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId && x.AktifMi);

        if (Cari == null)
            return;

        Hareketler = await _db.KasaHareketleri
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId && x.CariKartId == id)
            .OrderByDescending(x => x.Tarih)
            .ThenByDescending(x => x.Id)
            .Take(50)
            .ToListAsync();

        ToplamGiris = await _db.KasaHareketleri
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId && x.CariKartId == id && x.Tip == HareketTipi.Giris)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        ToplamCikis = await _db.KasaHareketleri
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId && x.CariKartId == id && x.Tip == HareketTipi.Cikis)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;
    }
}
