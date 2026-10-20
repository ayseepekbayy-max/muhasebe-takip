using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;
namespace MuhasebeTakip2.App.Pages.Kasa;

public class HesaplarModel(AppDbContext db, IIslemGecmisiService audit) : PageModel
{
    public List<KasaHesapService.BakiyeSatiri> Hesaplar { get; set; } = [];
    public List<KasaTransfer> Transferler { get; set; } = [];
    public decimal AtanmamisBakiye { get; set; }
    public int AtanmamisHareketSayisi { get; set; }
    public decimal ToplamBakiye => Hesaplar.Sum(x => x.Bakiye) + AtanmamisBakiye;
    public string Hata { get; set; } = "";
    [BindProperty] public HesapForm Yeni { get; set; } = new();
    [BindProperty] public TransferForm Transfer { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null) return RedirectToPage("/Login");
        await YukleAsync(firmaId.Value); return Page();
    }
    public async Task<IActionResult> OnPostEkleAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null) return RedirectToPage("/Login");
        Yeni.Ad = (Yeni.Ad ?? "").Trim();
        if (!ModelState.IsValid || Yeni.Ad.Length is < 1 or > 100 || !Enum.IsDefined(Yeni.Tur) ||
            decimal.Round(Yeni.AcilisBakiyesi, 2) != Yeni.AcilisBakiyesi || (Yeni.AcilisBakiyesi < -9999999999999999m || Yeni.AcilisBakiyesi > 9999999999999999m))
            return await HataAsync(firmaId.Value, "Hesap adı, türü ve en fazla iki ondalıklı açılış bakiyesini kontrol edin.");
        if (await db.KasaHesaplar.AnyAsync(x => x.FirmaId == firmaId && x.Ad.ToUpper() == Yeni.Ad.ToUpper()))
            return await HataAsync(firmaId.Value, "Bu isimde bir hesap zaten var. Farklı bir hesap adı seçin.");
        var hesap = new KasaHesap { FirmaId = firmaId.Value, Ad = Yeni.Ad, Tur = Yeni.Tur, AcilisBakiyesi = Yeni.AcilisBakiyesi };
        db.Add(hesap);
        await db.SaveChangesWithAuditAsync(() => audit.KaydetAsync("Kasa/Banka", "Ekleme", $"Hesap eklendi: {hesap.Ad}.", yeniDeger: new { hesap.Id, hesap.Ad, hesap.Tur, hesap.AcilisBakiyesi }), true);
        return RedirectToPage();
    }
    public async Task<IActionResult> OnPostAktiflikAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null) return RedirectToPage("/Login");
        var hesap = await db.KasaHesaplar.SingleOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId.Value);
        if (hesap == null) return NotFound();
        var eski = hesap.AktifMi; hesap.AktifMi = !hesap.AktifMi;
        await db.SaveChangesWithAuditAsync(() => audit.KaydetAsync("Kasa/Banka", "Düzenleme", $"{hesap.Ad} hesabının aktifliği değişti.", new { AktifMi = eski }, new { hesap.Id, hesap.AktifMi }), false);
        return RedirectToPage();
    }
    public async Task<IActionResult> OnPostTransferAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null) return RedirectToPage("/Login");
        if (!ModelState.IsValid || Transfer.Tutar <= 0 || Transfer.Tutar > 9999999999999999m || decimal.Round(Transfer.Tutar, 2) != Transfer.Tutar || Transfer.Tarih == default ||
            Transfer.KaynakHesapId == Transfer.HedefHesapId || (Transfer.Aciklama?.Length ?? 0) > 500)
            return await HataAsync(firmaId.Value, "İki farklı hesap, geçerli tarih ve sıfırdan büyük, en fazla iki ondalıklı tutar seçin.");
        if (!await KasaHesapService.GecerliMiAsync(db, firmaId.Value, Transfer.KaynakHesapId) ||
            !await KasaHesapService.GecerliMiAsync(db, firmaId.Value, Transfer.HedefHesapId))
            return await HataAsync(firmaId.Value, KasaHesapService.SecimHatasi);
        var transfer = new KasaTransfer { FirmaId = firmaId.Value, KaynakHesapId = Transfer.KaynakHesapId, HedefHesapId = Transfer.HedefHesapId,
            Tutar = Transfer.Tutar, Tarih = DateTime.SpecifyKind(Transfer.Tarih.Date, DateTimeKind.Utc), Aciklama = (Transfer.Aciklama ?? "").Trim() };
        db.Add(transfer);
        await db.SaveChangesWithAuditAsync(() => audit.KaydetAsync("Kasa/Banka", "Transfer", "Hesaplar arası para transferi yapıldı.", yeniDeger: new { transfer.Id, transfer.KaynakHesapId, transfer.HedefHesapId, transfer.Tutar, transfer.Tarih, transfer.Aciklama }), true);
        return RedirectToPage();
    }
    public async Task<IActionResult> OnPostTransferSilAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null) return RedirectToPage("/Login");
        var transfer = await db.KasaTransferler.SingleOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId.Value);
        if (transfer == null) return NotFound();
        db.Remove(transfer);
        await db.SaveChangesWithAuditAsync(() => audit.KaydetAsync("Kasa/Banka", "Silme", "Hesaplar arası transfer iki hesaptan birlikte kaldırıldı.", eskiDeger: new { transfer.Id, transfer.KaynakHesapId, transfer.HedefHesapId, transfer.Tutar }), false);
        return RedirectToPage();
    }
    private async Task<IActionResult> HataAsync(int firmaId, string hata) { Hata = hata; await YukleAsync(firmaId); return Page(); }
    private async Task YukleAsync(int firmaId)
    {
        Hesaplar = await KasaHesapService.BakiyelerAsync(db, firmaId);
        AtanmamisBakiye = await KasaHesapService.AtanmamisBakiyeAsync(db, firmaId);
        AtanmamisHareketSayisi = await db.KasaHareketleri.CountAsync(x => x.FirmaId == firmaId && x.KasaHesapId == null);
        Transferler = await db.KasaTransferler.AsNoTracking().Include(x => x.KaynakHesap).Include(x => x.HedefHesap)
            .Where(x => x.FirmaId == firmaId).OrderByDescending(x => x.Tarih).ThenByDescending(x => x.Id).Take(100).ToListAsync();
    }
    public class HesapForm { public string Ad { get; set; } = ""; public KasaHesapTuru Tur { get; set; } = KasaHesapTuru.Nakit; public decimal AcilisBakiyesi { get; set; } }
    public class TransferForm { public int KaynakHesapId { get; set; } public int HedefHesapId { get; set; } public decimal Tutar { get; set; } public DateTime Tarih { get; set; } = DateTime.UtcNow.Date; public string? Aciklama { get; set; } }
}
