using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages.Araclar;

public class YedeklemeModel : PageModel
{
    private readonly AppDbContext _db;

    public YedeklemeModel(AppDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public IFormFile? YedekDosyasi { get; set; }

    public string Mesaj { get; set; } = "";
    public string Hata { get; set; } = "";

    public IActionResult OnGet()
    {
        if (HttpContext.Session.GetInt32("FirmaId") == null)
            return RedirectToPage("/Login");

        return Page();
    }

    public async Task<IActionResult> OnGetIndirAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var paket = new YedekPaketi
        {
            OlusturmaTarihi = DateTime.UtcNow,
            Cariler = await _db.CariKartlar.Where(x => x.FirmaId == firmaId.Value).ToListAsync(),
            Stoklar = await _db.StokUrunler.Where(x => x.FirmaId == firmaId.Value).ToListAsync(),
            StokHareketleri = await _db.StokHareketleri.Where(x => x.FirmaId == firmaId.Value).ToListAsync(),
            Faturalar = await _db.Faturalar.Include(x => x.Kalemler).Where(x => x.FirmaId == firmaId.Value).ToListAsync(),
            KasaHareketleri = await _db.KasaHareketleri.Where(x => x.FirmaId == firmaId.Value).ToListAsync(),
            Cekler = await _db.Cekler.Where(x => x.FirmaId == firmaId.Value).ToListAsync()
        };

        var json = JsonSerializer.Serialize(paket, new JsonSerializerOptions { WriteIndented = true });
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return File(bytes, "application/json", $"firmova_yedek_{DateTime.Now:yyyyMMdd_HHmmss}.json");
    }

    public async Task<IActionResult> OnPostGeriYukleAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        if (YedekDosyasi == null || YedekDosyasi.Length == 0)
        {
            Hata = "Lütfen bir yedek dosyası seçin.";
            return Page();
        }

        try
        {
            using var stream = YedekDosyasi.OpenReadStream();
            var paket = await JsonSerializer.DeserializeAsync<YedekPaketi>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (paket == null)
            {
                Hata = "Yedek dosyası okunamadı.";
                return Page();
            }

            var eklenen = 0;

            foreach (var cari in paket.Cariler)
            {
                if (string.IsNullOrWhiteSpace(cari.Unvan))
                    continue;

                var varMi = await _db.CariKartlar.AnyAsync(x => x.FirmaId == firmaId.Value && x.Unvan == cari.Unvan);
                if (varMi)
                    continue;

                cari.Id = 0;
                cari.FirmaId = firmaId.Value;
                cari.Firma = null;
                _db.CariKartlar.Add(cari);
                eklenen++;
            }

            foreach (var stok in paket.Stoklar)
            {
                if (string.IsNullOrWhiteSpace(stok.Ad))
                    continue;

                var varMi = await _db.StokUrunler.AnyAsync(x => x.FirmaId == firmaId.Value && x.Ad == stok.Ad && x.Kod == stok.Kod);
                if (varMi)
                    continue;

                stok.Id = 0;
                stok.FirmaId = firmaId.Value;
                stok.Firma = null;
                _db.StokUrunler.Add(stok);
                eklenen++;
            }

            foreach (var fatura in paket.Faturalar)
            {
                if (string.IsNullOrWhiteSpace(fatura.FaturaNo))
                    continue;

                var varMi = await _db.Faturalar.AnyAsync(x => x.FirmaId == firmaId.Value && x.FaturaNo == fatura.FaturaNo);
                if (varMi)
                    continue;

                fatura.Id = 0;
                fatura.FirmaId = firmaId.Value;
                fatura.Firma = null;
                fatura.CariKart = null;
                foreach (var kalem in fatura.Kalemler)
                {
                    kalem.Id = 0;
                    kalem.FaturaId = 0;
                    kalem.Fatura = null;
                }
                _db.Faturalar.Add(fatura);
                eklenen++;
            }

            await _db.SaveChangesAsync();
            Mesaj = $"Güvenli geri yükleme tamamlandı. Eklenen ana kayıt: {eklenen}. Mevcut kayıtlar korunup tekrar eklenmedi.";
        }
        catch (Exception ex)
        {
            Hata = $"Geri yükleme sırasında hata oluştu: {ex.Message}";
        }

        return Page();
    }

    public class YedekPaketi
    {
        public DateTime OlusturmaTarihi { get; set; }
        public List<CariKart> Cariler { get; set; } = new();
        public List<StokUrun> Stoklar { get; set; } = new();
        public List<StokHareket> StokHareketleri { get; set; } = new();
        public List<Fatura> Faturalar { get; set; } = new();
        public List<KasaHareket> KasaHareketleri { get; set; } = new();
        public List<Cek> Cekler { get; set; } = new();
    }
}