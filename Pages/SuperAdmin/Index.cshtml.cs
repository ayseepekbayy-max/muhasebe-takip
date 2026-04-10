using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages.SuperAdmin;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db)
    {
        _db = db;
    }

    public List<Firma> Firmalar { get; set; } = new();
    public List<Kullanici> Kullanicilar { get; set; } = new();

    public string Hata { get; set; } = "";
    public string Mesaj { get; set; } = "";

    public async Task<IActionResult> OnGetAsync()
    {
        if (!SuperAdminMi())
            return RedirectToPage("/Index");

        await YukleAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRolGuncelleAsync(int id, string yeniRol)
    {
        if (!SuperAdminMi())
            return RedirectToPage("/Index");

        yeniRol = (yeniRol ?? "").Trim();

        if (yeniRol != "Kullanici" && yeniRol != "SuperAdmin")
        {
            Hata = "Geçersiz rol seçildi.";
            await YukleAsync();
            return Page();
        }

        var kullanici = await _db.Kullanicilar
            .Include(x => x.Firma)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (kullanici == null)
        {
            Hata = "Kullanıcı bulunamadı.";
            await YukleAsync();
            return Page();
        }

        var kullaniciAdi = (kullanici.KullaniciAdi ?? "").Trim().ToLower();
        if (kullaniciAdi == "admin" && yeniRol != "SuperAdmin")
        {
            Hata = "Ana admin hesabının rolü SuperAdmin dışında bir role düşürülemez.";
            await YukleAsync();
            return Page();
        }

        kullanici.Rol = yeniRol;
        await _db.SaveChangesAsync();

        Mesaj = "Kullanıcı rolü güncellendi.";
        await YukleAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostFirmaDurumDegistirAsync(int id)
    {
        if (!SuperAdminMi())
            return RedirectToPage("/Index");

        var firma = await _db.Firmalar.FirstOrDefaultAsync(x => x.Id == id);

        if (firma == null)
        {
            Hata = "Firma bulunamadı.";
            await YukleAsync();
            return Page();
        }

        firma.AktifMi = !firma.AktifMi;
        await _db.SaveChangesAsync();

        Mesaj = firma.AktifMi
            ? "Firma aktif hale getirildi."
            : "Firma pasif hale getirildi.";

        await YukleAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostKullaniciSilAsync(int id)
    {
        if (!SuperAdminMi())
            return RedirectToPage("/Index");

        var kendiKullaniciId = HttpContext.Session.GetInt32("KullaniciId");

        var kullanici = await _db.Kullanicilar
            .Include(x => x.Firma)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (kullanici == null)
        {
            Hata = "Kullanıcı bulunamadı.";
            await YukleAsync();
            return Page();
        }

        var kullaniciAdi = (kullanici.KullaniciAdi ?? "").Trim().ToLower();

        if (kullaniciAdi == "admin")
        {
            Hata = "Ana admin hesabı silinemez.";
            await YukleAsync();
            return Page();
        }

        if (kendiKullaniciId == kullanici.Id)
        {
            Hata = "Giriş yaptığınız kullanıcıyı silemezsiniz.";
            await YukleAsync();
            return Page();
        }

        _db.Kullanicilar.Remove(kullanici);
        await _db.SaveChangesAsync();

        Mesaj = "Kullanıcı silindi.";
        await YukleAsync();
        return Page();
    }

    private bool SuperAdminMi()
    {
        var rol = (HttpContext.Session.GetString("Rol") ?? "").Trim().ToLower();
        return rol == "superadmin";
    }

    private async Task YukleAsync()
    {
        Firmalar = await _db.Firmalar
            .OrderBy(x => x.FirmaAdi)
            .ToListAsync();

        Kullanicilar = await _db.Kullanicilar
            .Include(x => x.Firma)
            .OrderBy(x => x.KullaniciAdi)
            .ToListAsync();
    }
}