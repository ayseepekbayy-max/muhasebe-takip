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

    public List<Kullanici> Kullanicilar { get; set; } = new();

    public string Hata { get; set; } = "";
    public string Mesaj { get; set; } = "";

    public async Task<IActionResult> OnGetAsync()
    {
        var rol = (HttpContext.Session.GetString("Rol") ?? "").Trim().ToLower();
        if (rol != "superadmin")
            return RedirectToPage("/Index");

        await YukleAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRolGuncelleAsync(int id, string yeniRol)
    {
        var rol = (HttpContext.Session.GetString("Rol") ?? "").Trim().ToLower();
        if (rol != "superadmin")
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

        if ((kullanici.KullaniciAdi ?? "").Trim().ToLower() == "admin" && yeniRol != "SuperAdmin")
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

    private async Task YukleAsync()
    {
        Kullanicilar = await _db.Kullanicilar
            .Include(x => x.Firma)
            .OrderBy(x => x.KullaniciAdi)
            .ToListAsync();
    }
}