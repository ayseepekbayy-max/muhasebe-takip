using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages.Calisanlar;

public class PuantajModel : PageModel
{
    private readonly AppDbContext _db;

    public PuantajModel(AppDbContext db)
    {
        _db = db;
    }

    public Calisan? Calisan { get; set; }

    public List<PuantajGunViewModel> AylikGunler { get; set; } = new();

    [BindProperty]
    public int CalisanId { get; set; }

    [BindProperty]
    public DateTime Tarih { get; set; } = DateTime.Today;

    [BindProperty]
    public PuantajDurum Durum { get; set; } = PuantajDurum.Geldi;

    [BindProperty]
    public string? Not { get; set; }

    [BindProperty(SupportsGet = true)]
    public int SeciliYil { get; set; }

    [BindProperty(SupportsGet = true)]
    public int SeciliAy { get; set; }

    public int ToplamGeldi { get; set; }
    public int ToplamGelmedi { get; set; }
    public int ToplamIzinli { get; set; }
    public int ToplamYarimGun { get; set; }

    public string Hata { get; set; } = "";
    public string Mesaj { get; set; } = "";

    public async Task<IActionResult> OnGetAsync(int id, int? yil, int? ay)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        CalisanId = id;
        SeciliYil = yil ?? DateTime.Today.Year;
        SeciliAy = ay ?? DateTime.Today.Month;

        await YukleAsync(firmaId.Value, id, SeciliYil, SeciliAy);

        if (Calisan == null)
            return RedirectToPage("/Calisanlar");

        return Page();
    }

    public async Task<IActionResult> OnPostKaydetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var calisan = await _db.Calisanlar
            .FirstOrDefaultAsync(x => x.Id == CalisanId && x.FirmaId == firmaId);

        if (calisan == null)
            return RedirectToPage("/Calisanlar");

        if (Tarih.DayOfWeek == DayOfWeek.Sunday)
            return RedirectToPage(new { id = CalisanId, yil = SeciliYil, ay = SeciliAy });

        var mevcut = await _db.CalisanPuantajlari
            .FirstOrDefaultAsync(x =>
                x.CalisanId == CalisanId &&
                x.FirmaId == firmaId &&
                x.Tarih.Date == Tarih.Date);

        if (mevcut != null)
        {
            mevcut.Durum = Durum;
            mevcut.Not = Not;
        }
        else
        {
            _db.CalisanPuantajlari.Add(new CalisanPuantaj
            {
                FirmaId = firmaId.Value,
                CalisanId = CalisanId,
                Tarih = Tarih.Date,
                Durum = Durum,
                Not = Not
            });
        }

        await _db.SaveChangesAsync();

        return RedirectToPage(new { id = CalisanId, yil = SeciliYil, ay = SeciliAy });
    }

    public async Task<IActionResult> OnPostDisaAktarAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleAsync(firmaId.Value, CalisanId, SeciliYil, SeciliAy);

        if (Calisan == null)
            return RedirectToPage("/Calisanlar");

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Puantaj");

        ws.Cell(1, 1).Value = "Çalışan";
        ws.Cell(1, 2).Value = Calisan.AdSoyad ?? "";

        ws.Cell(2, 1).Value = "Ay";
        ws.Cell(2, 2).Value = $"{SeciliAy}/{SeciliYil}";

        ws.Cell(4, 1).Value = "Tarih";
        ws.Cell(4, 2).Value = "Gün";
        ws.Cell(4, 3).Value = "Durum";
        ws.Cell(4, 4).Value = "Not";

        var header = ws.Range(4, 1, 4, 4);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightGray;
        header.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        header.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        int row = 5;

        foreach (var gun in AylikGunler)
        {
            string durumText = gun.Tarih.DayOfWeek == DayOfWeek.Sunday
                ? "Hafta Sonu"
                : gun.Durum switch
                {
                    PuantajDurum.Geldi => "Geldi",
                    PuantajDurum.Gelmedi => "Gelmedi",
                    PuantajDurum.Izinli => "İzinli",
                    PuantajDurum.YarimGun => "Yarım Gün",
                    _ => ""
                };

            ws.Cell(row, 1).Value = gun.Tarih;
            ws.Cell(row, 1).Style.DateFormat.Format = "dd.MM.yyyy";
            ws.Cell(row, 2).Value = gun.Tarih.ToString("dddd");
            ws.Cell(row, 3).Value = durumText;
            ws.Cell(row, 4).Value = gun.Not ?? "";
            row++;
        }

        int summaryStart = row + 2;
        ws.Cell(summaryStart, 1).Value = "Özet";
        ws.Cell(summaryStart, 1).Style.Font.Bold = true;

        ws.Cell(summaryStart + 1, 1).Value = "Toplam Geldi";
        ws.Cell(summaryStart + 1, 2).Value = ToplamGeldi;

        ws.Cell(summaryStart + 2, 1).Value = "Toplam Gelmedi";
        ws.Cell(summaryStart + 2, 2).Value = ToplamGelmedi;

        ws.Cell(summaryStart + 3, 1).Value = "Toplam İzinli";
        ws.Cell(summaryStart + 3, 2).Value = ToplamIzinli;

        ws.Cell(summaryStart + 4, 1).Value = "Toplam Yarım Gün";
        ws.Cell(summaryStart + 4, 2).Value = ToplamYarimGun;

        ws.Columns().AdjustToContents();

        var fullRange = ws.Range(4, 1, row - 1, 4);
        fullRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        fullRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var dosyaAdi = $"puantaj_{Calisan.AdSoyad}_{SeciliYil}_{SeciliAy}.xlsx";

        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            dosyaAdi
        );
    }

    private async Task YukleAsync(int firmaId, int calisanId, int yil, int ay)
    {
        Calisan = await _db.Calisanlar
            .FirstOrDefaultAsync(x => x.Id == calisanId && x.FirmaId == firmaId);

        if (Calisan == null)
            return;

        var ayBaslangic = new DateTime(yil, ay, 1);
        var ayBitis = ayBaslangic.AddMonths(1).AddDays(-1);

        var kayitlar = await _db.CalisanPuantajlari
            .Where(x =>
                x.CalisanId == calisanId &&
                x.FirmaId == firmaId &&
                x.Tarih >= ayBaslangic &&
                x.Tarih <= ayBitis)
            .OrderBy(x => x.Tarih)
            .ToListAsync();

        AylikGunler = new List<PuantajGunViewModel>();

        for (var gun = ayBaslangic; gun <= ayBitis; gun = gun.AddDays(1))
        {
            var kayit = kayitlar.FirstOrDefault(x => x.Tarih.Date == gun.Date);

            AylikGunler.Add(new PuantajGunViewModel
            {
                Tarih = gun,
                Durum = kayit?.Durum ?? PuantajDurum.Gelmedi,
                Not = kayit?.Not
            });
        }

        var sayilacakGunler = AylikGunler
            .Where(x => x.Tarih.DayOfWeek != DayOfWeek.Sunday)
            .ToList();

        ToplamGeldi = sayilacakGunler.Count(x => x.Durum == PuantajDurum.Geldi);
        ToplamGelmedi = sayilacakGunler.Count(x => x.Durum == PuantajDurum.Gelmedi);
        ToplamIzinli = sayilacakGunler.Count(x => x.Durum == PuantajDurum.Izinli);
        ToplamYarimGun = sayilacakGunler.Count(x => x.Durum == PuantajDurum.YarimGun);
    }

    public class PuantajGunViewModel
    {
        public DateTime Tarih { get; set; }
        public PuantajDurum Durum { get; set; }
        public string? Not { get; set; }
    }
}