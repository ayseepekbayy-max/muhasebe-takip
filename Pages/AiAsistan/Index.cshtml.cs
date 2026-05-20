using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Helpers;
using MuhasebeTakip2.App.Services.Ai;
using System.Globalization;

namespace MuhasebeTakip2.App.Pages.AiAsistan;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly NovaReplyService _novaReplyService;
    private readonly ConversationMemoryService _memory;

    private const string SonDetayCalisanKey = "AiSonDetayCalisan";
    private const string SonDetayAyKey = "AiSonDetayAy";
    private const string SonDetayYilKey = "AiSonDetayYil";
    private const string SonDetayTipKey = "AiSonDetayTip";
    private const string SonDetayAltTipKey = "AiSonDetayAltTip";

    public IndexModel(
        AppDbContext db,
        NovaReplyService novaReplyService,
        ConversationMemoryService memory)
    {
        _db = db;
        _novaReplyService = novaReplyService;
        _memory = memory;
    }

    [BindProperty]
    public string Soru { get; set; } = "";

    public List<ChatMesaj> Mesajlar { get; set; } = new();

    public IActionResult OnGet()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");

        if (firmaId == null)
            return RedirectToPage("/Login");

        Mesajlar = HttpContext.Session.GetObject<List<ChatMesaj>>("AiMesajlar")
                    ?? new List<ChatMesaj>();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        return await MesajIsle();
    }

    public async Task<IActionResult> OnPostAjaxAsync()
    {
        return await MesajIsle();
    }

    private async Task<IActionResult> MesajIsle()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");

        if (firmaId == null)
        {
            return new JsonResult(new
            {
                success = false,
                cevap = "Oturum süresi dolmuş."
            });
        }

        if (string.IsNullOrWhiteSpace(Soru))
        {
            return new JsonResult(new
            {
                success = false,
                cevap = "Lütfen soru yazın."
            });
        }

        Mesajlar = HttpContext.Session.GetObject<List<ChatMesaj>>("AiMesajlar")
                    ?? new List<ChatMesaj>();

        Mesajlar.Add(new ChatMesaj
        {
            Gonderen = "Kullanici",
            Metin = Soru
        });

        string cevap;

        try
        {
            cevap = await AkilliCevapUret(Soru, firmaId.Value);
        }
        catch (Exception ex)
        {
            cevap =
                "Hata oluştu.\n\n" +
                ex.Message +
                "\n\n" +
                ex.InnerException?.Message;
        }

        Mesajlar.Add(new ChatMesaj
        {
            Gonderen = "Ai",
            Metin = cevap
        });

        HttpContext.Session.SetObject("AiMesajlar", Mesajlar);

        return new JsonResult(new
        {
            success = true,
            cevap
        });
    }

    public IActionResult OnPostTemizle()
    {
        HttpContext.Session.Remove("AiMesajlar");
        return RedirectToPage();
    }

    private async Task<string> AkilliCevapUret(string soru, int firmaId)
    {
        var novaReply = _novaReplyService.GetReply(soru);

        if (!string.IsNullOrWhiteSpace(novaReply))
            return novaReply;

        return await CevapUret(soru, firmaId);
    }

    private async Task<string> CevapUret(string soru, int firmaId)
    {
        var lower = soru.ToLowerInvariant();

        var ay = AyBul(lower);

        if (lower.Contains("bu ay"))
        {
            ay = DateTime.Now;
        }

        var ayBaslangic = DateTime.SpecifyKind(
            new DateTime(ay.Year, ay.Month, 1),
            DateTimeKind.Utc);

        var ayBitis = ayBaslangic.AddMonths(1);
        var turkceAy = ayBaslangic.ToString(
            "MMMM",
            new CultureInfo("tr-TR"));

        var calisanlar = await _db.Calisanlar
            .Where(x => x.FirmaId == firmaId)
            .ToListAsync();

        Calisan? bulunanCalisan = null;

        foreach (var c in calisanlar)
        {
            var adSoyad = (c.AdSoyad ?? "")
                .ToLowerInvariant()
                .Trim();

            if (
                !string.IsNullOrWhiteSpace(adSoyad) &&
                (
                    lower.Contains(adSoyad) ||
                    adSoyad.Contains(lower.Replace("ne kadar avans aldı", "").Trim()) ||
                    lower.Contains(adSoyad.Split(' ')[0])
                ))
            {
                bulunanCalisan = c;
                break;
            }
        }

        if (bulunanCalisan != null)
        {
            _memory.SonCalisaniKaydet(bulunanCalisan.AdSoyad);
        }

        // DETAY VER

if (
    !lower.Contains("stok") &&
    !lower.Contains("ürün") &&
    !lower.Contains("urun") &&
    !lower.Contains("kasa") &&
    !lower.Contains("müşteri") &&
    !lower.Contains("musteri") &&
    !lower.Contains("maaş") &&
    !lower.Contains("maas") &&
    !lower.Contains("avans") &&
    !lower.Contains("puantaj") &&
    !lower.Contains("cari") &&
    !lower.Contains("kim") &&
    !lower.Contains("kaç") &&
    !lower.Contains("kac") &&
    !lower.Contains("ne kadar") &&
    (
        lower.Contains("detay ver") ||
        lower.Contains("detay göster") ||
        lower.Contains("hareketleri göster") ||
        lower.Contains("listele")
    )
)
{
    var detayAyBaslangic = ayBaslangic;

    var kayitliAy = HttpContext.Session.GetInt32(SonDetayAyKey);
    var kayitliYil = HttpContext.Session.GetInt32(SonDetayYilKey);

    if (!AyIfadesiVarMi(lower) && kayitliAy != null && kayitliYil != null)
    {
        detayAyBaslangic = DateTime.SpecifyKind(
            new DateTime(kayitliYil.Value, kayitliAy.Value, 1),
            DateTimeKind.Utc);
    }

    var detayAyBitis = detayAyBaslangic.AddMonths(1);
    var detayTurkceAy = detayAyBaslangic.ToString("MMMM", new CultureInfo("tr-TR"));
    var detayTip = HttpContext.Session.GetString(SonDetayTipKey);
    var detayAltTip = HttpContext.Session.GetString(SonDetayAltTipKey);

    if (detayTip == "PersonelGideri")
    {
        var hareketler = await _db.CalisanAvanslari
            .Include(x => x.Calisan)
            .Where(x =>
                x.FirmaId == firmaId &&
                (
                    x.Tip == CalisanHareketTipi.MaasOdeme ||
                    x.Tip == CalisanHareketTipi.Diger
                ) &&
                x.Tarih >= detayAyBaslangic &&
                x.Tarih < detayAyBitis)
            .OrderBy(x => x.Tarih)
            .ThenBy(x => x.Id)
            .ToListAsync();

        if (!hareketler.Any())
            return $"{detayTurkceAy} döneminde personel gideri detayı bulunamadı.";

        var text = $"{detayTurkceAy} dönemi personel gideri detayları:\n\n";

        foreach (var item in hareketler)
        {
            var ad = item.Calisan?.AdSoyad ?? "Çalışan";

            text +=
                $"- {item.Tarih:dd.MM.yyyy} | {ad} | " +
                $"{item.Tutar:N2} TL | " +
                (item.Tip == CalisanHareketTipi.MaasOdeme ? "Maaş" : "Diğer");

            if (!string.IsNullOrWhiteSpace(item.Aciklama))
                text += $" | {item.Aciklama}";

            text += "\n";
        }

        return text;
    }

    if (detayTip == "Kasa")
    {
        var query = _db.KasaHareketleri
            .Where(x =>
                x.FirmaId == firmaId &&
                x.Tarih >= detayAyBaslangic &&
                x.Tarih < detayAyBitis);

        if (detayAltTip == "Giris")
            query = query.Where(x => x.Tip == HareketTipi.Giris);

        if (detayAltTip == "Cikis")
            query = query.Where(x => x.Tip == HareketTipi.Cikis);

        var hareketler = await query
            .OrderByDescending(x => x.Tarih)
            .ToListAsync();

        if (!hareketler.Any())
            return $"{detayTurkceAy} döneminde kasa hareketi bulunamadı.";

        var text = $"{detayTurkceAy} dönemi kasa detayları:\n\n";

        foreach (var item in hareketler)
        {
            text += $"- {item.Tarih:dd.MM.yyyy} | {item.Tip} | {item.Tutar:N2} TL\n";
        }

        return text;
    }

if (detayTip == "Calisanlar")
{
    var liste = await _db.Calisanlar
        .Where(x => x.FirmaId == firmaId)
        .OrderBy(x => x.AdSoyad)
        .ToListAsync();

    if (!liste.Any())
        return "Çalışan bulunamadı.";

    var text = "Çalışan listesi:\n\n";

    foreach (var item in liste)
    {
        text += $"- {item.AdSoyad}";

        if (item.Maas > 0)
            text += $" | Maaş: {item.Maas:N2} TL";

        text += "\n";
    }

    return text;
}

    if (detayTip == "StokUrunler")
{
    var urunler = await _db.StokUrunler
        .Where(x => x.FirmaId == firmaId)
        .OrderBy(x => x.Ad)
        .ToListAsync();

    if (!urunler.Any())
        return "Stok ürünü bulunamadı.";

    var text = "Stok ürünleri:\n\n";

    foreach (var urun in urunler)
    {
        text += $"- {urun.Ad}";

        if (!string.IsNullOrWhiteSpace(urun.Kod))
            text += $" | Kod: {urun.Kod}";

        text += $" | Birim: {urun.Birim}\n";
    }

    return text;
}

    if (detayTip == "Stok")
    {
        var hareketler = await _db.StokHareketleri
            .Include(x => x.StokUrun)
            .Where(x =>
                x.FirmaId == firmaId &&
                x.Tarih >= detayAyBaslangic &&
                x.Tarih < detayAyBitis)
            .OrderByDescending(x => x.Tarih)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        if (!hareketler.Any())
            return $"{detayTurkceAy} döneminde stok hareketi bulunamadı.";

        var text = $"{detayTurkceAy} dönemi stok detayları:\n\n";

        foreach (var item in hareketler)
        {
            var urunAdi = !string.IsNullOrWhiteSpace(item.Ad)
                ? item.Ad
                : item.StokUrun?.Ad ?? "Ürün";

            text +=
                $"- {item.Tarih:dd.MM.yyyy} | {urunAdi} | " +
                $"{item.Tip} | {item.Miktar:N2}";

            if (!string.IsNullOrWhiteSpace(item.Aciklama))
                text += $" | {item.Aciklama}";

            text += "\n";
        }

        return text;
    }
    if (detayTip == "PuantajCalisan")
{
    var sonCalisanAdi = HttpContext.Session.GetString(SonDetayCalisanKey)
        ?? _memory.SonCalisaniGetir();

    if (string.IsNullOrWhiteSpace(sonCalisanAdi))
        return "Detayı gösterilecek çalışan bulunamadı.";

    var calisan = await _db.Calisanlar
        .FirstOrDefaultAsync(x =>
            x.FirmaId == firmaId &&
            x.AdSoyad == sonCalisanAdi);

    if (calisan == null)
        return "Çalışan bulunamadı.";

    var puantajlar = await _db.CalisanPuantajlari
        .Where(x =>
            x.FirmaId == firmaId &&
            x.CalisanId == calisan.Id &&
            x.Tarih >= detayAyBaslangic &&
            x.Tarih < detayAyBitis)
        .OrderBy(x => x.Tarih)
        .ToListAsync();

    if (!puantajlar.Any())
        return $"{calisan.AdSoyad} için {detayTurkceAy} döneminde puantaj detayı bulunamadı.";

    var text = $"{calisan.AdSoyad} {detayTurkceAy} dönemi puantaj detayları:\n\n";

    foreach (var item in puantajlar)
    {
        text += $"- {item.Tarih:dd.MM.yyyy} | {item.Durum}\n";
    }

    return text;
}

if (detayTip == "Puantaj")
{
    var puantajlar = await _db.CalisanPuantajlari
        .Include(x => x.Calisan)
        .Where(x =>
            x.FirmaId == firmaId &&
            x.Tarih >= detayAyBaslangic &&
            x.Tarih < detayAyBitis)
        .OrderBy(x => x.Tarih)
        .ThenBy(x => x.Calisan!.AdSoyad)
        .ToListAsync();

    if (!puantajlar.Any())
        return $"{detayTurkceAy} döneminde puantaj detayı bulunamadı.";

    var text = $"{detayTurkceAy} dönemi genel puantaj detayları:\n\n";

    foreach (var item in puantajlar)
    {
        var ad = item.Calisan?.AdSoyad ?? "Çalışan";
        text += $"- {item.Tarih:dd.MM.yyyy} | {ad} | {item.Durum}\n";
    }

    return text;
}
    if (detayTip == "Musteri")
    {
        var isler = await _db.MusteriIsler
            .Include(x => x.Musteri)
            .Where(x =>
                x.FirmaId == firmaId &&
                x.Tarih >= detayAyBaslangic &&
                x.Tarih < detayAyBitis)
            .OrderByDescending(x => x.Tarih)
            .ToListAsync();

        if (!isler.Any())
            return $"{detayTurkceAy} döneminde müşteri işi bulunamadı.";

        var text = $"{detayTurkceAy} dönemi müşteri iş detayları:\n\n";

        foreach (var item in isler)
        {
            var ad = item.Musteri?.AdSoyad ?? "Müşteri";
            text += $"- {item.Tarih:dd.MM.yyyy} | {ad} | Gelir: {item.Gelir:N2} TL\n";
        }

        return text;
    }

    if (detayTip == "KarZarar" || detayTip == "Yonetim")
    {
        var gelirler = await _db.MusteriIsler
            .Include(x => x.Musteri)
            .Where(x =>
                x.FirmaId == firmaId &&
                x.Tarih >= detayAyBaslangic &&
                x.Tarih < detayAyBitis)
            .OrderByDescending(x => x.Tarih)
            .ToListAsync();

        var masraflar = await _db.MusteriMasraflar
            .Where(x =>
                x.FirmaId == firmaId &&
                x.Tarih >= detayAyBaslangic &&
                x.Tarih < detayAyBitis)
            .OrderByDescending(x => x.Tarih)
            .ToListAsync();

        var personel = await _db.CalisanAvanslari
            .Include(x => x.Calisan)
            .Where(x =>
                x.FirmaId == firmaId &&
                (
                    x.Tip == CalisanHareketTipi.MaasOdeme ||
                    x.Tip == CalisanHareketTipi.Diger
                ) &&
                x.Tarih >= detayAyBaslangic &&
                x.Tarih < detayAyBitis)
            .OrderByDescending(x => x.Tarih)
            .ToListAsync();

        var text = $"{detayTurkceAy} dönemi finans detayları:\n\n";

        text += "Gelirler:\n";
        if (!gelirler.Any())
            text += "- Gelir kaydı yok\n";
        else
        {
            foreach (var item in gelirler)
            {
                var ad = item.Musteri?.AdSoyad ?? "Müşteri";
                text += $"- {item.Tarih:dd.MM.yyyy} | {ad} | {item.Gelir:N2} TL\n";
            }
        }

        text += "\nMüşteri masrafları:\n";
        if (!masraflar.Any())
            text += "- Masraf kaydı yok\n";
        else
        {
            foreach (var item in masraflar)
            {
                text += $"- {item.Tarih:dd.MM.yyyy} | {item.Tutar:N2} TL\n";
            }
        }

        text += "\nPersonel giderleri:\n";
        if (!personel.Any())
            text += "- Personel gideri yok\n";
        else
        {
            foreach (var item in personel)
            {
                var ad = item.Calisan?.AdSoyad ?? "Çalışan";
                text += $"- {item.Tarih:dd.MM.yyyy} | {ad} | {item.Tutar:N2} TL\n";
            }
        }

        return text;
    }

    if (detayTip == "MaasToplam" && bulunanCalisan == null)
    {
        var text = $"{detayTurkceAy} dönemi tüm çalışan maaş detayları:\n\n";
        var kayitVarMi = false;

        foreach (var c in calisanlar)
        {
            var hareketler = await MaasHareketleriniGetir(firmaId, c.Id, detayAyBaslangic);

            if (!hareketler.Any())
                continue;

            kayitVarMi = true;
            text += $"{c.AdSoyad}:\n";

            foreach (var item in hareketler)
            {
                text +=
                    $"- {item.Tarih:dd.MM.yyyy} | {item.Tutar:N2} TL | " +
                    (item.Tip == CalisanHareketTipi.MaasOdeme ? "Maaş" : "Diğer");

                if (!string.IsNullOrWhiteSpace(item.Aciklama))
                    text += $" | {item.Aciklama}";

                text += "\n";
            }

            text += "\n";
        }

        if (!kayitVarMi)
            return $"{detayTurkceAy} döneminde maaş detayı bulunamadı.";

        return text;
    }

    if (detayTip == "MaasCalisan")
    {
        var sonCalisanAdiMaas = bulunanCalisan?.AdSoyad
            ?? HttpContext.Session.GetString(SonDetayCalisanKey)
            ?? _memory.SonCalisaniGetir();

        if (string.IsNullOrWhiteSpace(sonCalisanAdiMaas))
            return "Detayı gösterilecek çalışan bulunamadı.";

        var calisanMaas = await _db.Calisanlar
            .FirstOrDefaultAsync(x =>
                x.FirmaId == firmaId &&
                x.AdSoyad == sonCalisanAdiMaas);

        if (calisanMaas == null)
            return "Çalışan bulunamadı.";

        var hareketler = await MaasHareketleriniGetir(
            firmaId,
            calisanMaas.Id,
            detayAyBaslangic);

        if (!hareketler.Any())
            return $"{calisanMaas.AdSoyad} için {detayTurkceAy} döneminde maaş detayı bulunamadı.";

        var text = $"{calisanMaas.AdSoyad} {detayTurkceAy} dönemi maaş detayları:\n\n";

        foreach (var item in hareketler)
        {
            text +=
                $"- {item.Tarih:dd.MM.yyyy} | {item.Tutar:N2} TL | " +
                (item.Tip == CalisanHareketTipi.MaasOdeme ? "Maaş" : "Diğer");

            if (!string.IsNullOrWhiteSpace(item.Aciklama))
                text += $" | {item.Aciklama}";

            text += "\n";
        }

        return text;
    }

    if (detayTip == "Calisan")
{
    var sonCalisanAdi = bulunanCalisan?.AdSoyad
        ?? HttpContext.Session.GetString(SonDetayCalisanKey)
        ?? _memory.SonCalisaniGetir();

    if (string.IsNullOrWhiteSpace(sonCalisanAdi))
        return "Detayı gösterilecek çalışan bulunamadı.";

    var calisan = await _db.Calisanlar
        .FirstOrDefaultAsync(x =>
            x.FirmaId == firmaId &&
            x.AdSoyad == sonCalisanAdi);

    if (calisan == null)
        return "Çalışan bulunamadı.";

    var hareketler = await AvansHareketleriniGetir(
        firmaId,
        calisan.Id,
        detayAyBaslangic);

    if (!hareketler.Any())
        return $"{calisan.AdSoyad} için {detayTurkceAy} döneminde avans detayı bulunamadı.";

    var text = $"{calisan.AdSoyad} {detayTurkceAy} dönemi avans detayları:\n\n";

    foreach (var item in hareketler)
    {
        text += $"- {item.Tarih:dd.MM.yyyy} | {item.Tutar:N2} TL";

        if (!string.IsNullOrWhiteSpace(item.Aciklama))
            text += $" | {item.Aciklama}";

        text += "\n";
    }

    return text;
}

    if (detayTip == "Toplam" && bulunanCalisan == null)
    {
        var text = $"{detayTurkceAy} dönemi tüm çalışan avans detayları:\n\n";
        var kayitVarMi = false;

        foreach (var c in calisanlar)
        {
            var hareketler = await AvansHareketleriniGetir(
                firmaId,
                c.Id,
                detayAyBaslangic);

            if (!hareketler.Any())
                continue;

            kayitVarMi = true;
            text += $"{c.AdSoyad}:\n";

            foreach (var item in hareketler)
            {
                text += $"- {item.Tarih:dd.MM.yyyy} | {item.Tutar:N2} TL";

                if (!string.IsNullOrWhiteSpace(item.Aciklama))
                    text += $" | {item.Aciklama}";

                text += "\n";
            }

            text += "\n";
        }

        if (!kayitVarMi)
            return $"{detayTurkceAy} döneminde avans detayı bulunamadı.";

        return text;
    }

    return "Detay gösterilecek önceki konu bulunamadı.";
}

        // PERSONEL GİDERİ

if (
    lower.Contains("personel gider") ||
    lower.Contains("personel gideri") ||
    lower.Contains("personel maliyet") ||
    lower.Contains("personel maliyeti") ||
    lower.Contains("çalışan gider") ||
    lower.Contains("çalışan gideri") ||
    lower.Contains("çalışan maliyet") ||
    lower.Contains("çalışan maliyeti") ||
    lower.Contains("toplam çalışan maliyeti") ||
    lower.Contains("maaş gider") ||
    lower.Contains("maaş maliyet")
)
{
    var toplam = await _db.CalisanAvanslari
        .Where(x =>
            x.FirmaId == firmaId &&
            (
                x.Tip == CalisanHareketTipi.MaasOdeme ||
                x.Tip == CalisanHareketTipi.Diger
            ) &&
            x.Tarih >= ayBaslangic &&
            x.Tarih < ayBitis)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    if (toplam <= 0)
        return $"{turkceAy} döneminde personel gideri bulunamadı.";

    SonGenelDetayKaydet("PersonelGideri", ayBaslangic);
    return $"{turkceAy} dönemi toplam personel gideri: {toplam:N2} TL";
}




        // ÇALIŞAN AVANS

        if (
            bulunanCalisan != null &&
            lower.Contains("avans") &&
            !lower.Contains("toplam") &&
            !lower.Contains("analiz") &&
            !lower.Contains("durum") &&
            !lower.Contains("en fazla") &&
            !lower.Contains("en çok")
        )
        {
            var hareketler = await AvansHareketleriniGetir(
                firmaId,
                bulunanCalisan.Id,
                ayBaslangic);

            var toplam = hareketler.Sum(x => x.Tutar);

            SonAvansDetayiniKaydet(bulunanCalisan.AdSoyad, ayBaslangic);

            if (toplam <= 0)
            {
                return
                    $"{bulunanCalisan.AdSoyad} için " +
                    $"{turkceAy} döneminde avans kaydı bulunamadı.";
            }

            return
                $"{bulunanCalisan.AdSoyad} isimli çalışan " +
                $"{turkceAy} döneminde toplam " +
                $"{toplam:N2} TL avans aldı.";
        }

        // AVANS ANALİZİ

        if (lower.Contains("avans"))
        {
            if (
                lower.Contains("en fazla") ||
                lower.Contains("en çok")
            )
            {
                string enAd = "";
                decimal enToplam = 0;

                foreach (var c in calisanlar)
                {
                    var hareketler = await AvansHareketleriniGetir(
                        firmaId,
                        c.Id,
                        ayBaslangic);

                    var toplam = hareketler.Sum(x => x.Tutar);

                    if (toplam > enToplam)
                    {
                        enToplam = toplam;
                        enAd = c.AdSoyad;
                    }
                }

                if (enToplam <= 0)
                {
                    return "Avans kaydı bulunamadı.";
                }

                SonAvansDetayiniKaydet(enAd, ayBaslangic);

                return
                    $"En fazla avans alan kişi: " +
                    $"{enAd} ({enToplam:N2} TL)";
            }

            decimal toplamAvans = 0;

            foreach (var c in calisanlar)
            {
                var hareketler = await AvansHareketleriniGetir(
                    firmaId,
                    c.Id,
                    ayBaslangic);

                toplamAvans += hareketler.Sum(x => x.Tutar);
            }

            SonToplamAvansDetayiniKaydet(ayBaslangic);

            return
                $"{turkceAy} döneminde toplam " +
                $"{toplamAvans:N2} TL avans verilmiş.";
        }

        // ÇALIŞAN MAAŞ

        if (
            bulunanCalisan != null &&
            lower.Contains("maaş") &&
            !lower.Contains("son maaş") &&
            !lower.Contains("son maas") &&
            !lower.Contains("ortalama") &&
            !lower.Contains("toplam") &&
            !lower.Contains("dağılım") &&
            !lower.Contains("dagilim") &&
            !lower.Contains("en yüksek") &&
            !lower.Contains("kim aldı") &&
            !lower.Contains("maaş gider") &&
            !lower.Contains("personel gider")
        )
        {
            var hareketler = await MaasHareketleriniGetir(
                firmaId,
                bulunanCalisan.Id,
                ayBaslangic);

            var toplam = hareketler.Sum(x => x.Tutar);

            SonMaasDetayiniKaydet(bulunanCalisan.AdSoyad, ayBaslangic);

            if (toplam <= 0)
            {
                return $"{bulunanCalisan.AdSoyad} için {turkceAy} döneminde maaş ödemesi bulunamadı.";
            }

            return
                $"{bulunanCalisan.AdSoyad} isimli çalışana " +
                $"{turkceAy} döneminde toplam {toplam:N2} TL maaş ödemesi yapılmış.";
        }

        // MAAŞ ANALİZİ

        if (
    lower.Contains("son maaş") ||
    lower.Contains("son maas") ||
    lower.Contains("son maaş ödemesi") ||
    lower.Contains("son maas odemesi")
)
{
    var sonMaas = await _db.CalisanAvanslari
        .Include(x => x.Calisan)
        .Where(x =>
            x.FirmaId == firmaId &&
            (
                x.Tip == CalisanHareketTipi.MaasOdeme ||
                x.Tip == CalisanHareketTipi.Diger
            ))
        .OrderByDescending(x => x.Tarih)
        .ThenByDescending(x => x.Id)
        .FirstOrDefaultAsync();

    if (sonMaas == null)
        return "Maaş ödemesi bulunamadı.";

    var calisanAdi = sonMaas.Calisan?.AdSoyad ?? "Çalışan";

    return
        $"Son maaş ödemesi:\n\n" +
        $"- Çalışan: {calisanAdi}\n" +
        $"- Tarih: {sonMaas.Tarih:dd.MM.yyyy}\n" +
        $"- Tutar: {sonMaas.Tutar:N2} TL";
}


        if (lower.Contains("maaş"))
        {
            var maasDagilimi = new List<(string Ad, decimal Toplam)>();

            foreach (var c in calisanlar)
            {
                var hareketler = await MaasHareketleriniGetir(
                    firmaId,
                    c.Id,
                    ayBaslangic);

                var toplam = hareketler.Sum(x => x.Tutar);

                if (toplam > 0)
                {
                    maasDagilimi.Add((c.AdSoyad ?? "İsimsiz çalışan", toplam));
                }
            }

            if (lower.Contains("ortalama"))
            {
                if (!maasDagilimi.Any())
                    return $"{turkceAy} döneminde maaş ödemesi bulunamadı.";

                var ortalamaOdeme = maasDagilimi.Average(x => x.Toplam);

                SonToplamMaasDetayiniKaydet(ayBaslangic);

                return $"{turkceAy} döneminde ortalama maaş ödemesi: {ortalamaOdeme:N2} TL";
            }


            if (
                lower.Contains("en yüksek") ||
                lower.Contains("kim aldı")
            )
            {
                var enYuksekOdeme = maasDagilimi
                    .OrderByDescending(x => x.Toplam)
                    .FirstOrDefault();

                if (enYuksekOdeme.Toplam > 0)
                {
                    SonMaasDetayiniKaydet(enYuksekOdeme.Ad, ayBaslangic);

                    return $"En yüksek maaş ödemesi alan çalışan: {enYuksekOdeme.Ad} ({enYuksekOdeme.Toplam:N2} TL)";
                }

                var enYuksekKayitliMaas = await _db.Calisanlar
                    .Where(x => x.FirmaId == firmaId && x.Maas > 0)
                    .OrderByDescending(x => x.Maas)
                    .FirstOrDefaultAsync();

                if (enYuksekKayitliMaas == null)
                    return $"{turkceAy} döneminde maaş ödemesi bulunamadı.";

                return
                    $"Bu dönemde maaş ödeme kaydı bulunamadı.\n" +
                    $"Kayıtlı maaşı en yüksek çalışan: {enYuksekKayitliMaas.AdSoyad} ({enYuksekKayitliMaas.Maas:N2} TL)";
            }

            if (
                lower.Contains("dağılım") ||
                lower.Contains("dagilim") ||
                lower.Contains("listele")
            )
            {
                if (maasDagilimi.Any())
                {
                    SonToplamMaasDetayiniKaydet(ayBaslangic);

                    var text = $"{turkceAy} dönemi maaş ödeme dağılımı:\n\n";

                    foreach (var item in maasDagilimi.OrderByDescending(x => x.Toplam))
                    {
                        text += $"- {item.Ad}: {item.Toplam:N2} TL\n";
                    }

                    return text;
                }

                var kayitliMaaslar = await _db.Calisanlar
                    .Where(x => x.FirmaId == firmaId && x.Maas > 0)
                    .OrderByDescending(x => x.Maas)
                    .ToListAsync();

                if (!kayitliMaaslar.Any())
                    return $"{turkceAy} döneminde maaş ödemesi bulunamadı.";

                var textKayitli = "Bu dönemde maaş ödeme kaydı bulunamadı.\nKayıtlı maaş dağılımı:\n\n";

                foreach (var item in kayitliMaaslar)
                {
                    textKayitli += $"- {item.AdSoyad}: {item.Maas:N2} TL\n";
                }

                return textKayitli;
            }

            if (
                lower.Contains("verdim mi") ||
                lower.Contains("ödedim mi")
            )
            {
                var toplam = maasDagilimi.Sum(x => x.Toplam);

                SonToplamMaasDetayiniKaydet(ayBaslangic);

                if (toplam <= 0)
                    return $"{turkceAy} döneminde maaş ödemesi bulunamadı.";

                return $"{turkceAy} döneminde toplam {toplam:N2} TL maaş ödemesi yapılmış.";
            }

            if (
                lower.Contains("toplam") ||
                lower.Contains("ne kadar")
            )
            {
                var toplam = maasDagilimi.Sum(x => x.Toplam);

                SonToplamMaasDetayiniKaydet(ayBaslangic);

                if (toplam <= 0)
                    return $"{turkceAy} döneminde maaş ödemesi bulunamadı.";

                return $"{turkceAy} döneminde toplam maaş ödemesi: {toplam:N2} TL";
            }
        }

        // ÇALIŞAN PUANTAJ

if (
    bulunanCalisan != null &&
    (
        lower.Contains("puantaj") ||
        lower.Contains("puan") ||
        lower.Contains("gelmedi") ||
        lower.Contains("gelmeyen") ||
        lower.Contains("devamsız") ||
        lower.Contains("devamsizlik") ||
        lower.Contains("izin")
    )
)
{
    var puantajlar = await _db.CalisanPuantajlari
        .Where(x =>
            x.FirmaId == firmaId &&
            x.CalisanId == bulunanCalisan.Id &&
            x.Tarih >= ayBaslangic &&
            x.Tarih < ayBitis)
        .ToListAsync();

    var gelmedi = puantajlar.Count(x => x.Durum == PuantajDurum.Gelmedi);
    var izinli = puantajlar.Count(x => x.Durum == PuantajDurum.Izinli);
    var yarim = puantajlar.Count(x => x.Durum == PuantajDurum.YarimGun);
    var geldi = puantajlar.Count(x => x.Durum == PuantajDurum.Geldi);

    var beklenenGun = Enumerable
    .Range(0, ayBitis.Subtract(ayBaslangic).Days)
    .Select(i => ayBaslangic.AddDays(i))
    .Count(x =>
        x.DayOfWeek != DayOfWeek.Sunday);

var kayitliGun = puantajlar
    .Select(x => x.Tarih.Date)
    .Distinct()
    .Count();

var eksikGun = beklenenGun - kayitliGun;
SonPuantajDetayiniKaydet(bulunanCalisan.AdSoyad, ayBaslangic);

if (eksikGun < 0)
    eksikGun = 0;


    if (lower.Contains("kaç gün geldi") || lower.Contains("kac gun geldi"))
        return $"{bulunanCalisan.AdSoyad} {turkceAy} döneminde {geldi} gün geldi.";

    if (lower.Contains("kaç gün gelmedi") || lower.Contains("kac gun gelmedi") || lower.Contains("gelmedi"))
        return $"{bulunanCalisan.AdSoyad} {turkceAy} döneminde {gelmedi} gün gelmedi.";

    if (lower.Contains("izin"))
        return $"{bulunanCalisan.AdSoyad} {turkceAy} döneminde {izinli} gün izinli görünüyor.";

    if (lower.Contains("devamsız") || lower.Contains("devamsizlik"))
    {
        var toplamDevamsizlik = gelmedi + izinli;

        return
            $"{bulunanCalisan.AdSoyad} {turkceAy} devamsızlık durumu:\n\n" +
            $"- Gelmedi: {gelmedi} gün\n" +
            $"- İzinli: {izinli} gün\n" +
            $"- Toplam devamsızlık: {toplamDevamsizlik} gün";
    }

   return
    $"{bulunanCalisan.AdSoyad} {turkceAy} puantaj özeti:\n\n" +
    $"- Geldi: {geldi} gün\n" +
    $"- Gelmedi: {gelmedi} gün\n" +
    $"- İzinli: {izinli} gün\n" +
    $"- Yarım gün: {yarim} gün";


}

// PUANTAJ GENEL

if (
    lower.Contains("puantaj") ||
    lower.Contains("puan") ||
    lower.Contains("gelmedi") ||
    lower.Contains("gelmeyen") ||
    lower.Contains("devamsız") ||
    lower.Contains("devamsizlik") ||
    lower.Contains("izin")
)
{
    var puantajlar = await _db.CalisanPuantajlari
        .Include(x => x.Calisan)
        .Where(x =>
            x.FirmaId == firmaId &&
            x.Tarih >= ayBaslangic &&
            x.Tarih < ayBitis)
        .ToListAsync();

    if (!puantajlar.Any())
        return $"{turkceAy} döneminde puantaj kaydı bulunamadı.";

    SonGenelDetayKaydet("Puantaj", ayBaslangic);

    if (
        lower.Contains("en fazla izin") ||
        lower.Contains("en çok izin") ||
        lower.Contains("en cok izin") ||
        lower.Contains("kim en fazla izin") ||
        lower.Contains("kim en çok izin") ||
        lower.Contains("kim en cok izin")
    )
    {
        var enIzinli = puantajlar
            .Where(x => x.Durum == PuantajDurum.Izinli)
            .GroupBy(x => x.Calisan!.AdSoyad)
            .Select(x => new
            {
                Ad = x.Key,
                Sayi = x.Count()
            })
            .OrderByDescending(x => x.Sayi)
            .FirstOrDefault();

        if (enIzinli == null)
            return $"{turkceAy} döneminde izin kaydı bulunamadı.";

        SonPuantajDetayiniKaydet(enIzinli.Ad, ayBaslangic);

        return $"{turkceAy} döneminde en fazla izin alan çalışan: {enIzinli.Ad} ({enIzinli.Sayi} gün)";
    }

    if (
    lower.Contains("kim en çok gelmedi") ||
    lower.Contains("kim en cok gelmedi") ||
    lower.Contains("en çok gelmedi") ||
    lower.Contains("en cok gelmedi") ||
    lower.Contains("kim en çok gelmeyen") ||
    lower.Contains("kim en cok gelmeyen") ||
    lower.Contains("en çok gelmeyen") ||
    lower.Contains("en cok gelmeyen")
)
    {
        var enCokGelmeyen = puantajlar
            .Where(x => x.Durum == PuantajDurum.Gelmedi)
            .GroupBy(x => x.Calisan!.AdSoyad)
            .Select(x => new
            {
                Ad = x.Key,
                Sayi = x.Count()
            })
            .OrderByDescending(x => x.Sayi)
            .FirstOrDefault();

        if (enCokGelmeyen == null)
            return $"{turkceAy} döneminde gelmedi kaydı bulunamadı.";

        SonPuantajDetayiniKaydet(enCokGelmeyen.Ad, ayBaslangic);

        return $"{turkceAy} döneminde en çok gelmeyen çalışan: {enCokGelmeyen.Ad} ({enCokGelmeyen.Sayi} gün)";
    }

    if (
        lower.Contains("en fazla devamsız") ||
        lower.Contains("en fazla devamsizlik") ||
        lower.Contains("devamsızlık yapan") ||
        lower.Contains("devamsizlik yapan")
    )
    {
        var enDevamsiz = puantajlar
            .Where(x =>
                x.Durum == PuantajDurum.Gelmedi ||
                x.Durum == PuantajDurum.Izinli)
            .GroupBy(x => x.Calisan!.AdSoyad)
            .Select(x => new
            {
                Ad = x.Key,
                Sayi = x.Count()
            })
            .OrderByDescending(x => x.Sayi)
            .FirstOrDefault();

        if (enDevamsiz == null)
            return $"{turkceAy} döneminde devamsızlık kaydı bulunamadı.";

        SonPuantajDetayiniKaydet(enDevamsiz.Ad, ayBaslangic);

        return $"{turkceAy} döneminde en fazla devamsızlık yapan çalışan: {enDevamsiz.Ad} ({enDevamsiz.Sayi} gün)";
    }

    var toplamGelmedi = puantajlar.Count(x => x.Durum == PuantajDurum.Gelmedi);
    var toplamIzinli = puantajlar.Count(x => x.Durum == PuantajDurum.Izinli);
    var toplamYarim = puantajlar.Count(x => x.Durum == PuantajDurum.YarimGun);
    var toplamGeldi = puantajlar.Count(x => x.Durum == PuantajDurum.Geldi);

    return
        $"{turkceAy} dönemi genel puantaj özeti:\n\n" +
        $"- Geldi: {toplamGeldi} gün\n" +
        $"- Gelmedi: {toplamGelmedi} gün\n" +
        $"- İzinli: {toplamIzinli} gün\n" +
        $"- Yarım gün: {toplamYarim} gün";
}
        // KASA ANALİZİ

        if (
            lower.Contains("bugünkü kasa") ||
            lower.Contains("bugün kasa")
        )
        {
            var bugun = DateTime.UtcNow.Date;
            var yarin = bugun.AddDays(1);

            var bugunGiris = await _db.KasaHareketleri
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.Tarih >= bugun &&
                    x.Tarih < yarin &&
                    x.Tip == HareketTipi.Giris)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            var bugunCikis = await _db.KasaHareketleri
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.Tarih >= bugun &&
                    x.Tarih < yarin &&
                    x.Tip == HareketTipi.Cikis)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            var bugunNet = bugunGiris - bugunCikis;

            if (lower.Contains("giriş"))
            {
                return $"Bugünkü kasa girişi: {bugunGiris:N2} TL";
            }

            if (lower.Contains("çıkış"))
            {
                return $"Bugünkü kasa çıkışı: {bugunCikis:N2} TL";
            }

            return
                $"Bugünkü kasa özeti:\n\n" +
                $"- Giriş: {bugunGiris:N2} TL\n" +
                $"- Çıkış: {bugunCikis:N2} TL\n" +
                $"- Net durum: {bugunNet:N2} TL";
        }

        if (lower.Contains("son kasa hareket"))
        {
            var hareketler = await _db.KasaHareketleri
                .Where(x => x.FirmaId == firmaId)
                .OrderByDescending(x => x.Tarih)
                .Take(5)
                .ToListAsync();

            if (!hareketler.Any())
                return "Kasa hareketi bulunamadı.";

            var text = "Son kasa hareketleri:\n\n";

            foreach (var item in hareketler)
            {
                text +=
                    $"- {item.Tarih:dd.MM.yyyy} | " +
                    $"{item.Tip} | " +
                    $"{item.Tutar:N2} TL\n";
            }

            return text;
        }

        if (
            lower.Contains("kasa") ||
            lower.Contains("nakit")
        )
        {
        if (
        lower.Contains("giriş") ||
        lower.Contains("giris")
    )
    {
        var girisToplam = await _db.KasaHareketleri
            .Where(x =>
                x.FirmaId == firmaId &&
                x.Tip == HareketTipi.Giris &&
                x.Tarih >= ayBaslangic &&
                x.Tarih < ayBitis)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        SonGenelDetayKaydet("Kasa", ayBaslangic, "Giris");
        return $"{turkceAy} dönemi toplam kasa girişi: {girisToplam:N2} TL";
    }

    if (
        lower.Contains("çıkış") ||
        lower.Contains("cikis") ||
        lower.Contains("çikis") ||
        lower.Contains("cıkış")
    )
    {
        var cikisToplam = await _db.KasaHareketleri
            .Where(x =>
                x.FirmaId == firmaId &&
                x.Tip == HareketTipi.Cikis &&
                x.Tarih >= ayBaslangic &&
                x.Tarih < ayBitis)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        SonGenelDetayKaydet("Kasa", ayBaslangic, "Cikis");
        return $"{turkceAy} dönemi toplam kasa çıkışı: {cikisToplam:N2} TL";
    }

            var giris = await _db.KasaHareketleri
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.Tip == HareketTipi.Giris)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            var cikis = await _db.KasaHareketleri
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.Tip == HareketTipi.Cikis)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            var bakiye = giris - cikis;

            if (
                lower.Contains("arttı") ||
                lower.Contains("azaldı")
            )
            {
                var bugun = DateTime.UtcNow.Date;
                var yarin = bugun.AddDays(1);

                var dun = bugun.AddDays(-1);
                var dunBitis = bugun;

                var bugunGiris = await _db.KasaHareketleri
                    .Where(x =>
                        x.FirmaId == firmaId &&
                        x.Tarih >= bugun &&
                        x.Tarih < yarin &&
                        x.Tip == HareketTipi.Giris)
                    .SumAsync(x => (decimal?)x.Tutar) ?? 0;

                var bugunCikis = await _db.KasaHareketleri
                    .Where(x =>
                        x.FirmaId == firmaId &&
                        x.Tarih >= bugun &&
                        x.Tarih < yarin &&
                        x.Tip == HareketTipi.Cikis)
                    .SumAsync(x => (decimal?)x.Tutar) ?? 0;

                var dunGiris = await _db.KasaHareketleri
                    .Where(x =>
                        x.FirmaId == firmaId &&
                        x.Tarih >= dun &&
                        x.Tarih < dunBitis &&
                        x.Tip == HareketTipi.Giris)
                    .SumAsync(x => (decimal?)x.Tutar) ?? 0;

                var dunCikis = await _db.KasaHareketleri
                    .Where(x =>
                        x.FirmaId == firmaId &&
                        x.Tarih >= dun &&
                        x.Tarih < dunBitis &&
                        x.Tip == HareketTipi.Cikis)
                    .SumAsync(x => (decimal?)x.Tutar) ?? 0;

                var bugunNet = bugunGiris - bugunCikis;
                var dunNet = dunGiris - dunCikis;

                if (bugunNet > dunNet)
                {
                    return
                        $"Kasa düne göre artmış görünüyor.\n\n" +
                        $"Bugün net: {bugunNet:N2} TL\n" +
                        $"Dün net: {dunNet:N2} TL";
                }

                if (bugunNet < dunNet)
                {
                    return
                        $"Kasa düne göre azalmış görünüyor.\n\n" +
                        $"Bugün net: {bugunNet:N2} TL\n" +
                        $"Dün net: {dunNet:N2} TL";
                }

                return "Kasa durumu düne göre aynı seviyede görünüyor.";
            }

            if (lower.Contains("analiz"))
            {
                SonGenelDetayKaydet("Kasa", ayBaslangic);

                return
                    $"Kasa analiziniz:\n\n" +
                    $"- Toplam giriş: {giris:N2} TL\n" +
                    $"- Toplam çıkış: {cikis:N2} TL\n" +
                    $"- Güncel bakiye: {bakiye:N2} TL";
            }
            SonGenelDetayKaydet("Kasa", ayBaslangic);
            return $"Güncel kasa bakiyesi: {bakiye:N2} TL";
        }

        // STOK ANALİZİ

