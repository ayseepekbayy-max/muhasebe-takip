using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Helpers;

namespace MuhasebeTakip2.App.Pages;

public class LoginModel : PageModel
{
    private readonly AppDbContext _db;

    public LoginModel(AppDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public string KullaniciAdi { get; set; } = "";

    [BindProperty]
    public string Sifre { get; set; } = "";

    public string Hata { get; set; } = "";

    public IActionResult OnGet()
    {
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        KullaniciAdi = (KullaniciAdi ?? "").Trim();
        Sifre = (Sifre ?? "").Trim();

        var kullanici = await _db.Kullanicilar
            .Include(x => x.Firma)
            .FirstOrDefaultAsync(x => x.KullaniciAdi == KullaniciAdi);

        if (kullanici == null)
        {
            Hata = "Kullanıcı adı veya şifre yanlış.";
            return Page();
        }

        bool sifreDogru = PasswordHelper.Verify(Sifre, kullanici.Sifre);

        if (!sifreDogru && kullanici.Sifre == Sifre)
        {
            kullanici.Sifre = PasswordHelper.Hash(Sifre);
            await _db.SaveChangesAsync();
            sifreDogru = true;
        }

        if (!sifreDogru)
        {
            Hata = "Kullanıcı adı veya şifre yanlış.";
            return Page();
        }

        var firma = kullanici.Firma;

            if (firma == null || !firma.AktifMi)
            {
                Hata = "Bu firma hesabı pasif durumda.";
                return Page();
            }

            HttpContext.Session.Clear();

            HttpContext.Session.SetInt32("KullaniciId", kullanici.Id);
            HttpContext.Session.SetInt32("FirmaId", kullanici.FirmaId);
            HttpContext.Session.SetString("KullaniciAdi", kullanici.KullaniciAdi);
            HttpContext.Session.SetString("FirmaAdi", firma.FirmaAdi ?? "Firma");
            HttpContext.Session.SetString("Rol", (kullanici.Rol ?? "").Trim());

            HttpContext.Session.SetString("MenuCariKartlar", firma.MenuCariKartlar ? "1" : "0");
            HttpContext.Session.SetString("MenuKasa", firma.MenuKasa ? "1" : "0");
            HttpContext.Session.SetString("MenuRaporlar", firma.MenuRaporlar ? "1" : "0");
            HttpContext.Session.SetString("MenuCalisanlar", firma.MenuCalisanlar ? "1" : "0");
            HttpContext.Session.SetString("MenuMusteriler", firma.MenuMusteriler ? "1" : "0");
            HttpContext.Session.SetString("MenuStoklar", firma.MenuStoklar ? "1" : "0");
            HttpContext.Session.SetString("MenuMaliyet", firma.MenuMaliyet ? "1" : "0");
            HttpContext.Session.SetString("MenuCekler", firma.MenuCekler ? "1" : "0");
                    return RedirectToPage("/Index");
    }
}