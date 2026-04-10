using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages.Cekler;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    public IndexModel(AppDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    public List<Cek> Alinacaklar { get; set; } = new();
    public List<Cek> Odenecekler { get; set; } = new();

    [BindProperty]
    public Cek YeniCek { get; set; } = new()
    {
        Tarih = DateTime.Today
    };

    [BindProperty]
    public IFormFile? CekResmi { get; set; }

    public string Mesaj { get; set; } = "";
    public string Hata { get; set; } = "";

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleAsync(firmaId.Value);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        YeniCek.No = (YeniCek.No ?? "").Trim();
        YeniCek.Aciklama = (YeniCek.Aciklama ?? "").Trim();

        if (string.IsNullOrWhiteSpace(YeniCek.No) || YeniCek.Tutar <= 0)
        {
            Hata = "No ve tutar alanlarını doğru doldurun.";
            await YukleAsync(firmaId.Value);
            return Page();
        }

        var cekNoVarMi = await _db.Cekler.AnyAsync(x =>
            x.FirmaId == firmaId.Value && x.No == YeniCek.No);

        if (cekNoVarMi)
        {
            Hata = "Bu çek numarası zaten kayıtlı.";
            await YukleAsync(firmaId.Value);
            return Page();
        }

        try
        {
            YeniCek.FirmaId = firmaId.Value;

            // PostgreSQL için tarihi UTC yap
            YeniCek.Tarih = DateTime.SpecifyKind(YeniCek.Tarih.Date, DateTimeKind.Utc);

            if (CekResmi != null && CekResmi.Length > 0)
            {
                var izinliUzantilar = new[] { ".jpg", ".jpeg", ".png", ".webp", ".pdf" };
                var ext = Path.GetExtension(CekResmi.FileName).ToLower();

                if (!izinliUzantilar.Contains(ext))
                {
                    Hata = "Sadece jpg, jpeg, png, webp veya pdf dosyaları yüklenebilir.";
                    await YukleAsync(firmaId.Value);
                    return Page();
                }

                if (CekResmi.Length > 5 * 1024 * 1024)
                {
                    Hata = "Dosya boyutu en fazla 5 MB olabilir.";
                    await YukleAsync(firmaId.Value);
                    return Page();
                }

                var klasor = Path.Combine(_env.WebRootPath, "uploads", "cekler");
                Directory.CreateDirectory(klasor);

                var dosyaAdi = $"{Guid.NewGuid()}{ext}";
                var tamYol = Path.Combine(klasor, dosyaAdi);

                using (var stream = new FileStream(tamYol, FileMode.Create))
                {
                    await CekResmi.CopyToAsync(stream);
                }

                YeniCek.ResimYolu = $"/uploads/cekler/{dosyaAdi}";
            }

            _db.Cekler.Add(YeniCek);
            await _db.SaveChangesAsync();

            Mesaj = "Çek kaydedildi.";
            YeniCek = new Cek { Tarih = DateTime.Today };

            await YukleAsync(firmaId.Value);
            return Page();
        }
        catch (Exception ex)
        {
            var detay = ex.InnerException?.Message ?? ex.Message;
            Hata = "Çek kaydedilirken hata oluştu: " + detay;
            await YukleAsync(firmaId.Value);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostSilAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        try
        {
            var cek = await _db.Cekler
                .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId.Value);

            if (cek != null)
            {
                if (!string.IsNullOrWhiteSpace(cek.ResimYolu))
                {
                    var fizikselYol = Path.Combine(
                        _env.WebRootPath,
                        cek.ResimYolu.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString())
                    );

                    if (System.IO.File.Exists(fizikselYol))
                        System.IO.File.Delete(fizikselYol);
                }

                _db.Cekler.Remove(cek);
                await _db.SaveChangesAsync();
            }

            return RedirectToPage();
        }
        catch (Exception ex)
        {
            var detay = ex.InnerException?.Message ?? ex.Message;
            Hata = "Çek silinirken hata oluştu: " + detay;
            await YukleAsync(firmaId.Value);
            return Page();
        }
    }

    private async Task YukleAsync(int firmaId)
    {
        Alinacaklar = await _db.Cekler
            .Where(x => x.FirmaId == firmaId && x.Tip == CekTipi.Alinacak)
            .OrderBy(x => x.Tarih)
            .ToListAsync();

        Odenecekler = await _db.Cekler
            .Where(x => x.FirmaId == firmaId && x.Tip == CekTipi.Odenecek)
            .OrderBy(x => x.Tarih)
            .ToListAsync();
    }
}