if (
    lower.Contains("stok") ||
    lower.Contains("ürün") ||
    lower.Contains("urun")
)
{
    if (
        lower.Contains("kaç stok") ||
        lower.Contains("kac stok") ||
        lower.Contains("kaç ürün") ||
        lower.Contains("kac urun") ||
        lower.Contains("toplam stok") ||
        lower.Contains("toplam ürün") ||
        lower.Contains("toplam urun")
    )
    {
        var stokSayisi = await _db.StokUrunler
            .CountAsync(x => x.FirmaId == firmaId);

       SonGenelDetayKaydet("StokUrunler", ayBaslangic);
        return $"Toplam stok ürün sayınız: {stokSayisi}";
    }

    if (
        lower.Contains("stok girişi") ||
        lower.Contains("stok girisi") ||
        lower.Contains("ürün girişi") ||
        lower.Contains("urun girisi") ||
        lower.Contains("giren stok") ||
        lower.Contains("giren ürün") ||
        lower.Contains("giren urun")
    )
    {
        var toplamGiris = await _db.StokHareketleri
            .Where(x =>
                x.FirmaId == firmaId &&
                x.Tip == StokHareketTipi.Giris &&
                x.Tarih >= ayBaslangic &&
                x.Tarih < ayBitis)
            .SumAsync(x => (decimal?)x.Miktar) ?? 0;

        SonGenelDetayKaydet("Stok", ayBaslangic);
        return $"{turkceAy} döneminde toplam stok girişi: {toplamGiris:N2}";
    }

    if (
        lower.Contains("stok çıkışı") ||
        lower.Contains("stok cikisi") ||
        lower.Contains("stok çıkısı") ||
        lower.Contains("ürün çıkışı") ||
        lower.Contains("urun cikisi") ||
        lower.Contains("çıkan stok") ||
        lower.Contains("cikan stok") ||
        lower.Contains("çıkan ürün") ||
        lower.Contains("cikan urun")
    )
    {
        var toplamCikis = await _db.StokHareketleri
            .Where(x =>
                x.FirmaId == firmaId &&
                x.Tip == StokHareketTipi.Cikis &&
                x.Tarih >= ayBaslangic &&
                x.Tarih < ayBitis)
            .SumAsync(x => (decimal?)x.Miktar) ?? 0;

        SonGenelDetayKaydet("Stok", ayBaslangic);
        return $"{turkceAy} döneminde toplam stok çıkışı: {toplamCikis:N2}";
    }

    if (
        lower.Contains("son stok") ||
        lower.Contains("stok hareket") ||
        lower.Contains("hareketleri göster") ||
        lower.Contains("stok hareketlerini göster")
    )
    {
        var hareketler = await _db.StokHareketleri
            .Include(x => x.StokUrun)
            .Where(x =>
                x.FirmaId == firmaId &&
                x.Tarih >= ayBaslangic &&
                x.Tarih < ayBitis)
            .OrderByDescending(x => x.Tarih)
            .ThenByDescending(x => x.Id)
            .Take(10)
            .ToListAsync();

        if (!hareketler.Any())
            return $"{turkceAy} döneminde stok hareketi bulunamadı.";

        var text = $"{turkceAy} dönemi stok hareketleri:\n\n";

        foreach (var item in hareketler)
        {
            var urunAdi = !string.IsNullOrWhiteSpace(item.Ad)
                ? item.Ad
                : item.StokUrun?.Ad ?? "Ürün";

            text +=
                $"- {item.Tarih:dd.MM.yyyy} | " +
                $"{urunAdi} | " +
                $"{item.Tip} | " +
                $"{item.Miktar:N2}";

            if (!string.IsNullOrWhiteSpace(item.Aciklama))
                text += $" | {item.Aciklama}";

            text += "\n";
        }
        SonGenelDetayKaydet("Stok", ayBaslangic);
        return text;
    }

    if (
        lower.Contains("en çok hareket") ||
        lower.Contains("en cok hareket") ||
        lower.Contains("en hareketli")
    )
    {
        var hareketliUrun = await _db.StokHareketleri
            .Include(x => x.StokUrun)
            .Where(x =>
                x.FirmaId == firmaId &&
                x.Tarih >= ayBaslangic &&
                x.Tarih < ayBitis)
            .GroupBy(x => x.StokUrun != null ? x.StokUrun.Ad : x.Ad)
            .Select(x => new
            {
                Urun = x.Key,
                ToplamHareket = x.Sum(y => y.Miktar)
            })
            .OrderByDescending(x => x.ToplamHareket)
            .FirstOrDefaultAsync();

        if (hareketliUrun == null)
            return $"{turkceAy} döneminde stok hareketi bulunamadı.";

        SonGenelDetayKaydet("Stok", ayBaslangic);
        return
            $"{turkceAy} döneminde en çok hareket gören stok: {hareketliUrun.Urun}\n" +
            $"Toplam hareket miktarı: {hareketliUrun.ToplamHareket:N2}";
    }

    if (
        lower.Contains("stok durum") ||
        lower.Contains("stok özeti") ||
        lower.Contains("stok ozeti") ||
        lower.Contains("stokta ne var") ||
        lower.Contains("hangi ürün") ||
        lower.Contains("hangi urun")
    )
    {
        var urunler = await _db.StokUrunler
            .Where(x => x.FirmaId == firmaId)
            .OrderBy(x => x.Ad)
            .ToListAsync();

        if (!urunler.Any())
            return "Stok ürünü bulunamadı.";

        var text = "Stok durumu:\n\n";

        foreach (var urun in urunler)
        {
            var giris = await _db.StokHareketleri
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.StokUrunId == urun.Id &&
                    x.Tip == StokHareketTipi.Giris)
                .SumAsync(x => (decimal?)x.Miktar) ?? 0;

            var cikis = await _db.StokHareketleri
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.StokUrunId == urun.Id &&
                    x.Tip == StokHareketTipi.Cikis)
                .SumAsync(x => (decimal?)x.Miktar) ?? 0;

            var kalan = giris - cikis;

            text += $"- {urun.Ad}: {kalan:N2} {urun.Birim}\n";
        }
        SonGenelDetayKaydet("Stok", ayBaslangic);
        return text;
    }

    var toplamUrun = await _db.StokUrunler
        .CountAsync(x => x.FirmaId == firmaId);

    SonGenelDetayKaydet("StokUrunler", ayBaslangic);
    return $"Toplam stok ürün sayınız: {toplamUrun}";
}


        // MÜŞTERİ ANALİZİ VE PERFORMANS

