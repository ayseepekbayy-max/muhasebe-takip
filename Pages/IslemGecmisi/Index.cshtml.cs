using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages.IslemGecmisi;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db)
    {
        _db = db;
    }

    public List<Models.IslemGecmisi> Kayitlar { get; set; } = new();
    public List<string> Moduller { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? Modul { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? IslemTuru { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Kullanici { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var firmaKayitlari = _db.IslemGecmisleri
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId.Value);

        Moduller = await firmaKayitlari
            .Select(x => x.Modul)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        var sorgu = firmaKayitlari;

        if (!string.IsNullOrWhiteSpace(Modul))
            sorgu = sorgu.Where(x => x.Modul == Modul);

        if (!string.IsNullOrWhiteSpace(IslemTuru))
            sorgu = sorgu.Where(x => x.IslemTuru == IslemTuru);

        if (!string.IsNullOrWhiteSpace(Kullanici))
        {
            var aranan = Kullanici.Trim().ToLower();
            sorgu = sorgu.Where(x => x.KullaniciAdi.ToLower().Contains(aranan));
        }

        Kayitlar = await sorgu
            .OrderByDescending(x => x.Tarih)
            .ThenByDescending(x => x.Id)
            .Take(500)
            .ToListAsync();

        return Page();
    }
}
