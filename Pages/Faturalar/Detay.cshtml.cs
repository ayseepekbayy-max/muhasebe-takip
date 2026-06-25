using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using Microsoft.AspNetCore.Hosting;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages.Faturalar;

public class DetayModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IIslemGecmisiService _islemGecmisi;

    public DetayModel(
        AppDbContext db,
        IWebHostEnvironment env,
        IIslemGecmisiService islemGecmisi)
    {
        _db = db;
        _env = env;
        _islemGecmisi = islemGecmisi;
    }

    public Fatura? Fatura { get; set; }
    public Firma? Firma { get; set; }
    public List<KasaHareket> Hareketler { get; set; } = new();
    public List<EkDosya> Dosyalar { get; set; } = new();

    [BindProperty]
    public IFormFile? EkDosya { get; set; }

    [BindProperty]
    public DurumGuncelleForm DurumForm { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleAsync(id, firmaId.Value);
        if (Fatura == null)
            return NotFound();

        return Page();
    }

    public async Task<IActionResult> OnPostDurumGuncelleAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var fatura = await _db.Faturalar
            .Include(x => x.CariKart)
            .Include(x => x.Kalemler)
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId.Value);

        if (fatura == null)
            return NotFound();

        if (!Enum.IsDefined(DurumForm.Durum))
        {
            TempData["Hata"] = "Geçersiz fatura durumu.";
            return RedirectToPage(new { id });
        }

        if (DurumForm.Durum == fatura.Durum)
        {
            TempData["Basari"] = "Fatura durumu zaten seçilen değerde.";
            return RedirectToPage(new { id });
        }

        if (DurumForm.Durum == FaturaDurumu.Bekliyor && fatura.OdenenToplam > 0)
        {
            TempData["Hata"] = "Ödeme bulunan fatura Bekliyor durumuna alınamaz.";
            return RedirectToPage(new { id });
        }

        if (DurumForm.Durum == FaturaDurumu.KismenOdendi &&
            (fatura.OdenenToplam <= 0 || fatura.OdenenToplam >= fatura.GenelToplam))
        {
            TempData["Hata"] = "Kısmen Ödendi durumu için fatura üzerinde kısmi ödeme bulunmalıdır.";
            return RedirectToPage(new { id });
        }

        var eskiDeger = IslemGecmisiSnapshots.Fatura(fatura);
        var eskiDurum = fatura.Durum;
        var kalan = Math.Max(0, fatura.GenelToplam - fatura.OdenenToplam);
        KasaHareket? kasaHareketi = null;

        if (DurumForm.Durum == FaturaDurumu.Odendi)
        {
            if (DurumForm.KasaHareketiOlustur && kalan > 0)
            {
                var islemAdi = fatura.Tip == FaturaTipi.Satis ? "Tahsilat" : "Ödeme";
                kasaHareketi = new KasaHareket
                {
                    FirmaId = firmaId.Value,
                    CariKartId = fatura.CariKartId,
                    FaturaId = fatura.Id,
                    Tarih = IndexModel.ToUtcDate(DurumForm.Tarih),
                    Tip = fatura.Tip == FaturaTipi.Satis ? HareketTipi.Giris : HareketTipi.Cikis,
                    Tutar = kalan,
                    Aciklama = $"{islemAdi} - {fatura.FaturaNo} - {fatura.CariKart?.Unvan}"
                };
                _db.KasaHareketleri.Add(kasaHareketi);
            }

            fatura.OdenenToplam = fatura.GenelToplam;
        }

        fatura.Durum = DurumForm.Durum;

        await _db.SaveChangesWithAuditAsync(
            async () =>
            {
                await _islemGecmisi.KaydetAsync(
                    "Faturalar",
                    "Durum Değişikliği",
                    $"Fatura durumu değiştirildi: {eskiDurum.Metin()} → {fatura.Durum.Metin()}.",
                    eskiDeger,
                    IslemGecmisiSnapshots.Fatura(fatura));

                if (fatura.Durum == FaturaDurumu.Odendi)
                {
                    await _islemGecmisi.KaydetAsync(
                        "Faturalar",
                        "Ödeme",
                        $"Fatura ödendi: {fatura.FaturaNo}.",
                        eskiDeger,
                        IslemGecmisiSnapshots.Fatura(fatura));
                }
                else if (fatura.Durum == FaturaDurumu.Iptal)
                {
                    await _islemGecmisi.KaydetAsync(
                        "Faturalar",
                        "İptal",
                        $"Fatura iptal edildi: {fatura.FaturaNo}.",
                        eskiDeger,
                        IslemGecmisiSnapshots.Fatura(fatura));
                }

                if (kasaHareketi != null)
                {
                    await _islemGecmisi.KaydetAsync(
                        "Kasa",
                        "Ekleme",
                        $"{fatura.FaturaNo} faturası ödendi işaretlenirken kasa hareketi oluşturuldu.",
                        yeniDeger: IslemGecmisiSnapshots.KasaHareket(kasaHareketi));
                }
            },
            anaKaydiOnceKaydet: false);

        TempData["Basari"] = "Fatura durumu güncellendi.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDosyaEkleAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var faturaVarMi = await _db.Faturalar.AnyAsync(x => x.Id == id && x.FirmaId == firmaId.Value);
        if (!faturaVarMi)
            return NotFound();

        if (EkDosya == null || EkDosya.Length == 0)
        {
            TempData["Hata"] = "Lütfen bir dosya seçin.";
            return RedirectToPage(new { id });
        }

        var uzanti = Path.GetExtension(EkDosya.FileName).ToLowerInvariant();
        var izinli = new[] { ".pdf", ".png", ".jpg", ".jpeg", ".webp", ".xlsx", ".xls", ".docx" };
        if (!izinli.Contains(uzanti))
        {
            TempData["Hata"] = "PDF, resim, Excel veya Word dosyası yükleyebilirsiniz.";
            return RedirectToPage(new { id });
        }

        var klasor = Path.Combine(_env.WebRootPath, "uploads", "faturalar");
        Directory.CreateDirectory(klasor);
        var dosyaAdi = $"fatura-{id}-{Guid.NewGuid():N}{uzanti}";
        var fizikselYol = Path.Combine(klasor, dosyaAdi);
        await using var fs = System.IO.File.Create(fizikselYol);
        await EkDosya.CopyToAsync(fs);

        _db.EkDosyalar.Add(new EkDosya
        {
            FirmaId = firmaId.Value,
            FaturaId = id,
            DosyaAdi = Path.GetFileName(EkDosya.FileName),
            DosyaYolu = $"/uploads/faturalar/{dosyaAdi}",
            IcerikTipi = EkDosya.ContentType,
            Boyut = EkDosya.Length,
            YuklemeTarihi = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        TempData["Basari"] = "Dosya faturaya eklendi.";
        return RedirectToPage(new { id });
    }

    private async Task YukleAsync(int id, int firmaId)
    {
        Firma = await _db.Firmalar.FirstOrDefaultAsync(x => x.Id == firmaId);
        Fatura = await _db.Faturalar
            .Include(x => x.CariKart)
            .Include(x => x.Kalemler)
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId);

        if (Fatura == null)
            return;

        DurumForm = new DurumGuncelleForm
        {
            Durum = Fatura.Durum,
            Tarih = DateTime.UtcNow.Date
        };

        Hareketler = await _db.KasaHareketleri
            .Where(x => x.FirmaId == firmaId && (x.FaturaId == id || x.Aciklama.Contains(Fatura.FaturaNo)))
            .OrderByDescending(x => x.Tarih)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        Dosyalar = await _db.EkDosyalar
            .Where(x => x.FirmaId == firmaId && x.FaturaId == id)
            .OrderByDescending(x => x.YuklemeTarihi)
            .ToListAsync();
    }

    public class DurumGuncelleForm
    {
        public FaturaDurumu Durum { get; set; } = FaturaDurumu.Bekliyor;
        public bool KasaHareketiOlustur { get; set; }
        public DateTime Tarih { get; set; } = DateTime.UtcNow.Date;
    }
}