if (
    lower.Contains("müşteri") ||
    lower.Contains("musteri")
)
{
    if (
    lower.Contains("kaç müşteriyle iş yaptım") ||
    lower.Contains("kac musteriyle is yaptim") ||
    lower.Contains("kaç müşteri ile iş yaptım") ||
    lower.Contains("kac musteri ile is yaptim") ||
    lower.Contains("bu ay kaç müşteriyle") ||
    lower.Contains("bu ay kac musteriyle") ||
    lower.Contains("bu ay kaç müşteri ile") ||
    lower.Contains("bu ay kac musteri ile")
)
{
    var isYapilanMusteriSayisi = await _db.MusteriIsler
        .Where(x =>
            x.FirmaId == firmaId &&
            x.Tarih >= ayBaslangic &&
            x.Tarih < ayBitis)
        .Select(x => x.MusteriId)
        .Distinct()
        .CountAsync();

    SonGenelDetayKaydet("Musteri", ayBaslangic);
    return $"{turkceAy} döneminde {isYapilanMusteriSayisi} müşteriyle iş yapılmış.";
}

    if (
        lower.Contains("kaç müşteri") ||
        lower.Contains("kac musteri") ||
        lower.Contains("kaç müşterim") ||
        lower.Contains("kac musterim") ||
        lower.Contains("toplam müşteri") ||
        lower.Contains("toplam musteri")
    )
    {
        var musteriAdlari = await _db.Musteriler
            .Where(x => x.FirmaId == firmaId)
            .Select(x => x.AdSoyad)
            .ToListAsync();

        var cariMusteriAdlari = await _db.CariKartlar
            .Where(x =>
                x.FirmaId == firmaId &&
                x.Tip == CariTip.Alici)
            .Select(x => x.Ad)
            .ToListAsync();

        var toplamMusteri = musteriAdlari
            .Concat(cariMusteriAdlari)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct()
            .Count();

        SonGenelDetayKaydet("Musteri", ayBaslangic);
        return $"Toplam müşteri sayınız: {toplamMusteri}";
    }

    if (
        lower.Contains("en çok kazandıran") ||
        lower.Contains("en cok kazandiran") ||
        lower.Contains("en fazla kazandıran") ||
        lower.Contains("en fazla kazandiran")
    )
    {
        var musteri = await _db.MusteriIsler
            .Include(x => x.Musteri)
            .Where(x =>
                x.FirmaId == firmaId &&
                x.Tarih >= ayBaslangic &&
                x.Tarih < ayBitis)
            .GroupBy(x => x.Musteri!.AdSoyad)
            .Select(x => new
            {
                Musteri = x.Key,
                Toplam = x.Sum(y => y.Gelir)
            })
            .OrderByDescending(x => x.Toplam)
            .FirstOrDefaultAsync();

        if (musteri == null)
            return $"{turkceAy} döneminde müşteri gelir verisi bulunamadı.";

        SonGenelDetayKaydet("Musteri", ayBaslangic);

        return
            $"{turkceAy} döneminde en çok kazandıran müşteri: {musteri.Musteri}\n" +
            $"Toplam gelir: {musteri.Toplam:N2} TL";

    }

    if (
        lower.Contains("müşteri gelir") ||
        lower.Contains("musteri gelir") ||
        lower.Contains("müşteri kazanç") ||
        lower.Contains("musteri kazanc")
    )
    {
        var toplamGelir = await _db.MusteriIsler
            .Where(x =>
                x.FirmaId == firmaId &&
                x.Tarih >= ayBaslangic &&
                x.Tarih < ayBitis)
            .SumAsync(x => (decimal?)x.Gelir) ?? 0;

        SonGenelDetayKaydet("Musteri", ayBaslangic);
        return $"{turkceAy} dönemi müşteri gelirleri: {toplamGelir:N2} TL";
    }

    if (
        lower.Contains("müşteri durum") ||
        lower.Contains("musteri durum") ||
        lower.Contains("müşteri performans") ||
        lower.Contains("musteri performans")
    )
    {
        var musteriAdlari = await _db.Musteriler
            .Where(x => x.FirmaId == firmaId)
            .Select(x => x.AdSoyad)
            .ToListAsync();

        var cariMusteriAdlari = await _db.CariKartlar
            .Where(x =>
                x.FirmaId == firmaId &&
                x.Tip == CariTip.Alici)
            .Select(x => x.Ad)
            .ToListAsync();

        var toplamMusteri = musteriAdlari
            .Concat(cariMusteriAdlari)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct()
            .Count();

        var cariAliciSayisi = await _db.CariKartlar
            .CountAsync(x =>
                x.FirmaId == firmaId &&
                x.Tip == CariTip.Alici);

        var isYapilanMusteriSayisi = await _db.MusteriIsler
            .Where(x =>
                x.FirmaId == firmaId &&
                x.Tarih >= ayBaslangic &&
                x.Tarih < ayBitis)
            .Select(x => x.MusteriId)
            .Distinct()
            .CountAsync();

        var toplamGelir = await _db.MusteriIsler
            .Where(x =>
                x.FirmaId == firmaId &&
                x.Tarih >= ayBaslangic &&
                x.Tarih < ayBitis)
            .SumAsync(x => (decimal?)x.Gelir) ?? 0;

        SonGenelDetayKaydet("Musteri", ayBaslangic);

        return
            $"{turkceAy} dönemi müşteri durumu:\n\n" +
            $"- Toplam müşteri sayısı: {toplamMusteri}\n" +
            $"- Cari karttaki alıcı sayısı: {cariAliciSayisi}\n" +
            $"- Bu dönem iş yapılan müşteri: {isYapilanMusteriSayisi}\n" +
            $"- Bu dönem müşteri geliri: {toplamGelir:N2} TL";
    }

    if (
        lower.Contains("borçlu müşteri") ||
        lower.Contains("borclu musteri") ||
        lower.Contains("borçlu müşteriler") ||
        lower.Contains("borclu musteriler") ||
        lower.Contains("müşteri borç") ||
        lower.Contains("musteri borc")
    )
    {
        return
    "Şu anda borçlu müşteri listesi gösterilemiyor.\n" +
    "Cari kartlarda müşteri adı ve tipi tutuluyor, fakat borç/alacak tutarı tutulmadığı için kimin borçlu olduğunu hesaplayamıyorum.";

    }

    var musteriAdlariGenel = await _db.Musteriler
        .Where(x => x.FirmaId == firmaId)
        .Select(x => x.AdSoyad)
        .ToListAsync();

    var cariMusteriAdlariGenel = await _db.CariKartlar
        .Where(x =>
            x.FirmaId == firmaId &&
            x.Tip == CariTip.Alici)
        .Select(x => x.Ad)
        .ToListAsync();

    var sayi = musteriAdlariGenel
        .Concat(cariMusteriAdlariGenel)
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x.Trim().ToLowerInvariant())
        .Distinct()
        .Count();

    SonGenelDetayKaydet("Musteri", ayBaslangic);
    return $"Toplam müşteri sayınız: {sayi}";
}


        // AYLIK KIYAS

        if (
            lower.Contains("geçen aya göre") ||
            lower.Contains("arttı mı") ||
            lower.Contains("azaldı mı")
        )
        {
            var gecenAyBaslangic = ayBaslangic.AddMonths(-1);
            var gecenAyBitis = ayBaslangic;

            var buAyGelir = await _db.MusteriIsler
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.Tarih >= ayBaslangic &&
                    x.Tarih < ayBitis)
                .SumAsync(x => (decimal?)x.Gelir) ?? 0;

            var gecenAyGelir = await _db.MusteriIsler
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.Tarih >= gecenAyBaslangic &&
                    x.Tarih < gecenAyBitis)
                .SumAsync(x => (decimal?)x.Gelir) ?? 0;

            if (buAyGelir > gecenAyGelir)
            {
                return
                    "Gelir geçen aya göre artmış görünüyor.\n\n" +
                    $"Bu ay: {buAyGelir:N2} TL\n" +
                    $"Geçen ay: {gecenAyGelir:N2} TL";
            }

            return
                "Gelir geçen aya göre düşmüş görünüyor.\n\n" +
                $"Bu ay: {buAyGelir:N2} TL\n" +
                $"Geçen ay: {gecenAyGelir:N2} TL";
        }

        // RİSK ANALİZİ

        if (
            lower.Contains("risk") ||
            lower.Contains("harcamalar normal") ||
            lower.Contains("nakit akışı")
        )
        {
            var gelir = await _db.MusteriIsler
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.Tarih >= ayBaslangic &&
                    x.Tarih < ayBitis)
                .SumAsync(x => (decimal?)x.Gelir) ?? 0;

            var gider = await _db.KasaHareketleri
    .Where(x =>
        x.FirmaId == firmaId &&
        x.Tip == HareketTipi.Cikis &&
        x.Tarih >= ayBaslangic &&
        x.Tarih < ayBitis)
    .SumAsync(x => (decimal?)x.Tutar) ?? 0;

