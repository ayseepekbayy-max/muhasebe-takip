using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Helpers;

namespace MuhasebeTakip2.App.Pages;

public class RegisterModel : PageModel
{
    private readonly AppDbContext _db;

    public RegisterModel(AppDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public string FirmaAdi { get; set; } = "";

    [BindProperty]
    public string KullaniciAdi { get; set; } = "";

    [BindProperty]
    public string Sifre { get; set; } = "";

    public string Hata { get; set; } = "";

    public IActionResult OnGet()
    {
        HttpContext.Session.Clear();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        FirmaAdi = (FirmaAdi ?? "").Trim();
        KullaniciAdi = (KullaniciAdi ?? "").Trim();
        Sifre = (Sifre ?? "").Trim();

        if (string.IsNullOrWhiteSpace(FirmaAdi))
        {
            Hata = "Firma adı boş olamaz.";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(KullaniciAdi))
        {
            Hata = "Kullanıcı adı boş olamaz.";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Sifre))
        {
            Hata = "Şifre boş olamaz.";
            return Page();
        }

        if (Sifre.Length < 4)
        {
            Hata = "Şifre en az 4 karakter olmalıdır.";
            return Page();
        }

        var kullaniciVarMi = await _db.Kullanicilar.AnyAsync(x => x.KullaniciAdi == KullaniciAdi);
        if (kullaniciVarMi)
        {
            Hata = "Bu kullanıcı adı zaten kullanılıyor.";
            return Page();
        }

        var firmaVarMi = await _db.Firmalar.AnyAsync(x => x.FirmaAdi == FirmaAdi);
        if (firmaVarMi)
        {
            Hata = "Bu firma adı zaten kayıtlı.";
            return Page();
        }

        using var transaction = await _db.Database.BeginTransactionAsync();

        try
        {
            var firma = new Firma
            {
                FirmaAdi = FirmaAdi,
                AktifMi = true
            };

            _db.Firmalar.Add(firma);
            await _db.SaveChangesAsync();

            var kullanici = new Kullanici
            {
                KullaniciAdi = KullaniciAdi,
                Sifre = PasswordHelper.Hash(Sifre),
                FirmaId = firma.Id,
                Rol = "Admin"
            };

            _db.Kullanicilar.Add(kullanici);
            await _db.SaveChangesAsync();

            await transaction.CommitAsync();

            HttpContext.Session.Clear();

            return RedirectToPage("/Login");
        }
        catch
        {
            await transaction.RollbackAsync();
            Hata = "Kayıt sırasında bir hata oluştu.";
            return Page();
        }
    }
}