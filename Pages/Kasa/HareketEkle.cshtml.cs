using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using System.Globalization;

namespace MuhasebeTakip2.App.Pages.Kasa;

public class HareketEkleModel : PageModel
{
    private readonly AppDbContext _db;
    public HareketEkleModel(AppDbContext db) => _db = db;

    public List<SelectListItem> CariSecenekleri { get; set; } = new();

    [BindProperty]
    public KasaHareket Hareket { get; set; } = new();

    [BindProperty]
    public string? TutarText { get; set; }

    public async Task<IActionResult> OnGetAsync(int? cariId)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        Hareket.Tarih = DateTime.UtcNow.Date;
        Hareket.Tip = HareketTipi.Giris;
        Hareket.Tutar = 0;
        Hareket.Aciklama = "";
        TutarText = "";

        await YukleCariSecenekleriAsync(firmaId.Value);

        if (cariId.HasValue)
        {
            var cariVarMi = await _db.CariKartlar
                .AnyAsync(x => x.Id == cariId.Value && x.FirmaId == firmaId);

            if (cariVarMi)
                Hareket.CariKartId = cariId.Value;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleCariSecenekleriAsync(firmaId.Value);

        try
        {
            decimal tutar;
            var girilen = (TutarText ?? "").Trim();

            if (string.IsNullOrWhiteSpace(girilen))
            {
                ModelState.AddModelError("", "Tutar boş olamaz.");
                return Page();
            }

            string temizTutar = girilen;

            if (temizTutar.Contains(",") && temizTutar.Contains("."))
            {
                temizTutar = temizTutar.Replace(".", "").Replace(",", ".");
            }
            else if (temizTutar.Contains(","))
            {
                temizTutar = temizTutar.Replace(",", ".");
            }
            else
            {
                temizTutar = temizTutar.Replace(".", "");
            }

            if (!decimal.TryParse(
                    temizTutar,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out tutar) || tutar <= 0)
            {
                ModelState.AddModelError("", "Geçerli bir tutar girin.");
                return Page();
            }

            Hareket.Tutar = tutar;
            Hareket.Aciklama = (Hareket.Aciklama ?? "").Trim();

            if (Hareket.CariKartId == 0)
                Hareket.CariKartId = null;

            if (Hareket.CariKartId.HasValue)
            {
                var secilenCari = await _db.CariKartlar
                    .AnyAsync(x => x.Id == Hareket.CariKartId.Value && x.FirmaId == firmaId);

                if (!secilenCari)
                {
                    ModelState.AddModelError("", "Geçersiz cari seçimi.");
                    return Page();
                }
            }

            Hareket.FirmaId = firmaId.Value;

            // PostgreSQL timestamptz için UTC tarih gönder
            var secilenTarih = Hareket.Tarih.Date;
            Hareket.Tarih = DateTime.SpecifyKind(secilenTarih, DateTimeKind.Utc);

            _db.KasaHareketleri.Add(Hareket);
            await _db.SaveChangesAsync();

            return RedirectToPage("/Kasa/Hareketler");
        }
        catch (DbUpdateException ex)
        {
            var detay = ex.InnerException?.Message ?? ex.Message;
            ModelState.AddModelError("", $"Veritabanı hatası: {detay}");
            return Page();
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", $"Genel hata: {ex.Message}");
            return Page();
        }
    }

    private async Task YukleCariSecenekleriAsync(int firmaId)
    {
        CariSecenekleri = await _db.CariKartlar
            .Where(x => x.FirmaId == firmaId)
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