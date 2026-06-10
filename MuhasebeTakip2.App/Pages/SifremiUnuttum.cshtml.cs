using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Services;
using System.Security.Cryptography;

namespace MuhasebeTakip2.App.Pages;

public class SifremiUnuttumModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly EmailService _emailService;

    public SifremiUnuttumModel(AppDbContext db, EmailService emailService)
    {
        _db = db;
        _emailService = emailService;
    }

    [BindProperty]
    public string Email { get; set; } = "";

    public string Hata { get; set; } = "";
    public string Mesaj { get; set; } = "";

    public IActionResult OnGet()
    {
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Email = (Email ?? "").Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(Email))
        {
            Hata = "Mail adresi boş olamaz.";
            return Page();
        }

        var kullanici = await _db.Kullanicilar
            .FirstOrDefaultAsync(x => x.Email == Email);

        if (kullanici == null)
        {
            Hata = "Bu mail adresiyle kayıtlı kullanıcı bulunamadı.";
            return Page();
        }

        var kod = RandomNumberGenerator
            .GetInt32(100000, 999999)
            .ToString();

        kullanici.SifreSifirlamaKodu = kod;
        kullanici.SifreSifirlamaKodGecerlilik = DateTime.UtcNow.AddMinutes(10);

        await _db.SaveChangesAsync();

       try
{
    await _emailService.SendAsync(
        Email,
        "FirmovaAI Şifre Sıfırlama Kodu",
        $"Şifre sıfırlama kodunuz: {kod}\n\nBu kod 10 dakika geçerlidir.");
}
catch (Exception ex)
{
    Hata = "Mail gönderilirken hata oluştu: " + ex.Message;
    return Page();
}

return RedirectToPage("/SifreSifirla", new { email = Email });
    }
}