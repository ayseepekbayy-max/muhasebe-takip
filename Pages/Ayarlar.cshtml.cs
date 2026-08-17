using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Helpers;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages;

public class AyarlarModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IEmailService _emailService;
    private readonly IIslemGecmisiService _islemGecmisi;

    public AyarlarModel(
        AppDbContext db,
        IWebHostEnvironment env,
        IEmailService emailService,
        IIslemGecmisiService islemGecmisi)
    {
        _db = db;
        _env = env;
        _emailService = emailService;
        _islemGecmisi = islemGecmisi;
    }

    [BindProperty] public string KullaniciAdi { get; set; } = "";
    [BindProperty] public string MevcutSifre { get; set; } = "";
    [BindProperty] public string YeniSifre { get; set; } = "";
    [BindProperty] public string YeniSifreTekrar { get; set; } = "";
    [BindProperty] public string KullaniciEmail { get; set; } = "";
    [BindProperty] public bool OdemeEmailBildirimiAktifMi { get; set; } = true;

    [BindProperty] public string FirmaAdi { get; set; } = "";
    [BindProperty] public string Adres { get; set; } = "";
    [BindProperty] public string Telefon { get; set; } = "";
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string VergiDairesi { get; set; } = "";
    [BindProperty] public string VergiNo { get; set; } = "";
    [BindProperty] public IFormFile? LogoDosyasi { get; set; }
    public string LogoYolu { get; set; } = "";

    [BindProperty] public bool MenuCariKartlar { get; set; } = true;
    [BindProperty] public bool MenuKasa { get; set; } = true;
    [BindProperty] public bool MenuRaporlar { get; set; } = true;
    [BindProperty] public bool MenuOdemeler { get; set; } = true;
    [BindProperty] public bool MenuCalisanlar { get; set; } = true;
    [BindProperty] public bool MenuMusteriler { get; set; } = true;
    [BindProperty] public bool MenuStoklar { get; set; } = true;
    [BindProperty] public bool MenuMaliyet { get; set; } = true;
    [BindProperty] public bool MenuCekler { get; set; } = true;

    public string Mesaj { get; set; } = "";
    public string Hata { get; set; } = "";

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        var kullaniciId = HttpContext.Session.GetInt32("KullaniciId");
        if (firmaId == null || kullaniciId == null)
            return RedirectToPage("/Login");

        await BilgileriYukle(firmaId.Value, kullaniciId.Value);
        return Page();
    }

    public async Task<IActionResult> OnPostFirmaGuncelleAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        var kullaniciId = HttpContext.Session.GetInt32("KullaniciId");
        if (firmaId == null || kullaniciId == null)
            return RedirectToPage("/Login");

        var firma = await _db.Firmalar.FirstOrDefaultAsync(x => x.Id == firmaId.Value);
        if (firma == null)
            return RedirectToPage("/Login");

        FirmaAdi = (FirmaAdi ?? "").Trim();
        if (string.IsNullOrWhiteSpace(FirmaAdi))
        {
            Hata = "Firma adı boş olamaz.";
            await BilgileriYukle(firmaId.Value, kullaniciId.Value);
            return Page();
        }

        firma.FirmaAdi = FirmaAdi;
        firma.Adres = (Adres ?? "").Trim();
        firma.Telefon = (Telefon ?? "").Trim();
        firma.Email = (Email ?? "").Trim();
        firma.VergiDairesi = (VergiDairesi ?? "").Trim();
        firma.VergiNo = (VergiNo ?? "").Trim();

        if (LogoDosyasi != null && LogoDosyasi.Length > 0)
        {
            var uzanti = Path.GetExtension(LogoDosyasi.FileName).ToLowerInvariant();
            var izinli = new[] { ".png", ".jpg", ".jpeg", ".webp" };
            if (!izinli.Contains(uzanti))
            {
                Hata = "Logo için PNG, JPG veya WEBP yükleyin.";
                await BilgileriYukle(firmaId.Value, kullaniciId.Value);
                return Page();
            }

            if (LogoDosyasi.Length > 1024 * 1024)
            {
                Hata = "Logo dosyası en fazla 1 MB olabilir.";
                await BilgileriYukle(firmaId.Value, kullaniciId.Value);
                return Page();
            }

            var icerikTipi = uzanti switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                _ => LogoDosyasi.ContentType
            };

            await using var ms = new MemoryStream();
            await LogoDosyasi.CopyToAsync(ms);
            firma.LogoYolu = $"data:{icerikTipi};base64,{Convert.ToBase64String(ms.ToArray())}";
        }

        await _db.SaveChangesAsync();
        HttpContext.Session.SetString("FirmaAdi", firma.FirmaAdi);
        Mesaj = "Firma bilgileri güncellendi.";
        await BilgileriYukle(firmaId.Value, kullaniciId.Value);
        return Page();
    }

    public async Task<IActionResult> OnPostKullaniciGuncelleAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        var kullaniciId = HttpContext.Session.GetInt32("KullaniciId");
        if (firmaId == null || kullaniciId == null)
            return RedirectToPage("/Login");

        var kullanici = await _db.Kullanicilar.FirstOrDefaultAsync(x => x.Id == kullaniciId.Value && x.FirmaId == firmaId.Value);
        if (kullanici == null)
            return RedirectToPage("/Login");

        KullaniciAdi = (KullaniciAdi ?? "").Trim();
        if (string.IsNullOrWhiteSpace(KullaniciAdi))
        {
            Hata = "Kullanıcı adı boş olamaz.";
            await BilgileriYukle(firmaId.Value, kullaniciId.Value);
            return Page();
        }

        var ayniAdKullananVar = await _db.Kullanicilar.AnyAsync(x => x.Id != kullanici.Id && x.KullaniciAdi == KullaniciAdi);
        if (ayniAdKullananVar)
        {
            Hata = "Bu kullanıcı adı zaten kullanılıyor.";
            await BilgileriYukle(firmaId.Value, kullaniciId.Value);
            return Page();
        }

        kullanici.KullaniciAdi = KullaniciAdi;
        if (!string.IsNullOrWhiteSpace(YeniSifre) || !string.IsNullOrWhiteSpace(YeniSifreTekrar))
        {
            if (string.IsNullOrWhiteSpace(MevcutSifre))
            {
                Hata = "Şifre değiştirmek için mevcut şifrenizi girin.";
                await BilgileriYukle(firmaId.Value, kullaniciId.Value);
                return Page();
            }

            var mevcutSifreKontrolu = PasswordHelper.Verify(kullanici, MevcutSifre, kullanici.Sifre);
            if (!mevcutSifreKontrolu.Succeeded)
            {
                Hata = "Mevcut şifre yanlış.";
                await BilgileriYukle(firmaId.Value, kullaniciId.Value);
                return Page();
            }

            if (YeniSifre != YeniSifreTekrar || YeniSifre.Length < 4)
            {
                Hata = "Yeni şifreler aynı olmalı ve en az 4 karakter içermeli.";
                await BilgileriYukle(firmaId.Value, kullaniciId.Value);
                return Page();
            }

            kullanici.Sifre = PasswordHelper.Hash(kullanici, YeniSifre);
        }

        await _db.SaveChangesAsync();
        HttpContext.Session.SetString("KullaniciAdi", kullanici.KullaniciAdi);
        Mesaj = "Kullanıcı bilgileri güncellendi.";
        await BilgileriYukle(firmaId.Value, kullaniciId.Value);
        return Page();
    }

    public async Task<IActionResult> OnPostEmailBildirimKaydetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        var kullaniciId = HttpContext.Session.GetInt32("KullaniciId");
        if (firmaId == null || kullaniciId == null)
            return RedirectToPage("/Login");

        var kullanici = await _db.Kullanicilar.FirstOrDefaultAsync(x => x.Id == kullaniciId.Value && x.FirmaId == firmaId.Value);
        if (kullanici == null)
            return RedirectToPage("/Login");

        var yeniEmail = (KullaniciEmail ?? "").Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(yeniEmail) && !EmailService.IsValidEmail(yeniEmail))
        {
            Hata = "Geçerli bir e-posta adresi girin.";
            await BilgileriYukle(firmaId.Value, kullaniciId.Value);
            KullaniciEmail = yeniEmail;
            return Page();
        }

        if (OdemeEmailBildirimiAktifMi && string.IsNullOrWhiteSpace(yeniEmail))
        {
            Hata = "Ödeme e-posta bildirimi için kullanıcı e-posta adresi gereklidir.";
            await BilgileriYukle(firmaId.Value, kullaniciId.Value);
            KullaniciEmail = yeniEmail;
            OdemeEmailBildirimiAktifMi = true;
            return Page();
        }

        var eskiEmail = kullanici.Email;
        var eskiAktiflik = kullanici.OdemeEmailBildirimiAktifMi;

        kullanici.Email = yeniEmail;
        kullanici.OdemeEmailBildirimiAktifMi = OdemeEmailBildirimiAktifMi;
        if (!string.Equals(eskiEmail, yeniEmail, StringComparison.OrdinalIgnoreCase))
            kullanici.EmailDogrulandiMi = false;

        await _islemGecmisi.KaydetAsync(
            "Ayarlar",
            "Güncelleme",
            $"Ödeme e-posta bildirim ayarları güncellendi. E-posta: {Maskele(yeniEmail)}",
            eskiDeger: new { Email = Maskele(eskiEmail), Aktif = eskiAktiflik },
            yeniDeger: new { Email = Maskele(yeniEmail), Aktif = OdemeEmailBildirimiAktifMi });

        await _db.SaveChangesAsync();
        Mesaj = "E-posta bildirim ayarları kaydedildi.";
        await BilgileriYukle(firmaId.Value, kullaniciId.Value);
        return Page();
    }

    public async Task<IActionResult> OnPostTestEpostasiAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        var kullaniciId = HttpContext.Session.GetInt32("KullaniciId");
        if (firmaId == null || kullaniciId == null)
            return RedirectToPage("/Login");

        var kullanici = await _db.Kullanicilar.FirstOrDefaultAsync(x => x.Id == kullaniciId.Value && x.FirmaId == firmaId.Value);
        if (kullanici == null)
            return RedirectToPage("/Login");

        if (!EmailService.IsValidEmail(kullanici.Email))
        {
            Hata = "Test e-postası göndermek için önce geçerli bir kullanıcı e-postası kaydedin.";
            await BilgileriYukle(firmaId.Value, kullaniciId.Value);
            return Page();
        }

        var html = """
<!doctype html>
<html lang="tr">
<body style="font-family:Segoe UI,Arial,sans-serif;color:#0f172a;">
  <h2>Firmova ERP test e-postası</h2>
  <p>Ödeme e-posta bildirim ayarlarınız çalışıyor.</p>
</body>
</html>
""";

        var sonuc = await _emailService.SendAsync(
            kullanici.Email,
            "Firmova ERP test e-postası",
            html);

        if (!sonuc.BasariliMi)
        {
            Hata = sonuc.HataMesaji ?? "Test e-postası gönderilemedi.";
            await BilgileriYukle(firmaId.Value, kullaniciId.Value);
            return Page();
        }

        await _islemGecmisi.KaydetAsync(
            "Ayarlar",
            "Test E-postası",
            $"Test e-postası gönderildi. E-posta: {Maskele(kullanici.Email)}",
            yeniDeger: new { Email = Maskele(kullanici.Email) });

        await _db.SaveChangesAsync();
        Mesaj = "Test e-postası gönderildi.";
        await BilgileriYukle(firmaId.Value, kullaniciId.Value);
        return Page();
    }

    public async Task<IActionResult> OnPostMenuKaydetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        var kullaniciId = HttpContext.Session.GetInt32("KullaniciId");
        if (firmaId == null || kullaniciId == null)
            return RedirectToPage("/Login");

        var firma = await _db.Firmalar.FirstOrDefaultAsync(x => x.Id == firmaId.Value);
        if (firma == null)
            return RedirectToPage("/Login");

        firma.MenuCariKartlar = MenuCariKartlar;
        firma.MenuKasa = MenuKasa;
        firma.MenuRaporlar = MenuRaporlar;
        firma.MenuOdemeler = MenuOdemeler;
        firma.MenuCalisanlar = MenuCalisanlar;
        firma.MenuMusteriler = MenuMusteriler;
        firma.MenuStoklar = MenuStoklar;
        firma.MenuMaliyet = MenuMaliyet;
        firma.MenuCekler = MenuCekler;
        await _db.SaveChangesAsync();

        HttpContext.Session.SetString("MenuCariKartlar", MenuCariKartlar ? "1" : "0");
        HttpContext.Session.SetString("MenuKasa", MenuKasa ? "1" : "0");
        HttpContext.Session.SetString("MenuRaporlar", MenuRaporlar ? "1" : "0");
        HttpContext.Session.SetString("MenuOdemeler", MenuOdemeler ? "1" : "0");
        HttpContext.Session.SetString("MenuCalisanlar", MenuCalisanlar ? "1" : "0");
        HttpContext.Session.SetString("MenuMusteriler", MenuMusteriler ? "1" : "0");
        HttpContext.Session.SetString("MenuStoklar", MenuStoklar ? "1" : "0");
        HttpContext.Session.SetString("MenuMaliyet", MenuMaliyet ? "1" : "0");
        HttpContext.Session.SetString("MenuCekler", MenuCekler ? "1" : "0");

        Mesaj = "Menü ayarları kaydedildi.";
        await BilgileriYukle(firmaId.Value, kullaniciId.Value);
        return Page();
    }

    private async Task BilgileriYukle(int firmaId, int kullaniciId)
    {
        var firma = await _db.Firmalar.FirstOrDefaultAsync(x => x.Id == firmaId);
        var kullanici = await _db.Kullanicilar.FirstOrDefaultAsync(x => x.Id == kullaniciId && x.FirmaId == firmaId);
        if (firma == null || kullanici == null)
            return;

        KullaniciAdi = kullanici.KullaniciAdi;
        KullaniciEmail = kullanici.Email;
        OdemeEmailBildirimiAktifMi = kullanici.OdemeEmailBildirimiAktifMi;
        FirmaAdi = firma.FirmaAdi;
        Adres = firma.Adres;
        Telefon = firma.Telefon;
        Email = firma.Email;
        VergiDairesi = firma.VergiDairesi;
        VergiNo = firma.VergiNo;
        LogoYolu = firma.LogoYolu;
        MenuCariKartlar = firma.MenuCariKartlar;
        MenuKasa = firma.MenuKasa;
        MenuRaporlar = firma.MenuRaporlar;
        MenuOdemeler = firma.MenuOdemeler;
        MenuCalisanlar = firma.MenuCalisanlar;
        MenuMusteriler = firma.MenuMusteriler;
        MenuStoklar = firma.MenuStoklar;
        MenuMaliyet = firma.MenuMaliyet;
        MenuCekler = firma.MenuCekler;
    }

    private static string Maskele(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return "";

        var parts = email.Split('@', 2);
        var name = parts[0];
        var maskedName = name.Length <= 1 ? "*" : $"{name[0]}***";
        return $"{maskedName}@{parts[1]}";
    }
}
