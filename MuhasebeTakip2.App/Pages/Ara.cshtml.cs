using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages;

public class AraModel : PageModel
{
    private readonly AppDbContext _db;

    public AraModel(AppDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public string Q { get; set; } = "";

    public List<AramaSonucu> Sonuclar { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var q = (Q ?? "").Trim();
        if (q.Length < 2)
            return Page();

        var lower = q.ToLower();

        var cariler = await _db.CariKartlar
            .Where(x => x.FirmaId == firmaId.Value &&
                ((x.Unvan ?? "").ToLower().Contains(lower) ||
                 (x.Telefon ?? "").ToLower().Contains(lower) ||
                 (x.VergiNo ?? "").ToLower().Contains(lower)))
            .OrderBy(x => x.Unvan)
            .Take(10)
            .Select(x => new AramaSonucu("Cari", x.Unvan, x.Telefon ?? x.VergiNo ?? "", $"/CariKartlar/Detay/{x.Id}"))
            .ToListAsync();

        var faturalar = await _db.Faturalar
            .Include(x => x.CariKart)
            .Where(x => x.FirmaId == firmaId.Value &&
                ((x.FaturaNo ?? "").ToLower().Contains(lower) ||
                 (x.Aciklama ?? "").ToLower().Contains(lower) ||
                 (x.CariKart != null && (x.CariKart.Unvan ?? "").ToLower().Contains(lower))))
            .OrderByDescending(x => x.Tarih)
            .Take(10)
            .Select(x => new AramaSonucu("Fatura", x.FaturaNo, $"{x.CariKart!.Unvan} - {x.GenelToplam:N2}", $"/Faturalar/Detay/{x.Id}"))
            .ToListAsync();

        var stoklar = await _db.StokUrunler
            .Where(x => x.FirmaId == firmaId.Value &&
                ((x.Ad ?? "").ToLower().Contains(lower) || (x.Kod ?? "").ToLower().Contains(lower)))
            .OrderBy(x => x.Ad)
            .Take(10)
            .Select(x => new AramaSonucu("Stok", x.Ad, x.Kod, $"/Stoklar/Detay/{x.Id}"))
            .ToListAsync();

        var kasa = await _db.KasaHareketleri
            .Include(x => x.CariKart)
            .Where(x => x.FirmaId == firmaId.Value &&
                ((x.Aciklama ?? "").ToLower().Contains(lower) ||
                 (x.CariKart != null && (x.CariKart.Unvan ?? "").ToLower().Contains(lower))))
            .OrderByDescending(x => x.Tarih)
            .Take(10)
            .Select(x => new AramaSonucu("Kasa", x.Aciklama, $"{x.Tarih:dd.MM.yyyy} - {x.Tutar:N2}", "/Kasa/Hareketler"))
            .ToListAsync();

        var cekler = await _db.Cekler
            .Where(x => x.FirmaId == firmaId.Value &&
                ((x.No ?? "").ToLower().Contains(lower) || (x.Aciklama ?? "").ToLower().Contains(lower)))
            .OrderByDescending(x => x.Tarih)
            .Take(10)
            .Select(x => new AramaSonucu("Çek", x.No, $"{x.Tarih:dd.MM.yyyy} - {x.Tutar:N2}", "/Cekler"))
            .ToListAsync();

        Sonuclar.AddRange(cariler);
        Sonuclar.AddRange(faturalar);
        Sonuclar.AddRange(stoklar);
        Sonuclar.AddRange(kasa);
        Sonuclar.AddRange(cekler);

        return Page();
    }

    public record AramaSonucu(string Tur, string Baslik, string Aciklama, string Url);
}