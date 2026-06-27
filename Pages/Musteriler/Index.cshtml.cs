using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages.Musteriler;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IIslemGecmisiService _islemGecmisi;

    public IndexModel(AppDbContext db, IIslemGecmisiService islemGecmisi)
    {
        _db = db;
        _islemGecmisi = islemGecmisi;
    }

    public List<MusteriOzet> Liste { get; set; } = new();

    public int ToplamMusteri { get; set; }
    public int AktifIsSayisi { get; set; }
    public int TamamlananIsSayisi { get; set; }
    public decimal ToplamCiro { get; set; }
    public decimal ToplamMasraf { get; set; }
    public decimal ToplamKar => ToplamCiro - ToplamMasraf;

    [BindProperty(SupportsGet = true)]
    public string? MusteriAra { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? TelefonAra { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? IsDurumuFiltre { get; set; }

    [BindProperty]
    [ValidateNever]
    public Musteri Yeni { get; set; } = new();

    public string Hata { get; set; } = "";
    public string Mesaj { get; set; } = "";
    public bool YeniMusteriModalAcik { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await ListeyiYukleAsync(firmaId.Value);
        return Page();
    }

    public async Task<IActionResult> OnPostEkleAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        Yeni.AdSoyad = (Yeni.AdSoyad ?? "").Trim();
        Yeni.Telefon = (Yeni.Telefon ?? "").Trim();
        Yeni.Adres = (Yeni.Adres ?? "").Trim();

        if (string.IsNullOrWhiteSpace(Yeni.AdSoyad))
        {
            ModelState.AddModelError("", "Ad Soyad boş olamaz.");
            YeniMusteriModalAcik = true;
            await ListeyiYukleAsync(firmaId.Value);
            return Page();
        }

        Yeni.Ad = Yeni.AdSoyad;
        Yeni.FirmaId = firmaId.Value;
        Yeni.AktifMi = true;
        Yeni.ArsivTarihi = null;
        Yeni.ArsivNotu = null;

        _db.Musteriler.Add(Yeni);
        await _db.SaveChangesWithAuditAsync(
            () => _islemGecmisi.KaydetAsync(
                "Müşteriler",
                "Ekleme",
                $"{Yeni.AdSoyad} müşterisi eklendi (ID: {Yeni.Id}).",
                yeniDeger: IslemGecmisiSnapshots.Musteri(Yeni)),
            anaKaydiOnceKaydet: true);

        TempData["Basari"] = "Müşteri eklendi.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSilAsync(int id)
    {
        ModelState.Clear();

        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var musteri = await _db.Musteriler
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId && x.AktifMi);

        if (musteri == null)
            return RedirectToPage();

        var eskiDeger = IslemGecmisiSnapshots.Musteri(musteri);
        musteri.AktifMi = false;
        musteri.ArsivTarihi = DateTime.UtcNow;
        musteri.ArsivNotu = "Silme yerine veri korunarak arşive taşındı.";

        await _db.SaveChangesWithAuditAsync(
            () => _islemGecmisi.KaydetAsync(
                "Musteriler",
                "Arsivleme",
                $"{musteri.AdSoyad} musterisi silinmeden arsive tasindi (ID: {musteri.Id}).",
                eskiDeger,
                IslemGecmisiSnapshots.Musteri(musteri)),
            anaKaydiOnceKaydet: false);

        TempData["Basari"] = "Musteri silinmeden arsive tasindi. Is ve masraf kayitlari korundu.";
        return RedirectToPage();

    }

    public async Task<IActionResult> OnGetDisaAktarAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await ListeyiYukleAsync(firmaId.Value);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Müşteriler");
        var basliklar = new[]
        {
            "Ad Soyad", "Telefon", "Adres", "İş Sayısı", "Aktif İş",
            "Tamamlanan İş", "Toplam Ciro", "Toplam Masraf", "Toplam Kar", "Son İş Tarihi"
        };

        for (var i = 0; i < basliklar.Length; i++)
            ws.Cell(1, i + 1).Value = basliklar[i];

        var header = ws.Range(1, 1, 1, basliklar.Length);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightGray;
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        header.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        var row = 2;
        foreach (var m in Liste)
        {
            ws.Cell(row, 1).Value = m.AdSoyad;
            ws.Cell(row, 2).Value = m.Telefon ?? "";
            ws.Cell(row, 3).Value = m.Adres ?? "";
            ws.Cell(row, 4).Value = m.IsSayisi;
            ws.Cell(row, 5).Value = m.AktifIsSayisi;
            ws.Cell(row, 6).Value = m.TamamlananIsSayisi;
            ws.Cell(row, 7).Value = m.ToplamCiro;
            ws.Cell(row, 8).Value = m.ToplamMasraf;
            ws.Cell(row, 9).Value = m.ToplamKar;
            if (m.SonIsTarihi.HasValue)
                ws.Cell(row, 10).Value = m.SonIsTarihi.Value;
            row++;
        }

        ws.Columns(7, 9).Style.NumberFormat.Format = "#,##0.00 ₺";
        ws.Column(10).Style.DateFormat.Format = "dd.MM.yyyy";
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();

        if (row > 2)
        {
            var range = ws.Range(1, 1, row - 1, basliklar.Length);
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"musteriler_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
    }

    public async Task<IActionResult> OnPostDisaAktarAsync() => await OnGetDisaAktarAsync();

    private async Task ListeyiYukleAsync(int firmaId)
    {
        var genelOzet = await _db.Musteriler
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId && x.AktifMi)
            .GroupBy(_ => 1)
            .Select(grup => new { Toplam = grup.Count() })
            .FirstOrDefaultAsync();

        ToplamMusteri = genelOzet?.Toplam ?? 0;

        var isOzeti = await _db.MusteriIsler
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId)
            .GroupBy(_ => 1)
            .Select(grup => new
            {
                Aktif = grup.Count(x => x.Gelir <= 0),
                Tamamlanan = grup.Count(x => x.Gelir > 0),
                Ciro = grup.Sum(x => (decimal?)x.Gelir) ?? 0
            })
            .FirstOrDefaultAsync();

        var masrafOzeti = await _db.MusteriMasraflar
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId)
            .GroupBy(_ => 1)
            .Select(grup => new { Masraf = grup.Sum(x => (decimal?)x.Tutar) ?? 0 })
            .FirstOrDefaultAsync();

        AktifIsSayisi = isOzeti?.Aktif ?? 0;
        TamamlananIsSayisi = isOzeti?.Tamamlanan ?? 0;
        ToplamCiro = isOzeti?.Ciro ?? 0;
        ToplamMasraf = masrafOzeti?.Masraf ?? 0;

        var sorgu = _db.Musteriler
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId && x.AktifMi);

        if (!string.IsNullOrWhiteSpace(MusteriAra))
        {
            var arama = MusteriAra.Trim();
            sorgu = sorgu.Where(x =>
                x.AdSoyad.Contains(arama) ||
                (x.Adres != null && x.Adres.Contains(arama)));
        }

        if (!string.IsNullOrWhiteSpace(TelefonAra))
        {
            var telefon = TelefonAra.Trim();
            sorgu = sorgu.Where(x => x.Telefon != null && x.Telefon.Contains(telefon));
        }

        var listeSorgu = sorgu.Select(musteri => new MusteriOzet
        {
            Id = musteri.Id,
            AdSoyad = musteri.AdSoyad,
            Telefon = musteri.Telefon,
            Adres = musteri.Adres,
            IsSayisi = _db.MusteriIsler.Count(x => x.FirmaId == firmaId && x.MusteriId == musteri.Id),
            AktifIsSayisi = _db.MusteriIsler.Count(x => x.FirmaId == firmaId && x.MusteriId == musteri.Id && x.Gelir <= 0),
            TamamlananIsSayisi = _db.MusteriIsler.Count(x => x.FirmaId == firmaId && x.MusteriId == musteri.Id && x.Gelir > 0),
            ToplamCiro = _db.MusteriIsler
                .Where(x => x.FirmaId == firmaId && x.MusteriId == musteri.Id)
                .Sum(x => (decimal?)x.Gelir) ?? 0,
            ToplamMasraf = _db.MusteriMasraflar
                .Where(x => x.FirmaId == firmaId && x.MusteriIs != null && x.MusteriIs.MusteriId == musteri.Id)
                .Sum(x => (decimal?)x.Tutar) ?? 0,
            SonIsTarihi = _db.MusteriIsler
                .Where(x => x.FirmaId == firmaId && x.MusteriId == musteri.Id)
                .OrderByDescending(x => x.Tarih)
                .Select(x => (DateTime?)x.Tarih)
                .FirstOrDefault()
        });

        if (!string.IsNullOrWhiteSpace(IsDurumuFiltre))
        {
            listeSorgu = IsDurumuFiltre switch
            {
                "Aktif" => listeSorgu.Where(x => x.AktifIsSayisi > 0),
                "Tamamlanan" => listeSorgu.Where(x => x.TamamlananIsSayisi > 0),
                "IsYok" => listeSorgu.Where(x => x.IsSayisi == 0),
                "Karli" => listeSorgu.Where(x => x.ToplamCiro - x.ToplamMasraf > 0),
                "Zarar" => listeSorgu.Where(x => x.ToplamCiro - x.ToplamMasraf < 0),
                _ => listeSorgu
            };
        }

        Liste = await listeSorgu
            .OrderByDescending(x => x.SonIsTarihi ?? DateTime.MinValue)
            .ThenBy(x => x.AdSoyad)
            .ToListAsync();
    }

    public class MusteriOzet
    {
        public int Id { get; set; }
        public string AdSoyad { get; set; } = "";
        public string? Telefon { get; set; }
        public string? Adres { get; set; }
        public int IsSayisi { get; set; }
        public int AktifIsSayisi { get; set; }
        public int TamamlananIsSayisi { get; set; }
        public decimal ToplamCiro { get; set; }
        public decimal ToplamMasraf { get; set; }
        public decimal ToplamKar => ToplamCiro - ToplamMasraf;
        public DateTime? SonIsTarihi { get; set; }
    }
}
