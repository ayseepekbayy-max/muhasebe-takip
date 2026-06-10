using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages.Maliyet;

public class DetayModel : PageModel
{
    private readonly AppDbContext _db;

    public DetayModel(AppDbContext db)
    {
        _db = db;
    }

    public MaliyetKaydi? Kayit { get; set; }

    public MaliyetKaydiDetay Detay { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");

        if (firmaId == null)
            return RedirectToPage("/Login");

        Kayit = await _db.MaliyetKayitlari
            .FirstOrDefaultAsync(x =>
                x.Id == id &&
                x.FirmaId == firmaId.Value);

        if (Kayit == null)
            return RedirectToPage("/Maliyet/Index");

        Detay = DetayOku(Kayit.DetayJson);

        return Page();
    }

    private static MaliyetKaydiDetay DetayOku(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new MaliyetKaydiDetay();

        try
        {
            return JsonSerializer.Deserialize<MaliyetKaydiDetay>(json) ?? new MaliyetKaydiDetay();
        }
        catch
        {
            return new MaliyetKaydiDetay();
        }
    }
}
