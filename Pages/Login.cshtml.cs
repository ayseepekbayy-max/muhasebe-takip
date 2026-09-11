using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Helpers;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages;

public class LoginModel : PageModel
{
    private readonly AppDbContext _db;

    private readonly PrivateAccessService _privateAccess;

    public LoginModel(AppDbContext db, PrivateAccessService privateAccess)
    {
        _db = db;
        _privateAccess = privateAccess;
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
        var privateResult = _privateAccess.Authenticate(KullaniciAdi, Sifre);
        if (privateResult.IsPrivateUsername)
        {
            if (privateResult.Person is null)
            {
                Hata = "Kullanıcı adı veya şifre hatalı.";
                return Page();
            }

            HttpContext.Session.Clear();
            HttpContext.Session.SetString("PrivateMode", "1");
            HttpContext.Session.SetString("PrivatePerson", privateResult.Person.Number);
            HttpContext.Session.SetString("PrivatePersonName", privateResult.Person.Name);
            return RedirectToPage("/Panel");
        }

        KullaniciAdi = (KullaniciAdi ?? "").Trim();
        Sifre = (Sifre ?? "").Trim();

        var kullanici = await _db.Kullanicilar
            .Include(x => x.Firma)
            .FirstOrDefaultAsync(x =>
            x.KullaniciAdi == KullaniciAdi ||
            x.Email == KullaniciAdi.ToLower());

        if (kullanici == null)
        {
            Hata = "Kullanıcı adı veya şifre yanlış.";
            return Page();
        }

        var sifreKontrolu = PasswordHelper.Verify(kullanici, Sifre, kullanici.Sifre);
        if (!sifreKontrolu.Succeeded)
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

            if (sifreKontrolu.RehashNeeded)
                await TryRehashPasswordAsync(kullanici, Sifre);

            HttpContext.Session.Clear();

            HttpContext.Session.SetInt32("KullaniciId", kullanici.Id);
            HttpContext.Session.SetInt32("FirmaId", kullanici.FirmaId);
            HttpContext.Session.SetString("KullaniciAdi", kullanici.KullaniciAdi);
            HttpContext.Session.SetString("FirmaAdi", firma.FirmaAdi ?? "Firma");
            HttpContext.Session.SetString("Rol", (kullanici.Rol ?? "").Trim());
            HttpContext.Session.Remove("DemoMode");

            HttpContext.Session.SetString("MenuCariKartlar", firma.MenuCariKartlar ? "1" : "0");
            HttpContext.Session.SetString("MenuKasa", firma.MenuKasa ? "1" : "0");
            HttpContext.Session.SetString("MenuRaporlar", firma.MenuRaporlar ? "1" : "0");
            HttpContext.Session.SetString("MenuOdemeler", firma.MenuOdemeler ? "1" : "0");
            HttpContext.Session.SetString("MenuCalisanlar", firma.MenuCalisanlar ? "1" : "0");
            HttpContext.Session.SetString("MenuMusteriler", firma.MenuMusteriler ? "1" : "0");
            HttpContext.Session.SetString("MenuStoklar", firma.MenuStoklar ? "1" : "0");
            HttpContext.Session.SetString("MenuMaliyet", firma.MenuMaliyet ? "1" : "0");
            HttpContext.Session.SetString("MenuCekler", firma.MenuCekler ? "1" : "0");
                    return RedirectToPage("/Index");
    }

    private async Task TryRehashPasswordAsync(Kullanici kullanici, string verifiedPassword)
    {
        var eskiSifre = kullanici.Sifre;
        kullanici.Sifre = PasswordHelper.Hash(kullanici, verifiedPassword);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            // Rehash yazılamazsa doğrulanmış kullanıcının girişini engelleme ve eski kaydı koru.
            kullanici.Sifre = eskiSifre;
            _db.Entry(kullanici).Property(x => x.Sifre).IsModified = false;
        }
    }

    public async Task<IActionResult> OnPostDemoAsync()
    {
        var kullanici = await DemoDataSeeder.PrepareDemoAsync(_db);
        var firma = kullanici.Firma;

        if (firma == null || !firma.AktifMi)
        {
            Hata = "Demo hesabı şu anda kullanılamıyor.";
            return Page();
        }

        HttpContext.Session.Clear();
        HttpContext.Session.SetInt32("KullaniciId", kullanici.Id);
        HttpContext.Session.SetInt32("FirmaId", kullanici.FirmaId);
        HttpContext.Session.SetString("KullaniciAdi", kullanici.KullaniciAdi);
        HttpContext.Session.SetString("FirmaAdi", firma.FirmaAdi ?? "Demo Firma");
        HttpContext.Session.SetString("Rol", "Demo");
        HttpContext.Session.SetString("DemoMode", "1");

        HttpContext.Session.SetString("MenuCariKartlar", firma.MenuCariKartlar ? "1" : "0");
        HttpContext.Session.SetString("MenuKasa", firma.MenuKasa ? "1" : "0");
        HttpContext.Session.SetString("MenuRaporlar", firma.MenuRaporlar ? "1" : "0");
        HttpContext.Session.SetString("MenuOdemeler", firma.MenuOdemeler ? "1" : "0");
        HttpContext.Session.SetString("MenuCalisanlar", firma.MenuCalisanlar ? "1" : "0");
        HttpContext.Session.SetString("MenuMusteriler", firma.MenuMusteriler ? "1" : "0");
        HttpContext.Session.SetString("MenuStoklar", firma.MenuStoklar ? "1" : "0");
        HttpContext.Session.SetString("MenuMaliyet", firma.MenuMaliyet ? "1" : "0");
        HttpContext.Session.SetString("MenuCekler", firma.MenuCekler ? "1" : "0");

        TempData["Mesaj"] = "Demo hesap hazırlandı. Örnek verileri güvenle inceleyebilirsiniz.";
        return RedirectToPage("/Index");
    }
}
