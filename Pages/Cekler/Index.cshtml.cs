using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages.Cekler;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ICekDurumService _cekDurumService;

    public IndexModel(AppDbContext db, IWebHostEnvironment env, ICekDurumService cekDurumService)
    {
        _db = db;
        _env = env;
        _cekDurumService = cekDurumService;
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

    [BindProperty]
    public string TutarGirisi { get; set; } = "";

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

        var tutarMetni = (TutarGirisi ?? "").Trim();
        // Validate grouping before parsing: NumberStyles.Number alone accepts malformed groups.
        if (tutarMetni.Length > 64 ||
            !Regex.IsMatch(tutarMetni, @"\A(?:[0-9]+|[0-9]{1,3}(?:\.[0-9]{3})+)(?:,[0-9]{1,2})?\z") ||
            !decimal.TryParse(tutarMetni, NumberStyles.Number, CultureInfo.GetCultureInfo("tr-TR"), out var tutar) ||
            tutar <= 0)
        {
            Hata = "Geçerli bir tutar girin. Örnek: 105.000,50";
            ModelState.AddModelError(nameof(TutarGirisi), Hata);
            await YukleAsync(firmaId.Value);
            return Page();
        }
        YeniCek.Tutar = tutar;

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
            string? resimYolu = null;

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

                resimYolu = $"/uploads/cekler/{dosyaAdi}";
            }

            var utcTarih = new DateTime(
                YeniCek.Tarih.Year,
                YeniCek.Tarih.Month,
                YeniCek.Tarih.Day,
                0, 0, 0,
                DateTimeKind.Utc);

             var kayit = new Cek
                {
                    FirmaId = firmaId.Value,
                    No = YeniCek.No,
                    Tarih = utcTarih,
                    Tutar = YeniCek.Tutar,
                    Tip = YeniCek.Tip,
                    Aciklama = YeniCek.Aciklama ?? "",
                    ResimYolu = resimYolu,
                    OlusturmaTarihi = DateTime.UtcNow
                };

            _db.Cekler.Add(kayit);
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
                var fizikselYol = await AccountingDocuments.FindChequeFileAsync(
                    _db, _env, firmaId.Value, cek.Id, HttpContext.RequestAborted);
                // Never remove the document until the database commit is confirmed.
                // A lost commit acknowledgement must leave the file intact.
                await using (var transaction = await _db.Database.BeginTransactionAsync(HttpContext.RequestAborted))
                {
                    _db.Cekler.Remove(cek);
                    await _db.SaveChangesAsync(HttpContext.RequestAborted);
                    await transaction.CommitAsync(HttpContext.RequestAborted);
                }

                if (fizikselYol != null)
                {
                    try
                    {
                        System.IO.File.Delete(fizikselYol);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // The accounting deletion is already committed. Do not undo it or expose paths.
                        TempData["Hata"] = "Çek kaydı silindi ancak belge dosyası temizlenemedi. Dosya temizliği ayrıca kontrol edilmelidir.";
                    }
                }
            }

            return RedirectToPage();
        }
        catch (Exception)
        {
            Hata = "Çek silme işlemi doğrulanamadı. Sonucu kontrol etmeden tekrar denemeyin.";
            await YukleAsync(firmaId.Value);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDurumDegistirAsync(int id, bool odendiMi)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var sonuc = await _cekDurumService.DurumDegistirAsync(firmaId.Value, id, odendiMi);
        TempData[sonuc.Basarili ? "Basari" : "Hata"] = sonuc.Mesaj;
        return RedirectToPage();
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