if (gelir <= 0)
{
    return
        $"Risk analizi:\n\n" +
        $"- Bu dönemde gelir kaydı yok.\n" +
        $"- Gider toplamı: {gider:N2} TL\n" +
        "- Gelir olmadığı için gider oranı hesaplanamıyor.\n" +
        "- Nakit akışı dikkat gerektiriyor.";
}

var oran = (gider / gelir) * 100;

if (oran >= 80)
            {
                return
                    $"Risk analizi:\n\n" +
                    $"- Gider oranı yüksek (%{oran:N0})\n" +
                    "- Nakit akışı riskli görünüyor.";
            }

            return
                $"Risk analizi:\n\n" +
                $"- Gider oranı normal (%{oran:N0})\n" +
                "- Finansal durum stabil görünüyor.";
        }

        // KAR ZARAR VE İŞLETME ANALİZİ

if (
    lower.Contains("kar ettim") ||
    lower.Contains("kar ediyor") ||
    lower.Contains("kar mı") ||
    lower.Contains("kar zarar") ||
    lower.Contains("kâr") ||
    lower.Contains("zarar") ||
    lower.Contains("gelir gider") ||
    lower.Contains("gelir-gider") ||
    lower.Contains("finansal özet") ||
    lower.Contains("finansal ozet") ||
    lower.Contains("şirketin durumu") ||
    lower.Contains("sirketin durumu") ||
    lower.Contains("şirket durumu") ||
    lower.Contains("sirket durumu") ||
    lower.Contains("işletme analizi") ||
    lower.Contains("isletme analizi") ||
    lower.Contains("işletme yorumu") ||
    lower.Contains("isletme yorumu") ||
    lower.Contains("akıllı işletme") ||
    lower.Contains("akilli isletme") ||
    lower.Contains("performans")
)
{
    var toplamGelir = await _db.MusteriIsler
        .Where(x =>
            x.FirmaId == firmaId &&
            x.Tarih >= ayBaslangic &&
            x.Tarih < ayBitis)
        .SumAsync(x => (decimal?)x.Gelir) ?? 0;

    var musteriMasraf = await _db.MusteriMasraflar
        .Where(x =>
            x.FirmaId == firmaId &&
            x.Tarih >= ayBaslangic &&
            x.Tarih < ayBitis)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var personelGideri = await _db.CalisanAvanslari
        .Where(x =>
            x.FirmaId == firmaId &&
            (
                x.Tip == CalisanHareketTipi.MaasOdeme ||
                x.Tip == CalisanHareketTipi.Diger
            ) &&
            x.Tarih >= ayBaslangic &&
            x.Tarih < ayBitis)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var toplamGider = musteriMasraf + personelGideri;
    var net = toplamGelir - toplamGider;

    string durum;
    string yorum;

    if (net > 0)
    {
        durum = "Kâr";
        yorum = "İşletme bu dönemde kârda görünüyor. Gelirler giderleri karşılamış.";
    }
    else if (net < 0)
    {
        durum = "Zarar";
        yorum = "İşletme bu dönemde zararda görünüyor. Giderler gelirlerden yüksek.";
    }
    else
    {
        durum = "Başabaş";
        yorum = "İşletme bu dönemde başabaş seviyede görünüyor.";
    }

    if (
        lower.Contains("kar ettim mi") ||
        lower.Contains("kâr ettim mi") ||
        lower.Contains("kar mı") ||
        lower.Contains("kâr mı")
    )
    {
        SonGenelDetayKaydet("KarZarar", ayBaslangic);

        if (net > 0)
            return $"{turkceAy} döneminde kâr etmiş görünüyorsunuz. Net kâr: {net:N2} TL";

        if (net < 0)
            return $"{turkceAy} döneminde kâr edilmemiş. Net zarar: {Math.Abs(net):N2} TL";

        return $"{turkceAy} döneminde kâr veya zarar görünmüyor. Sonuç başabaş.";
    }

    if (
        lower.Contains("zarar ettim mi") ||
        lower.Contains("zarar mı")
    )
    {
        SonGenelDetayKaydet("KarZarar", ayBaslangic);

        if (net < 0)
            return $"{turkceAy} döneminde zarar etmiş görünüyorsunuz. Net zarar: {Math.Abs(net):N2} TL";

        if (net > 0)
            return $"{turkceAy} döneminde zarar edilmemiş. Net kâr: {net:N2} TL";

        return $"{turkceAy} döneminde zarar görünmüyor. Sonuç başabaş.";
    }

    SonGenelDetayKaydet("KarZarar", ayBaslangic);

    return
        $"{turkceAy} dönemi gelir-gider analizi:\n\n" +
        $"- Toplam gelir: {toplamGelir:N2} TL\n" +
        $"- Müşteri masrafları: {musteriMasraf:N2} TL\n" +
        $"- Personel gideri: {personelGideri:N2} TL\n" +
        $"- Toplam gider: {toplamGider:N2} TL\n" +
        $"- Net sonuç: {net:N2} TL\n" +
        $"- Durum: {durum}\n\n" +
        yorum;
}
        // GENEL YÖNETİM ÖZETİ

