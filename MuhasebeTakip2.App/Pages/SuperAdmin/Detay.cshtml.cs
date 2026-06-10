using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages.SuperAdmin;

public class DetayModel : PageModel
{
    private readonly AppDbContext _db;

    public DetayModel(AppDbContext db)
    {
        _db = db;
    }

    public Firma? Firma { get; set; }
    public List<Kullanici> Kullanicilar { get; set; } = new();

    public string Mesaj { get; set; } = "";
    public string Hata { get; set; } = "";

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (HttpContext.Session.GetString("Rol") != "SuperAdmin")
            return RedirectToPage("/Login");

        await YukleFirmaDetayi(id);

        if (Firma == null)
            return NotFound();

        return Page();
    }


    private async Task YukleFirmaDetayi(int firmaId)
    {
        Firma = await _db.Firmalar.FirstOrDefaultAsync(x => x.Id == firmaId);

        if (Firma != null)
        {
            Kullanicilar = await _db.Kullanicilar
                .Where(x => x.FirmaId == firmaId)
                .OrderBy(x => x.KullaniciAdi)
                .ToListAsync();
        }
        else
        {
            Kullanicilar = new List<Kullanici>();
        }
    }
}