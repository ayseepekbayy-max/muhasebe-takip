using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Helpers;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;
using UglyToad.PdfPig;

namespace MuhasebeTakip2.App.Pages.Maliyet;

public class BelgeOkuModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IIslemGecmisiService _islemGecmisi;

    public BelgeOkuModel(AppDbContext db, IIslemGecmisiService islemGecmisi)
    {
        _db = db;
        _islemGecmisi = islemGecmisi;
    }

    [BindProperty]
    public IFormFile? Dosya { get; set; }

    [BindProperty]
    public string UretimAdi { get; set; } = "";

    [BindProperty]
    public string OkunanMetin { get; set; } = "";

    [BindProperty]
    public decimal ToplamMaliyet { get; set; }

    [BindProperty]
    public List<BelgeMaliyetKalemi> Kalemler { get; set; } = new();

    public string Hata { get; set; } = "";
    public string Mesaj { get; set; } = "";

    public IActionResult OnGet()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");

        if (firmaId == null)
            return RedirectToPage("/Login");

        return Page();
    }

    public async Task<IActionResult> OnPostOkuAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");

        if (firmaId == null)
            return RedirectToPage("/Login");

        if (Dosya == null || Dosya.Length == 0)
        {
            Hata = "Lütfen PDF, Word veya metin dosyası seçin.";
            return Page();
        }

        if (Dosya.Length > 10 * 1024 * 1024)
        {
            Hata = "Dosya çok büyük. En fazla 10 MB dosya yükleyin.";
            return Page();
        }

        UretimAdi = string.IsNullOrWhiteSpace(UretimAdi)
            ? Path.GetFileNameWithoutExtension(Dosya.FileName)
            : UretimAdi.Trim();

        try
        {
            await using var memory = new MemoryStream();
            await Dosya.CopyToAsync(memory);

            OkunanMetin = MetinCikar(Dosya.FileName, memory.ToArray());
            Kalemler = KalemleriBul(OkunanMetin);

            ToplamMaliyet = Kalemler.Sum(x => x.ToplamTutar);

            if (string.IsNullOrWhiteSpace(OkunanMetin))
            {
                Hata = "Dosyadan okunabilir metin çıkarılamadı. Eğer belge taranmış görselse OCR gerekir.";
                return Page();
            }

            if (!Kalemler.Any())
            {
                Mesaj = "Metin okundu ancak net bir maliyet satırı bulunamadı. Toplam tutarı elle düzenleyip kaydedebilirsiniz.";
            }
            else
            {
                Mesaj = "Belge okundu. Bulunan tutarları kontrol edip arşive kaydedebilirsiniz.";
            }
        }
        catch (Exception ex)
        {
            Hata = "Belge okunurken hata oluştu: " + ex.Message;
        }

        return Page();
    }

    public IActionResult OnPostMetinOku()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");

        if (firmaId == null)
            return RedirectToPage("/Login");

        UretimAdi = string.IsNullOrWhiteSpace(UretimAdi)
            ? "OCR ile okunan belge"
            : UretimAdi.Trim();

        if (string.IsNullOrWhiteSpace(OkunanMetin))
        {
            Hata = "OCR sonucunda okunabilir metin bulunamadı.";
            return Page();
        }

        Kalemler = KalemleriBul(OkunanMetin);
        ToplamMaliyet = Kalemler.Sum(x => x.ToplamTutar);

        Mesaj = Kalemler.Any()
            ? "OCR metni okundu. Bulunan tutarları kontrol edip arşive kaydedebilirsiniz."
            : "OCR metni okundu ancak net bir maliyet satırı bulunamadı. Toplam tutarı elle girerek kaydedebilirsiniz.";

        return Page();
    }

    public async Task<IActionResult> OnPostKaydetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");

        if (firmaId == null)
            return RedirectToPage("/Login");

        UretimAdi = (UretimAdi ?? "").Trim();

        if (string.IsNullOrWhiteSpace(UretimAdi))
        {
            Hata = "Kaydetmek için ürün veya iş adı gerekli.";
            return Page();
        }

        if (ToplamMaliyet <= 0)
        {
            Hata = "Kaydetmek için toplam maliyet 0'dan büyük olmalı.";
            return Page();
        }

        var kayit = new MaliyetKaydi
        {
            FirmaId = firmaId.Value,
            UretimAdi = UretimAdi,
            UretimAdedi = 1,
            MalzemeMaliyeti = ToplamMaliyet,
            ToplamMaliyet = ToplamMaliyet,
            BirimMaliyet = ToplamMaliyet,
            Kaynak = "Belge",
            DetayJson = JsonSerializer.Serialize(new MaliyetKaydiDetay
            {
                BelgeKalemleri = Kalemler
                    .Select(x => new MaliyetDetaySatiri
                    {
                        Aciklama = x.Aciklama,
                        Adet = 1,
                        BirimFiyat = x.ToplamTutar,
                        Toplam = x.ToplamTutar,
                        Not = "Belgeden otomatik okundu."
                    })
                    .ToList()
            }),
            OkunanMetin = OkunanMetin ?? "",
            HesapTarihi = DateTime.UtcNow
        };

        _db.MaliyetKayitlari.Add(kayit);
        await _db.SaveChangesWithAuditAsync(
            () => _islemGecmisi.KaydetAsync(
                "Maliyet",
                "Ekleme",
                $"{kayit.UretimAdi} belge kaynaklı maliyet kaydı eklendi (ID: {kayit.Id}).",
                yeniDeger: IslemGecmisiSnapshots.Maliyet(kayit)),
            anaKaydiOnceKaydet: true);

        TempData["Mesaj"] = "Belgeden okunan maliyet arşive kaydedildi.";
        return RedirectToPage("/Maliyet/Index");
    }

    public IActionResult OnPostAktarAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");

        if (firmaId == null)
            return RedirectToPage("/Login");

        UretimAdi = (UretimAdi ?? "").Trim();

        if (string.IsNullOrWhiteSpace(UretimAdi))
            UretimAdi = "Belgeden aktarılan maliyet";

        if (!Kalemler.Any() && !string.IsNullOrWhiteSpace(OkunanMetin))
            Kalemler = KalemleriBul(OkunanMetin);

        if (!Kalemler.Any())
        {
            Hata = "Üretim maliyetine aktarılacak maliyet kalemi bulunamadı.";
            return Page();
        }

        HttpContext.Session.SetObject("MaliyetBelgeAktarim", new MaliyetBelgeAktarim
        {
            UretimAdi = UretimAdi,
            OkunanMetin = OkunanMetin ?? "",
            Kalemler = Kalemler
                .Where(x => x.ToplamTutar > 0)
                .Select(x => new MaliyetBelgeAktarimKalemi
                {
                    Aciklama = x.Aciklama,
                    Tutar = x.ToplamTutar
                })
                .ToList()
        });

        return RedirectToPage("/Maliyet/Uretim");
    }

    private static string MetinCikar(string fileName, byte[] bytes)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        return extension switch
        {
            ".pdf" => PdfMetniCikar(bytes),
            ".docx" => DocxMetniCikar(bytes),
            ".txt" => Encoding.UTF8.GetString(bytes),
            ".csv" => Encoding.UTF8.GetString(bytes),
            _ => ""
        };
    }

    private static string PdfMetniCikar(byte[] bytes)
    {
        var sb = new StringBuilder();

        using var stream = new MemoryStream(bytes);
        using var document = PdfDocument.Open(stream);

        foreach (var page in document.GetPages())
        {
            sb.AppendLine(page.Text);
        }

        return sb.ToString();
    }

    private static string DocxMetniCikar(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var entry = archive.GetEntry("word/document.xml");

        if (entry == null)
            return "";

        using var reader = new StreamReader(entry.Open());
        var xml = XDocument.Parse(reader.ReadToEnd());

        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        var paragraflar = xml
            .Descendants(w + "p")
            .Select(p => string.Concat(p.Descendants(w + "t").Select(t => t.Value)).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x));

        return string.Join(Environment.NewLine, paragraflar);
    }

    private static List<BelgeMaliyetKalemi> KalemleriBul(string metin)
    {
        var kalemler = new List<BelgeMaliyetKalemi>();

        var satirlar = metin
            .Replace("\r", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var satir in satirlar)
        {
            var temizSatir = Regex.Replace(satir, @"\s+", " ").Trim();

            if (temizSatir.Length < 4)
                continue;

            var maliyetSatiriMi =
                temizSatir.Contains("tl", StringComparison.OrdinalIgnoreCase) ||
                temizSatir.Contains("₺", StringComparison.OrdinalIgnoreCase) ||
                temizSatir.Contains("toplam", StringComparison.OrdinalIgnoreCase) ||
                temizSatir.Contains("tutar", StringComparison.OrdinalIgnoreCase) ||
                temizSatir.Contains("fiyat", StringComparison.OrdinalIgnoreCase) ||
                temizSatir.Contains("kdv", StringComparison.OrdinalIgnoreCase);

            if (!maliyetSatiriMi)
                continue;

            var tutarlar = Regex.Matches(
                temizSatir,
                @"(?<!\d)(\d{1,3}(?:[.,]\d{3})*(?:[.,]\d{2})|\d+(?:[.,]\d{2}))\s*(?:TL|₺)?",
                RegexOptions.IgnoreCase);

            if (tutarlar.Count == 0)
                continue;

            var sonTutar = ParaOku(tutarlar[^1].Groups[1].Value);

            if (sonTutar <= 0)
                continue;

            var aciklama = Regex
                .Replace(temizSatir, @"\d{1,3}(?:[.,]\d{3})*(?:[.,]\d{2})|\d+(?:[.,]\d{2})|TL|₺", "", RegexOptions.IgnoreCase)
                .Trim(' ', '-', ':', '|');

            if (string.IsNullOrWhiteSpace(aciklama))
                aciklama = "Belgeden okunan kalem";

            kalemler.Add(new BelgeMaliyetKalemi
            {
                Aciklama = aciklama,
                ToplamTutar = sonTutar
            });
        }

        return kalemler
            .GroupBy(x => new { x.Aciklama, x.ToplamTutar })
            .Select(x => x.First())
            .Take(50)
            .ToList();
    }

    private static decimal ParaOku(string value)
    {
        value = value
            .Replace("TL", "", StringComparison.OrdinalIgnoreCase)
            .Replace("₺", "")
            .Trim();

        var tr = new CultureInfo("tr-TR");

        if (decimal.TryParse(value, NumberStyles.Any, tr, out var trSonuc))
            return trSonuc;

        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var invSonuc))
            return invSonuc;

        return 0;
    }
}

public class BelgeMaliyetKalemi
{
    public string Aciklama { get; set; } = "";
    public decimal ToplamTutar { get; set; }
}