if (
    lower.Contains("genel yönetim") ||
    lower.Contains("genel yonetim") ||
    lower.Contains("yönetim özeti") ||
    lower.Contains("yonetim ozeti") ||
    lower.Contains("şirket özeti") ||
    lower.Contains("sirket ozeti") ||
    lower.Contains("işletme özeti") ||
    lower.Contains("isletme ozeti") ||
    lower.Contains("genel rapor") ||
    lower.Contains("bu ay özet") ||
    lower.Contains("bu ay ozet") ||
    lower.Contains("dikkat etmem gereken")
)
{
    var toplamGelir = await _db.MusteriIsler
        .Where(x =>
            x.FirmaId == firmaId &&
            x.Tarih >= ayBaslangic &&
            x.Tarih < ayBitis)
        .SumAsync(x => (decimal?)x.Gelir) ?? 0;

    var musteriMasraf = await _db.MusteriMasraflar
        .Where(x =>
            x.FirmaId == firmaId &&
            x.Tarih >= ayBaslangic &&
            x.Tarih < ayBitis)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var personelGideri = await _db.CalisanAvanslari
        .Where(x =>
            x.FirmaId == firmaId &&
            (
                x.Tip == CalisanHareketTipi.MaasOdeme ||
                x.Tip == CalisanHareketTipi.Diger
            ) &&
            x.Tarih >= ayBaslangic &&
            x.Tarih < ayBitis)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var toplamGider = musteriMasraf + personelGideri;
    var net = toplamGelir - toplamGider;

    var kasaGiris = await _db.KasaHareketleri
        .Where(x =>
            x.FirmaId == firmaId &&
            x.Tip == HareketTipi.Giris)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var kasaCikis = await _db.KasaHareketleri
        .Where(x =>
            x.FirmaId == firmaId &&
            x.Tip == HareketTipi.Cikis)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var kasaBakiye = kasaGiris - kasaCikis;

    var toplamMusteri = await _db.Musteriler
        .CountAsync(x => x.FirmaId == firmaId);

    var cariAlici = await _db.CariKartlar
        .CountAsync(x =>
            x.FirmaId == firmaId &&
            x.Tip == CariTip.Alici);

    var cariSatici = await _db.CariKartlar
        .CountAsync(x =>
            x.FirmaId == firmaId &&
            x.Tip == CariTip.Satici);

    var calisanSayisi = await _db.Calisanlar
        .CountAsync(x => x.FirmaId == firmaId);

    var stokUrunSayisi = await _db.StokUrunler
        .CountAsync(x => x.FirmaId == firmaId);

    string finansYorumu;

    if (net > 0)
        finansYorumu = "Bu dönem işletme kârda görünüyor.";
    else if (net < 0)
        finansYorumu = "Bu dönem giderler gelirlerden yüksek görünüyor.";
    else
        finansYorumu = "Bu dönem işletme başabaş seviyede görünüyor.";
        
    SonGenelDetayKaydet("Yonetim", ayBaslangic);
    return
        $"{turkceAy} dönemi yönetim özeti:\n\n" +
        $"- Toplam gelir: {toplamGelir:N2} TL\n" +
        $"- Toplam gider: {toplamGider:N2} TL\n" +
        $"- Net sonuç: {net:N2} TL\n" +
        $"- Güncel kasa bakiyesi: {kasaBakiye:N2} TL\n" +
        $"- Personel gideri: {personelGideri:N2} TL\n" +
        $"- Müşteri sayısı: {toplamMusteri}\n" +
        $"- Cari alıcı sayısı: {cariAlici}\n" +
        $"- Cari satıcı sayısı: {cariSatici}\n" +
        $"- Çalışan sayısı: {calisanSayisi}\n" +
        $"- Stok ürün sayısı: {stokUrunSayisi}\n\n" +
        finansYorumu;
}


        // ÇALIŞAN SAYISI

        if (
            lower.Contains("çalışan") ||
            lower.Contains("personel")
        )
        {
            var sayi = await _db.Calisanlar
                .CountAsync(x => x.FirmaId == firmaId);

            SonGenelDetayKaydet("Calisanlar", ayBaslangic);

            return $"Toplam çalışan sayınız: {sayi}";
        }
        // CARİ ANALİZ

        if (
            lower.Contains("kaç alıcı") ||
            lower.Contains("kaç satıcı") ||
            lower.Contains("cari dağılım") ||
            lower.Contains("tedarikçi")
        )
        {
            var alici = await _db.CariKartlar
                .CountAsync(x =>
                    x.FirmaId == firmaId &&
                    x.Tip == CariTip.Alici);

            var satici = await _db.CariKartlar
                .CountAsync(x =>
                    x.FirmaId == firmaId &&
                    x.Tip == CariTip.Satici);

            return
                $"Cari analiz:\n\n" +
                $"- Alıcı sayısı: {alici}\n" +
                $"- Satıcı sayısı: {satici}\n" +
                $"- Toplam cari: {alici + satici}";
        }

        // CARİ

        if (lower.Contains("cari"))
        {
            var sayi = await _db.CariKartlar
                .CountAsync(x => x.FirmaId == firmaId);

            return $"Toplam cari kart sayınız: {sayi}";
        }

        return "Soruyu anladım ancak henüz buna cevap verecek sistem eklenmedi.";
    }

    private async Task<List<CalisanAvans>> AvansHareketleriniGetir(
        int firmaId,
        int calisanId,
        DateTime ayBaslangic)
    {
        var simdi = DateTime.Now;

        var buAyMi =
            ayBaslangic.Month == simdi.Month &&
            ayBaslangic.Year == simdi.Year;

        if (buAyMi)
        {
            return await _db.CalisanAvanslari
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.CalisanId == calisanId &&
                    x.Tip == CalisanHareketTipi.Avans &&
                    !x.ArsivlendiMi)
                .OrderBy(x => x.Tarih)
                .ThenBy(x => x.Id)
                .ToListAsync();
        }

        var kultur = new CultureInfo("tr-TR");
        var ayAdi = ayBaslangic.ToString("MMMM", kultur).ToLower(kultur);

        var arsivler = await _db.CalisanMaasArsivleri
            .Where(x =>
                x.FirmaId == firmaId &&
                x.CalisanId == calisanId)
            .OrderByDescending(x => x.OdemeTarihi)
            .ToListAsync();

        foreach (var arsiv in arsivler)
        {
            var detaylar = await _db.CalisanAvanslari
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.CalisanId == calisanId &&
                    x.ArsivlendiMi &&
                    x.Tarih >= arsiv.DonemBaslangic &&
                    x.Tarih <= arsiv.DonemBitis)
                .OrderBy(x => x.Tarih)
                .ThenBy(x => x.Id)
                .ToListAsync();

            var buAyArsiviMi =
                AyMetniIceriyor(arsiv.Aciklama, ayAdi) ||
                detaylar.Any(x =>
                    x.Tip == CalisanHareketTipi.MaasOdeme &&
                    AyMetniIceriyor(x.Aciklama, ayAdi));

            if (buAyArsiviMi)
            {
                return detaylar
                    .Where(x => x.Tip == CalisanHareketTipi.Avans)
                    .OrderBy(x => x.Tarih)
                    .ThenBy(x => x.Id)
                    .ToList();
            }
        }

        return new List<CalisanAvans>();
    }

    private async Task<List<CalisanAvans>> MaasHareketleriniGetir(
    int firmaId,
    int calisanId,
    DateTime ayBaslangic)
{
    var ayBitis = ayBaslangic.AddMonths(1);

    return await _db.CalisanAvanslari
        .Where(x =>
            x.FirmaId == firmaId &&
            x.CalisanId == calisanId &&
            (
                x.Tip == CalisanHareketTipi.MaasOdeme ||
                x.Tip == CalisanHareketTipi.Diger
            ) &&
            x.Tarih >= ayBaslangic &&
            x.Tarih < ayBitis)
        .OrderBy(x => x.Tarih)
        .ThenBy(x => x.Id)
        .ToListAsync();
}

    private bool AyMetniIceriyor(string? metin, string ayAdi)
    {
        if (string.IsNullOrWhiteSpace(metin))
            return false;

        var kultur = new CultureInfo("tr-TR");
        return metin.ToLower(kultur).Contains(ayAdi);
    }

    private DateTime AyBul(string text)
    {
        var now = DateTime.UtcNow;

        if (text.Contains("ocak"))
            return DateTime.SpecifyKind(new DateTime(now.Year, 1, 1), DateTimeKind.Utc);

        if (text.Contains("şubat"))
            return DateTime.SpecifyKind(new DateTime(now.Year, 2, 1), DateTimeKind.Utc);

        if (text.Contains("mart"))
            return DateTime.SpecifyKind(new DateTime(now.Year, 3, 1), DateTimeKind.Utc);

        if (text.Contains("nisan"))
            return DateTime.SpecifyKind(new DateTime(now.Year, 4, 1), DateTimeKind.Utc);

        if (text.Contains("mayıs"))
            return DateTime.SpecifyKind(new DateTime(now.Year, 5, 1), DateTimeKind.Utc);

        if (text.Contains("haziran"))
            return DateTime.SpecifyKind(new DateTime(now.Year, 6, 1), DateTimeKind.Utc);

        if (text.Contains("temmuz"))
            return DateTime.SpecifyKind(new DateTime(now.Year, 7, 1), DateTimeKind.Utc);

        if (text.Contains("ağustos"))
            return DateTime.SpecifyKind(new DateTime(now.Year, 8, 1), DateTimeKind.Utc);

        if (text.Contains("eylül"))
            return DateTime.SpecifyKind(new DateTime(now.Year, 9, 1), DateTimeKind.Utc);

        if (text.Contains("ekim"))
            return DateTime.SpecifyKind(new DateTime(now.Year, 10, 1), DateTimeKind.Utc);

        if (text.Contains("kasım"))
            return DateTime.SpecifyKind(new DateTime(now.Year, 11, 1), DateTimeKind.Utc);

        if (text.Contains("aralık"))
            return DateTime.SpecifyKind(new DateTime(now.Year, 12, 1), DateTimeKind.Utc);

        return DateTime.SpecifyKind(
            new DateTime(now.Year, now.Month, 1),
            DateTimeKind.Utc);
    }

    private bool AyIfadesiVarMi(string text)
    {
        return
            text.Contains("bu ay") ||
            text.Contains("ocak") ||
            text.Contains("şubat") ||
            text.Contains("mart") ||
            text.Contains("nisan") ||
            text.Contains("mayıs") ||
            text.Contains("haziran") ||
            text.Contains("temmuz") ||
            text.Contains("ağustos") ||
            text.Contains("eylül") ||
            text.Contains("ekim") ||
            text.Contains("kasım") ||
            text.Contains("aralık");
    }

    private void SonAvansDetayiniKaydet(string adSoyad, DateTime ayBaslangic)
    {
        HttpContext.Session.SetString(SonDetayTipKey, "Calisan");
        HttpContext.Session.SetString(SonDetayCalisanKey, adSoyad);
        HttpContext.Session.SetInt32(SonDetayAyKey, ayBaslangic.Month);
        HttpContext.Session.SetInt32(SonDetayYilKey, ayBaslangic.Year);
    }

    private void SonToplamAvansDetayiniKaydet(DateTime ayBaslangic)
    {
        HttpContext.Session.SetString(SonDetayTipKey, "Toplam");
        HttpContext.Session.Remove(SonDetayCalisanKey);
        HttpContext.Session.SetInt32(SonDetayAyKey, ayBaslangic.Month);
        HttpContext.Session.SetInt32(SonDetayYilKey, ayBaslangic.Year);
    }

    private void SonMaasDetayiniKaydet(string adSoyad, DateTime ayBaslangic)
    {
        HttpContext.Session.SetString(SonDetayTipKey, "MaasCalisan");
        HttpContext.Session.SetString(SonDetayCalisanKey, adSoyad);
        HttpContext.Session.SetInt32(SonDetayAyKey, ayBaslangic.Month);
        HttpContext.Session.SetInt32(SonDetayYilKey, ayBaslangic.Year);
    }

    private void SonToplamMaasDetayiniKaydet(DateTime ayBaslangic)
    {
        HttpContext.Session.SetString(SonDetayTipKey, "MaasToplam");
        HttpContext.Session.Remove(SonDetayCalisanKey);
        HttpContext.Session.SetInt32(SonDetayAyKey, ayBaslangic.Month);
        HttpContext.Session.SetInt32(SonDetayYilKey, ayBaslangic.Year);
    }
    private void SonPuantajDetayiniKaydet(string adSoyad, DateTime ayBaslangic)
    {
        HttpContext.Session.SetString(SonDetayTipKey, "PuantajCalisan");
        HttpContext.Session.SetString(SonDetayCalisanKey, adSoyad);
        HttpContext.Session.SetInt32(SonDetayAyKey, ayBaslangic.Month);
        HttpContext.Session.SetInt32(SonDetayYilKey, ayBaslangic.Year);
    }
    private void SonGenelDetayKaydet(string tip, DateTime ayBaslangic, string? altTip = null)
    {
        HttpContext.Session.SetString(SonDetayTipKey, tip);
        HttpContext.Session.Remove(SonDetayCalisanKey);
        HttpContext.Session.SetInt32(SonDetayAyKey, ayBaslangic.Month);
        HttpContext.Session.SetInt32(SonDetayYilKey, ayBaslangic.Year);

        if (string.IsNullOrWhiteSpace(altTip))
            HttpContext.Session.Remove(SonDetayAltTipKey);
        else
            HttpContext.Session.SetString(SonDetayAltTipKey, altTip);
    }

}

public class ChatMesaj
{
    public string Gonderen { get; set; } = "";
    public string Metin { get; set; } = "";
}
