using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages.Kasa;

public class HareketDuzenleModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IIslemGecmisiService _islemGecmisi;

    public HareketDuzenleModel(
        AppDbContext db,
        IIslemGecmisiService islemGecmisi)
    {
        _db = db;
        _islemGecmisi = islemGecmisi;
    }

    public List<SelectListItem> HesapSecenekleri { get; set; } = new();

    public List<SelectListItem> CariSecenekleri { get; set; } = new();

    [BindProperty]
    public KasaHareket Hareket { get; set; } = new();

    public string Hata { get; set; } = "";
    public string Mesaj { get; set; } = "";

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var h = await _db.KasaHareketleri
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId);

        if (h == null)
            return NotFound();

        Hareket = h;

        await YukleCariSecenekleriAsync(firmaId.Value);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleCariSecenekleriAsync(firmaId.Value);

        if (!await KasaHesapService.GecerliMiAsync(_db, firmaId.Value, Hareket.KasaHesapId))
        {
            Hata = KasaHesapService.SecimHatasi;
            return Page();
        }

        if (Hareket.Tutar <= 0)
        {
            Hata = "Tutar 0'dan büyük olmalı.";
            return Page();
        }

        if (!Enum.IsDefined(typeof(HareketTipi), Hareket.Tip))
        {
            Hata = "Geçersiz hareket tipi.";
            return Page();
        }

        Hareket.Aciklama = (Hareket.Aciklama ?? "").Trim();

        if (Hareket.CariKartId == 0)
            Hareket.CariKartId = null;

        if (Hareket.CariKartId.HasValue)
        {
            var secilenCari = await _db.CariKartlar
                .AnyAsync(x => x.Id == Hareket.CariKartId.Value && x.FirmaId == firmaId && x.AktifMi);

            if (!secilenCari)
            {
                Hata = "Geçersiz cari seçimi.";
                return Page();
            }
        }

        var dbHareket = await _db.KasaHareketleri
            .FirstOrDefaultAsync(x => x.Id == Hareket.Id && x.FirmaId == firmaId);

        if (dbHareket == null)
            return NotFound();

        Fatura? bagliFatura = null;
        object? eskiFaturaDegeri = null;
        if (dbHareket.FaturaId.HasValue)
        {
            bagliFatura = await _db.Faturalar.FirstOrDefaultAsync(x =>
                x.Id == dbHareket.FaturaId.Value && x.FirmaId == firmaId.Value);
            if (bagliFatura == null)
            {
                Hata = "Hareketin bağlı olduğu fatura bulunamadı.";
                return Page();
            }
            var hata = FaturaOdemeTutarliligi.Dogrula(bagliFatura, dbHareket,
                Hareket.Tutar, Hareket.Tip, Hareket.CariKartId);
            if (hata != null)
            {
                Hata = hata;
                return Page();
            }
            eskiFaturaDegeri = IslemGecmisiSnapshots.Fatura(bagliFatura);
            FaturaOdemeTutarliligi.Uygula(bagliFatura, dbHareket.Tutar, Hareket.Tutar);
        }

        var eskiDeger = KasaDegeri(dbHareket);

        var secilenTarih = Hareket.Tarih.Date;
        dbHareket.Tarih = DateTime.SpecifyKind(secilenTarih, DateTimeKind.Utc);

        dbHareket.KasaHesapId = Hareket.KasaHesapId;
        dbHareket.Tip = Hareket.Tip;
        dbHareket.Tutar = Hareket.Tutar;
        dbHareket.Aciklama = Hareket.Aciklama;
        dbHareket.CariKartId = Hareket.CariKartId;

        await _db.SaveChangesWithAuditAsync(async () =>
        {
            await _islemGecmisi.KaydetAsync(
                "Kasa", "Düzenleme", $"Kasa hareketi güncellendi (ID: {dbHareket.Id}).",
                eskiDeger, KasaDegeri(dbHareket));
            if (bagliFatura != null)
                await _islemGecmisi.KaydetAsync(
                    "Faturalar", "Ödeme Düzeltme", $"{bagliFatura.FaturaNo} faturasının ödemesi kasa hareketiyle güncellendi.",
                    eskiFaturaDegeri, IslemGecmisiSnapshots.Fatura(bagliFatura));
        }, anaKaydiOnceKaydet: false);
        Mesaj = "Kasa hareketi güncellendi.";

        return RedirectToPage("/Kasa/Hareketler");
    }

    private static object KasaDegeri(KasaHareket hareket) => new
    {
        hareket.Id,
        hareket.Tarih,
        Tip = hareket.Tip.ToString(),
        hareket.Tutar,
        hareket.Aciklama,
        hareket.CariKartId,
        hareket.FaturaId,
        hareket.KasaHesapId
    };

    private async Task YukleCariSecenekleriAsync(int firmaId)
    {
        HesapSecenekleri = await KasaHesapService.SeceneklerAsync(_db, firmaId);
        CariSecenekleri = await _db.CariKartlar
            .Where(x => x.FirmaId == firmaId && x.AktifMi)
            .OrderBy(x => x.Unvan)
            .Select(x => new SelectListItem
            {
                Value = x.Id.ToString(),
                Text = $"{x.Unvan} ({x.Tip})"
            })
            .ToListAsync();

        CariSecenekleri.Insert(0, new SelectListItem
        {
            Value = "",
            Text = "Cari seç (opsiyonel)"
        });
    }
}
