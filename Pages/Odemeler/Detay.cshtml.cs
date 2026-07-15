using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages.Odemeler;

public class DetayModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IIslemGecmisiService _islemGecmisi;

    public DetayModel(AppDbContext db, IIslemGecmisiService islemGecmisi)
    {
        _db = db;
        _islemGecmisi = islemGecmisi;
    }

    public OdemePlani? Odeme { get; set; }
    public List<OdemeHareketi> Hareketler { get; set; } = new();
    public OdemeDurumu Durum { get; set; }
    public bool BuAyOdendi { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleAsync(id, firmaId.Value);
        if (Odeme == null)
            return NotFound();

        return Page();
    }

    public async Task<IActionResult> OnPostSilAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var odeme = await _db.OdemePlanlari.FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId.Value);
        if (odeme == null)
            return NotFound();

        var hareketler = await _db.OdemeHareketleri
            .Where(x => x.FirmaId == firmaId.Value && x.OdemePlaniId == odeme.Id)
            .ToListAsync();

        var bildirimler = await _db.OdemeBildirimGecmisleri
            .Where(x => x.FirmaId == firmaId.Value && x.OdemePlaniId == odeme.Id)
            .ToListAsync();

        var eskiDeger = new
        {
            odeme.Id,
            odeme.OdemeAdi,
            odeme.OdemeTuru,
            odeme.AylikOdemeTutari,
            odeme.ToplamTaksitSayisi,
            odeme.KalanTaksitSayisi,
            odeme.SonrakiOdemeTarihi,
            HareketSayisi = hareketler.Count,
            BildirimKaydiSayisi = bildirimler.Count
        };

        _db.OdemeBildirimGecmisleri.RemoveRange(bildirimler);
        _db.OdemeHareketleri.RemoveRange(hareketler);
        _db.OdemePlanlari.Remove(odeme);

        await _db.SaveChangesWithAuditAsync(
            () => _islemGecmisi.KaydetAsync(
                "Ödemeler",
                "Silme",
                $"Ödeme planı silindi: {odeme.OdemeAdi} (ID: {odeme.Id}).",
                eskiDeger),
            anaKaydiOnceKaydet: false);

        TempData["Basari"] = "Ödeme planı silindi.";
        return RedirectToPage("/Odemeler/Index");
    }

    private async Task YukleAsync(int id, int firmaId)
    {
        Odeme = await _db.OdemePlanlari
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId);

        if (Odeme == null)
            return;

        Hareketler = await _db.OdemeHareketleri
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId && x.OdemePlaniId == id)
            .OrderByDescending(x => x.OdemeTarihi)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        var bugun = DateTime.UtcNow.Date;
        var ayBaslangic = new DateTime(bugun.Year, bugun.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        BuAyOdendi = Hareketler.Any(x => x.OdemeTarihi >= ayBaslangic && x.OdemeTarihi < ayBaslangic.AddMonths(1));
        Durum = OdemePlanlamaService.Durum(Odeme, bugun, BuAyOdendi);
    }
}
