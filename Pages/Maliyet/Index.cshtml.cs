using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages.Maliyet;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IIslemGecmisiService _islemGecmisi;

    public IndexModel(AppDbContext db, IIslemGecmisiService islemGecmisi)
    {
        _db = db;
        _islemGecmisi = islemGecmisi;
    }

    public List<MaliyetKaydi> MaliyetKayitlari { get; set; } = new();

    public decimal ToplamArsivMaliyeti { get; set; }

    public decimal OrtalamaBirimMaliyet { get; set; }

    public int KayitSayisi { get; set; }

    public string Mesaj { get; set; } = "";

    public string Hata { get; set; } = "";

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");

        if (firmaId == null)
            return RedirectToPage("/Login");

        await KayitlariYukleAsync(firmaId.Value);

        return Page();
    }

    public async Task<IActionResult> OnPostSilAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");

        if (firmaId == null)
            return RedirectToPage("/Login");

        var kayit = await _db.MaliyetKayitlari
            .FirstOrDefaultAsync(x =>
                x.Id == id &&
                x.FirmaId == firmaId.Value);

        if (kayit == null)
        {
            Hata = "Silinecek maliyet kaydı bulunamadı.";
            await KayitlariYukleAsync(firmaId.Value);
            return Page();
        }

        var eskiDeger = IslemGecmisiSnapshots.Maliyet(kayit);
        _db.MaliyetKayitlari.Remove(kayit);
        await _db.SaveChangesWithAuditAsync(
            () => _islemGecmisi.KaydetAsync(
                "Maliyet",
                "Silme",
                $"{kayit.UretimAdi} maliyet kaydı silindi (ID: {kayit.Id}).",
                eskiDeger: eskiDeger),
            anaKaydiOnceKaydet: false);

        Mesaj = "Maliyet kaydı silindi.";

        await KayitlariYukleAsync(firmaId.Value);

        return Page();
    }

    private async Task KayitlariYukleAsync(int firmaId)
    {
        MaliyetKayitlari = await _db.MaliyetKayitlari
            .Where(x => x.FirmaId == firmaId)
            .OrderByDescending(x => x.HesapTarihi)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        KayitSayisi = MaliyetKayitlari.Count;

        ToplamArsivMaliyeti =
            MaliyetKayitlari.Sum(x => x.ToplamMaliyet);

        OrtalamaBirimMaliyet =
            MaliyetKayitlari.Any()
                ? MaliyetKayitlari.Average(x => x.BirimMaliyet)
                : 0;
    }
}
