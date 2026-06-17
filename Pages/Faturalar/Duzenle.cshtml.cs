using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages.Faturalar;

public class DuzenleModel : PageModel
{
    private readonly AppDbContext _db;

    public DuzenleModel(AppDbContext db)
    {
        _db = db;
    }

    public List<CariKart> Cariler { get; set; } = new();
    public decimal OdenenToplam { get; set; }

    [BindProperty]
    public FaturaDuzenleForm Fatura { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var fatura = await _db.Faturalar
            .Include(x => x.Kalemler)
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId.Value);

        if (fatura == null)
            return NotFound();

        await CarileriYukleAsync(firmaId.Value);
        Fatura = new FaturaDuzenleForm
        {
            Id = fatura.Id,
            CariKartId = fatura.CariKartId ?? 0,
            FaturaNo = fatura.FaturaNo,
            Tip = fatura.Tip,
            Tarih = fatura.Tarih.Date,
            VadeTarihi = fatura.VadeTarihi?.Date,
            Aciklama = fatura.Aciklama,
            Kalemler = fatura.Kalemler.Select(x => new FaturaKalemForm
            {
                Aciklama = x.Aciklama,
                Miktar = x.Miktar,
                BirimFiyat = x.BirimFiyat,
                KdvOrani = x.KdvOrani
            }).ToList()
        };
        OdenenToplam = fatura.OdenenToplam;

        if (!Fatura.Kalemler.Any())
            Fatura.Kalemler.Add(new FaturaKalemForm());

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var fatura = await _db.Faturalar
            .Include(x => x.Kalemler)
            .FirstOrDefaultAsync(x => x.Id == Fatura.Id && x.FirmaId == firmaId.Value);

        if (fatura == null)
            return NotFound();

        await CarileriYukleAsync(firmaId.Value);
        OdenenToplam = fatura.OdenenToplam;

        if (Fatura.CariKartId <= 0)
            ModelState.AddModelError("", "Cari seçimi zorunludur.");

        var cariVarMi = await _db.CariKartlar.AnyAsync(x => x.Id == Fatura.CariKartId && x.FirmaId == firmaId.Value);
        if (!cariVarMi)
            ModelState.AddModelError("", "Seçilen cari bulunamadı.");

        var doluKalemler = TemizKalemler(Fatura.Kalemler ?? new List<FaturaKalemForm>());
        KalemleriDogrula(doluKalemler);
        var yeniKalemler = FaturaKalemleriOlustur(doluKalemler);
        var yeniGenelToplam = yeniKalemler.Sum(x => x.GenelToplam);

        if (fatura.OdenenToplam > yeniGenelToplam)
            ModelState.AddModelError("", "Fatura toplamı, daha önce ödenen/tahsil edilen tutardan düşük olamaz.");

        if (!ModelState.IsValid)
        {
            Fatura.Kalemler = doluKalemler.Any() ? doluKalemler : (Fatura.Kalemler ?? new List<FaturaKalemForm>());
            if (Fatura.Kalemler == null || !Fatura.Kalemler.Any())
                Fatura.Kalemler = new List<FaturaKalemForm> { new() };
            return Page();
        }

        fatura.CariKartId = Fatura.CariKartId;
        fatura.FaturaNo = string.IsNullOrWhiteSpace(Fatura.FaturaNo) ? fatura.FaturaNo : Fatura.FaturaNo.Trim();
        fatura.Tip = Fatura.Tip;
        fatura.Tarih = ToUtcDate(Fatura.Tarih);
        fatura.VadeTarihi = Fatura.VadeTarihi.HasValue ? ToUtcDate(Fatura.VadeTarihi.Value) : null;
        fatura.Aciklama = (Fatura.Aciklama ?? "").Trim();
        fatura.AraToplam = yeniKalemler.Sum(x => x.AraToplam);
        fatura.KdvToplam = yeniKalemler.Sum(x => x.KdvTutar);
        fatura.GenelToplam = yeniGenelToplam;

        _db.FaturaKalemleri.RemoveRange(fatura.Kalemler);
        fatura.Kalemler = yeniKalemler;

        await _db.SaveChangesAsync();
        TempData["Basari"] = "Fatura güncellendi.";
        return RedirectToPage("/Faturalar/Detay", new { id = fatura.Id });
    }

    private async Task CarileriYukleAsync(int firmaId)
    {
        Cariler = await _db.CariKartlar
            .Where(x => x.FirmaId == firmaId)
            .OrderBy(x => x.Unvan)
            .ToListAsync();
    }

    private static List<FaturaKalemForm> TemizKalemler(IEnumerable<FaturaKalemForm> kalemler)
    {
        return kalemler
            .Select(x => new FaturaKalemForm
            {
                Aciklama = (x.Aciklama ?? "").Trim(),
                Miktar = x.Miktar,
                BirimFiyat = x.BirimFiyat,
                KdvOrani = x.KdvOrani
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Aciklama) || x.BirimFiyat > 0)
            .ToList();
    }

    private void KalemleriDogrula(List<FaturaKalemForm> kalemler)
    {
        if (!kalemler.Any())
            ModelState.AddModelError("", "En az bir fatura kalemi girilmelidir.");

        for (var i = 0; i < kalemler.Count; i++)
        {
            var kalem = kalemler[i];
            var satir = i + 1;

            if (string.IsNullOrWhiteSpace(kalem.Aciklama))
                ModelState.AddModelError("", $"{satir}. kalem açıklaması zorunludur.");

            if (kalem.Miktar <= 0)
                ModelState.AddModelError("", $"{satir}. kalem miktarı sıfırdan büyük olmalıdır.");

            if (kalem.BirimFiyat <= 0)
                ModelState.AddModelError("", $"{satir}. kalem birim fiyatı sıfırdan büyük olmalıdır.");

            if (kalem.KdvOrani < 0)
                ModelState.AddModelError("", $"{satir}. kalem KDV oranı negatif olamaz.");
        }
    }

    private static List<FaturaKalem> FaturaKalemleriOlustur(IEnumerable<FaturaKalemForm> kalemler)
    {
        return kalemler.Select(kalem =>
        {
            var araToplam = kalem.Miktar * kalem.BirimFiyat;
            var kdvTutar = araToplam * kalem.KdvOrani / 100m;
            return new FaturaKalem
            {
                Aciklama = kalem.Aciklama ?? "",
                Miktar = kalem.Miktar,
                BirimFiyat = kalem.BirimFiyat,
                KdvOrani = kalem.KdvOrani,
                AraToplam = araToplam,
                KdvTutar = kdvTutar,
                GenelToplam = araToplam + kdvTutar
            };
        }).ToList();
    }

    private static DateTime ToUtcDate(DateTime value)
    {
        return DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
    }

    public class FaturaDuzenleForm
    {
        public int Id { get; set; }
        public int CariKartId { get; set; }
        public string FaturaNo { get; set; } = "";
        public FaturaTipi Tip { get; set; } = FaturaTipi.Satis;
        public DateTime Tarih { get; set; } = DateTime.UtcNow.Date;
        public DateTime? VadeTarihi { get; set; }
        public List<FaturaKalemForm> Kalemler { get; set; } = new() { new FaturaKalemForm() };
        public string? Aciklama { get; set; }
    }

    public class FaturaKalemForm
    {
        public string? Aciklama { get; set; }
        public decimal Miktar { get; set; } = 1;
        public decimal BirimFiyat { get; set; }
        public decimal KdvOrani { get; set; } = 20;
    }
}