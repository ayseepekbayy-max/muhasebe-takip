using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Helpers;

namespace MuhasebeTakip2.App.Pages;

public class SifreSifirlaModel : PageModel
{
    private readonly AppDbContext _db;

    public SifreSifirlaModel(AppDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public string Email { get; set; } = "";

    [BindProperty]
    public string Kod { get; set; } = "";

    [BindProperty]
    public string YeniSifre { get; set; } = "";

    [BindProperty]
    public string YeniSifreTekrar { get; set; } = "";

    public string Hata { get; set; } = "";
    public string Mesaj { get; set; } = "";

    public IActionResult OnGet(string email)
    {
        Email = (email ?? "").Trim().ToLowerInvariant();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Email = (Email ?? "").Trim().ToLowerInvariant();
        Kod = (Kod ?? "").Trim();
        YeniSifre = (YeniSifre ?? "").Trim();
        YeniSifreTekrar = (YeniSifreTekrar ?? "").Trim();

        if (string.IsNullOrWhiteSpace(Email))
        {
            Hata = "Mail adresi bulunamadı.";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Kod))
        {
            Hata = "Kod boş olamaz.";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(YeniSifre))
        {
            Hata = "Yeni şifre boş olamaz.";
            return Page();
        }

        if (YeniSifre.Length < 4)
        {
            Hata = "Şifre en az 4 karakter olmalıdır.";
            return Page();
        }

        if (YeniSifre != YeniSifreTekrar)
        {
            Hata = "Şifreler eşleşmiyor.";
            return Page();
        }

        var kullanici = await _db.Kullanicilar
            .FirstOrDefaultAsync(x =>
                x.Email == Email &&
                x.SifreSifirlamaKodu == Kod);

        if (kullanici == null)
        {
            Hata = "Kod hatalı.";
            return Page();
        }

        if (kullanici.SifreSifirlamaKodGecerlilik == null ||
            kullanici.SifreSifirlamaKodGecerlilik < DateTime.UtcNow)
        {
            Hata = "Kodun süresi dolmuş. Lütfen yeniden kod alın.";
            return Page();
        }

        kullanici.Sifre = PasswordHelper.Hash(kullanici, YeniSifre);
        kullanici.SifreSifirlamaKodu = null;
        kullanici.SifreSifirlamaKodGecerlilik = null;

        await _db.SaveChangesAsync();

        TempData["Mesaj"] = "Şifreniz başarıyla değiştirildi. Yeni şifrenizle giriş yapabilirsiniz.";

        return RedirectToPage("/Login");
    }
}
