using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Helpers;

namespace MuhasebeTakip2.App.Pages;

public class AyarlarModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    public AyarlarModel(AppDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    [BindProperty] public string KullaniciAdi { get; set; } = "";
    [BindProperty] public string MevcutSifre { get; set; } = "";
    [BindProperty] public string YeniSifre { get; set; } = "";
    [BindProperty] public string YeniSifreTekrar { get; set; } = "";

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

            var klasor = Path.Combine(_env.WebRootPath, "uploads", "logos");
            Directory.CreateDirectory(klasor);
            var dosyaAdi = $"firma-{firma.Id}-{Guid.NewGuid():N}{uzanti}";
            var fizikselYol = Path.Combine(klasor, dosyaAdi);
            await using var fs = System.IO.File.Create(fizikselYol);
            await LogoDosyasi.CopyToAsync(fs);
            firma.LogoYolu = $"/uploads/logos/{dosyaAdi}";
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

        var kullanici = await _db.Kullanicilar.FirstOrDefaultAsync(x => x.Id == kullaniciId.Value);
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

            var mevcutSifreDogru = PasswordHelper.Verify(MevcutSifre, kullanici.Sifre) || kullanici.Sifre == MevcutSifre;
            if (!mevcutSifreDogru)
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

            kullanici.Sifre = PasswordHelper.Hash(YeniSifre);
        }

        await _db.SaveChangesAsync();
        HttpContext.Session.SetString("KullaniciAdi", kullanici.KullaniciAdi);
        Mesaj = "Kullanıcı bilgileri güncellendi.";
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
        firma.MenuCalisanlar = MenuCalisanlar;
        firma.MenuMusteriler = MenuMusteriler;
        firma.MenuStoklar = MenuStoklar;
        firma.MenuMaliyet = MenuMaliyet;
        firma.MenuCekler = MenuCekler;
        await _db.SaveChangesAsync();

        HttpContext.Session.SetString("MenuCariKartlar", MenuCariKartlar ? "1" : "0");
        HttpContext.Session.SetString("MenuKasa", MenuKasa ? "1" : "0");
        HttpContext.Session.SetString("MenuRaporlar", MenuRaporlar ? "1" : "0");
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
        var kullanici = await _db.Kullanicilar.FirstOrDefaultAsync(x => x.Id == kullaniciId);
        if (firma == null || kullanici == null)
            return;

        KullaniciAdi = kullanici.KullaniciAdi;
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
        MenuCalisanlar = firma.MenuCalisanlar;
        MenuMusteriler = firma.MenuMusteriler;
        MenuStoklar = firma.MenuStoklar;
        MenuMaliyet = firma.MenuMaliyet;
        MenuCekler = firma.MenuCekler;
    }
}