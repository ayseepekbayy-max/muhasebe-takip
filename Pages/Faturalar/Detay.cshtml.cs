using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using Microsoft.AspNetCore.Hosting;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages.Faturalar;

[TypeFilter(typeof(AccountingPageContextFilter))]
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

    public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> HesapSecenekleri { get; set; } = new();

    public Fatura? Fatura { get; set; }
    public Firma? Firma { get; set; }
    public List<KasaHareket> Hareketler { get; set; } = new();
    public List<EkDosya> Dosyalar { get; set; } = new();

    [BindProperty]
    public IFormFile? EkDosya { get; set; }

    [BindProperty]
    public DurumGuncelleForm DurumForm { get; set; } = new();

    public async Task<IActionResult> OnGetAsync([FromRoute] int id)
    {
        var firmaId = AccountingPageContextFilter.CompanyId(HttpContext);

        if (!await InvoiceScope.CanAccessAsync(_db, firmaId, id, HttpContext.RequestAborted))
            return StatusCode(403);

        await YukleAsync(id, firmaId);
        if (Fatura?.CariKartId != null && Fatura.CariKart?.FirmaId != firmaId)
            return StatusCode(403);
        if (Fatura == null)
            return NotFound();

        return Page();
    }

    public Task<IActionResult> OnPostDurumGuncelleAsync([FromRoute] int id)
        => InvoicePaymentTransaction.RunPageAsync(_db, this, () => DurumGuncelleAsync(id));

    private async Task<IActionResult> DurumGuncelleAsync(int id)
    {
        var firmaId = AccountingPageContextFilter.CompanyId(HttpContext);

        await InvoicePaymentTransaction.LockInvoiceAsync(_db, firmaId, id, HttpContext.RequestAborted);
        if (!await InvoiceScope.CanAccessAsync(_db, firmaId, id, HttpContext.RequestAborted))
            return StatusCode(403);

        var fatura = await _db.Faturalar
            .Include(x => x.CariKart)
            .Include(x => x.Kalemler)
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId && x.AktifMi, HttpContext.RequestAborted);

        if (fatura == null)
            return NotFound();

        if (await InvoiceScope.ForeignAccountAsync(_db, firmaId, DurumForm.KasaHesapId, HttpContext.RequestAborted))
            return StatusCode(403);

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

        if (DurumForm.Durum == FaturaDurumu.Odendi && fatura.KalanTutar > 0 &&
            !await KasaHesapService.GecerliMiAsync(_db, firmaId, DurumForm.KasaHesapId))
        {
            TempData["Hata"] = KasaHesapService.SecimHatasi;
            return RedirectToPage(new { id });
        }

        var eskiDeger = IslemGecmisiSnapshots.Fatura(fatura);
        var eskiDurum = fatura.Durum;
        var kalan = Math.Max(0, fatura.GenelToplam - fatura.OdenenToplam);
        KasaHareket? kasaHareketi = null;

        if (DurumForm.Durum == FaturaDurumu.Odendi)
        {
            if (kalan > 0)
            {
                var islemAdi = fatura.Tip == FaturaTipi.Satis ? "Tahsilat" : "Ödeme";
                kasaHareketi = new KasaHareket
                {
                    FirmaId = firmaId,
                    KasaHesapId = DurumForm.KasaHesapId,
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

    public async Task<IActionResult> OnPostDosyaEkleAsync([FromRoute] int id)
    {
        var firmaId = AccountingPageContextFilter.CompanyId(HttpContext);

        var ct = HttpContext.RequestAborted;
        await using var transaction = await InvoicePaymentTransaction.BeginAsync(_db, ct);
        await InvoicePaymentTransaction.LockInvoiceAsync(_db, firmaId, id, ct);
        if (!await InvoiceScope.CanAccessAsync(_db, firmaId, id, ct))
            return StatusCode(403);

        var faturaVarMi = await _db.Faturalar.AnyAsync(x => x.Id == id && x.FirmaId == firmaId && x.AktifMi, ct);
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
        var fileCreated = false;
        var commitStarted = false;
        try
        {
            await using (var fs = new FileStream(fizikselYol, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fileCreated = true;
                await EkDosya.CopyToAsync(fs, ct);
            }

            var belge = new EkDosya
            {
                FirmaId = firmaId,
                FaturaId = id,
                DosyaAdi = Path.GetFileName(EkDosya.FileName),
                DosyaYolu = $"/uploads/faturalar/{dosyaAdi}",
                IcerikTipi = EkDosya.ContentType,
                Boyut = EkDosya.Length,
                YuklemeTarihi = DateTime.UtcNow
            };
            _db.EkDosyalar.Add(belge);
            await _db.SaveChangesWithAuditAsync(
                () => _islemGecmisi.KaydetAsync("Faturalar", "Belge Ekleme", $"Fatura {id} için belge eklendi.",
                    yeniDeger: new { id, belgeId = belge.Id, belge.DosyaAdi }, cancellationToken: ct),
                anaKaydiOnceKaydet: true, cancellationToken: ct);
            // A failed commit acknowledgement does not prove that the database rolled back.
            commitStarted = true;
            await transaction.CommitAsync(ct);
        }
        catch
        {
            if (fileCreated && !commitStarted)
                System.IO.File.Delete(fizikselYol);
            throw;
        }

        TempData["Basari"] = "Dosya faturaya eklendi.";
        return RedirectToPage(new { id });
    }

    private async Task YukleAsync(int id, int firmaId)
    {
        HesapSecenekleri = await KasaHesapService.SeceneklerAsync(_db, firmaId);
        Firma = await _db.Firmalar.FirstOrDefaultAsync(x => x.Id == firmaId);
        Fatura = await _db.Faturalar
            .Include(x => x.CariKart)
            .Include(x => x.Kalemler)
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId && x.AktifMi, HttpContext.RequestAborted);

        if (Fatura == null)
            return;

        DurumForm = new DurumGuncelleForm
        {
            Durum = Fatura.Durum,
            Tarih = DateTime.UtcNow.Date
        };

        Hareketler = await _db.KasaHareketleri
            .Where(x => x.FirmaId == firmaId && (x.FaturaId == id ||
                (x.FaturaId == null && Fatura.FaturaNo != "" && x.Aciklama.Contains(Fatura.FaturaNo))) &&
                (x.CariKartId == null || (x.CariKart != null && x.CariKart.FirmaId == firmaId)) &&
                (x.KasaHesapId == null || (x.KasaHesap != null && x.KasaHesap.FirmaId == firmaId)))
            .OrderByDescending(x => x.Tarih)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        Dosyalar = await _db.EkDosyalar
            .Where(x => x.FirmaId == firmaId && x.FaturaId == id && x.CariKartId == null)
            .OrderByDescending(x => x.YuklemeTarihi)
            .ToListAsync();
    }

    public class DurumGuncelleForm
    {
        public int? KasaHesapId { get; set; }
        public FaturaDurumu Durum { get; set; } = FaturaDurumu.Bekliyor;
        public DateTime Tarih { get; set; } = DateTime.UtcNow.Date;
    }
}
