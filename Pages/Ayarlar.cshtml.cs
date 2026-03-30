using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Helpers;

namespace MuhasebeTakip2.App.Pages;

public class AyarlarModel : PageModel
{
    private readonly AppDbContext _db;

    public AyarlarModel(AppDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public string KullaniciAdi { get; set; } = "";

    [BindProperty]
    public string MevcutSifre { get; set; } = "";

    [BindProperty]
    public string YeniSifre { get; set; } = "";

    [BindProperty]
    public string YeniSifreTekrar { get; set; } = "";

    [BindProperty]
    public bool MenuCariKartlar { get; set; } = true;

    [BindProperty]
    public bool MenuKasa { get; set; } = true;

    [BindProperty]
    public bool MenuRaporlar { get; set; } = true;

    [BindProperty]
    public bool MenuCalisanlar { get; set; } = true;

    [BindProperty]
    public bool MenuMusteriler { get; set; } = true;

    [BindProperty]
    public bool MenuStoklar { get; set; } = true;

    [BindProperty]
    public bool MenuMaliyet { get; set; } = true;

    [BindProperty]
    public bool MenuCekler { get; set; } = true;

    public string Mesaj { get; set; } = "";
    public string Hata { get; set; } = "";

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        var kullaniciId = HttpContext.Session.GetInt32("KullaniciId");

        if (firmaId == null || kullaniciId == null)
            return RedirectToPage("/Login");

        var firma = await _db.Firmalar.FirstOrDefaultAsync(x => x.Id == firmaId.Value);
        var kullanici = await _db.Kullanicilar.FirstOrDefaultAsync(x => x.Id == kullaniciId.Value);

        if (firma == null || kullanici == null)
            return RedirectToPage("/Login");

        KullaniciAdi = kullanici.KullaniciAdi;

        MenuCariKartlar = firma.MenuCariKartlar;
        MenuKasa = firma.MenuKasa;
        MenuRaporlar = firma.MenuRaporlar;
        MenuCalisanlar = firma.MenuCalisanlar;
        MenuMusteriler = firma.MenuMusteriler;
        MenuStoklar = firma.MenuStoklar;
        MenuMaliyet = firma.MenuMaliyet;
        MenuCekler = firma.MenuCekler;

        return Page();
    }

    public async Task<IActionResult> OnPostKullaniciGuncelleAsync()
    {
        var kullaniciId = HttpContext.Session.GetInt32("KullaniciId");
        if (kullaniciId == null)
            return RedirectToPage("/Login");

        var kullanici = await _db.Kullanicilar.FirstOrDefaultAsync(x => x.Id == kullaniciId.Value);
        if (kullanici == null)
            return RedirectToPage("/Login");

        KullaniciAdi = (KullaniciAdi ?? "").Trim();
        MevcutSifre = (MevcutSifre ?? "").Trim();
        YeniSifre = (YeniSifre ?? "").Trim();
        YeniSifreTekrar = (YeniSifreTekrar ?? "").Trim();

        if (string.IsNullOrWhiteSpace(KullaniciAdi))
        {
            Hata = "Kullanıcı adı boş olamaz.";
            await MenuBilgileriniYukle();
            return Page();
        }

        var ayniAdKullananVar = await _db.Kullanicilar
            .AnyAsync(x => x.Id != kullanici.Id && x.KullaniciAdi == KullaniciAdi);

        if (ayniAdKullananVar)
        {
            Hata = "Bu kullanıcı adı zaten kullanılıyor.";
            await MenuBilgileriniYukle();
            return Page();
        }

        kullanici.KullaniciAdi = KullaniciAdi;

        // Şifre değiştirilecekse mevcut şifre zorunlu
        if (!string.IsNullOrWhiteSpace(YeniSifre) || !string.IsNullOrWhiteSpace(YeniSifreTekrar))
        {
            if (string.IsNullOrWhiteSpace(MevcutSifre))
            {
                Hata = "Şifre değiştirmek için mevcut şifrenizi girin.";
                await MenuBilgileriniYukle();
                return Page();
            }

            bool mevcutSifreDogru =
                PasswordHelper.Verify(MevcutSifre, kullanici.Sifre) ||
                kullanici.Sifre == MevcutSifre;

            if (!mevcutSifreDogru)
            {
                Hata = "Mevcut şifre yanlış.";
                await MenuBilgileriniYukle();
                return Page();
            }

            if (YeniSifre != YeniSifreTekrar)
            {
                Hata = "Yeni şifreler birbiriyle aynı değil.";
                await MenuBilgileriniYukle();
                return Page();
            }

            if (YeniSifre.Length < 4)
            {
                Hata = "Yeni şifre en az 4 karakter olmalı.";
                await MenuBilgileriniYukle();
                return Page();
            }

            kullanici.Sifre = PasswordHelper.Hash(YeniSifre);
        }

        await _db.SaveChangesAsync();

        HttpContext.Session.SetString("KullaniciAdi", kullanici.KullaniciAdi);

        Mesaj = "Kullanıcı bilgileri güncellendi.";
        await MenuBilgileriniYukle();
        return Page();
    }

    public async Task<IActionResult> OnPostMenuKaydetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
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
        return Page();
    }

    private async Task MenuBilgileriniYukle()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return;

        var firma = await _db.Firmalar.FirstOrDefaultAsync(x => x.Id == firmaId.Value);
        if (firma == null)
            return;

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