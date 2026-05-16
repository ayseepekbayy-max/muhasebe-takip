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
            lower.Contains("detay ver") ||
            lower.Contains("detay göster") ||
            lower.Contains("hareketleri göster") ||
            lower.Contains("listele")
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

            var detayTurkceAy = detayAyBaslangic.ToString(
                "MMMM",
                new CultureInfo("tr-TR"));

            var detayTip = HttpContext.Session.GetString(SonDetayTipKey);

            if (detayTip == "MaasToplam" && bulunanCalisan == null)
            {
                var text = $"{detayTurkceAy} dönemi tüm çalışan maaş detayları:\n\n";
                var kayitVarMi = false;

                foreach (var c in calisanlar)
                {
                    var hareketler = await MaasHareketleriniGetir(
                        firmaId,
                        c.Id,
                        detayAyBaslangic);

                    if (!hareketler.Any())
                        continue;

                    kayitVarMi = true;

                    text += $"{c.AdSoyad}:\n";

                    foreach (var item in hareketler)
                    {
                        text +=
                            $"- {item.Tarih:dd.MM.yyyy} | " +
                            $"{item.Tutar:N2} TL | " +
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
                        $"- {item.Tarih:dd.MM.yyyy} | " +
                        $"{item.Tutar:N2} TL | " +
                        (item.Tip == CalisanHareketTipi.MaasOdeme ? "Maaş" : "Diğer");

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
                        text +=
                            $"- {item.Tarih:dd.MM.yyyy} | " +
                            $"{item.Tutar:N2} TL";

                        if (!string.IsNullOrWhiteSpace(item.Aciklama))
                        {
                            text += $" | {item.Aciklama}";
                        }

                        text += "\n";
                    }

                    text += "\n";
                }

                if (!kayitVarMi)
                {
                    return $"{detayTurkceAy} döneminde avans detayı bulunamadı.";
                }

                return text;
            }

            var sonCalisanAdi = bulunanCalisan?.AdSoyad;

            if (string.IsNullOrWhiteSpace(sonCalisanAdi))
                sonCalisanAdi = HttpContext.Session.GetString(SonDetayCalisanKey);

            if (string.IsNullOrWhiteSpace(sonCalisanAdi))
                sonCalisanAdi = _memory.SonCalisaniGetir();

            if (string.IsNullOrWhiteSpace(sonCalisanAdi))
                return "Detayı gösterilecek çalışan bulunamadı.";

            var calisan = await _db.Calisanlar
                .FirstOrDefaultAsync(x =>
                    x.FirmaId == firmaId &&
                    x.AdSoyad == sonCalisanAdi);

            if (calisan == null)
                return "Çalışan bulunamadı.";

            var calisanHareketler = await AvansHareketleriniGetir(
                firmaId,
                calisan.Id,
                detayAyBaslangic);

            if (!calisanHareketler.Any())
            {
                return $"{calisan.AdSoyad} için {detayTurkceAy} döneminde avans detayı bulunamadı.";
            }

            var calisanText =
                $"{calisan.AdSoyad} {detayTurkceAy} dönemi avans detayları:\n\n";

            foreach (var item in calisanHareketler)
            {
                calisanText +=
                    $"- {item.Tarih:dd.MM.yyyy} | " +
                    $"{item.Tutar:N2} TL";

                if (!string.IsNullOrWhiteSpace(item.Aciklama))
                {
                    calisanText += $" | {item.Aciklama}";
                }

                calisanText += "\n";
            }

            return calisanText;
        }

        // PERSONEL GİDERİ

if (
    lower.Contains("personel gider") ||
    lower.Contains("çalışan gider") ||
    lower.Contains("personel maliyet") ||
    lower.Contains("maaş gider")
)
{
    var toplam = await _db.CalisanAvanslari
        .Where(x =>
            x.FirmaId == firmaId &&
            x.Tip == CalisanHareketTipi.MaasOdeme &&
            !x.ArsivlendiMi &&
            x.Tarih >= ayBaslangic &&
            x.Tarih < ayBitis)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    return $"{turkceAy} ayı personel gideriniz: {toplam:N2} TL";
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
                lower.Contains("gelmedi") ||
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

            return
                $"{bulunanCalisan.AdSoyad} puantaj özeti:\n\n" +
                $"- Geldi: {geldi} gün\n" +
                $"- Gelmedi: {gelmedi} gün\n" +
                $"- İzinli: {izinli} gün\n" +
                $"- Yarım gün: {yarim} gün";
        }

        // PUANTAJ GENEL

        if (
            lower.Contains("puantaj") ||
            lower.Contains("gelmedi") ||
            lower.Contains("izin")
        )
        {
            var enDevamsiz = await _db.CalisanPuantajlari
                .Include(x => x.Calisan)
                .Where(x =>
                    x.FirmaId == firmaId &&
                    (
                        x.Durum == PuantajDurum.Gelmedi ||
                        x.Durum == PuantajDurum.Izinli
                    ))
                .GroupBy(x => x.Calisan!.AdSoyad)
                .Select(x => new
                {
                    Ad = x.Key,
                    Sayi = x.Count()
                })
                .OrderByDescending(x => x.Sayi)
                .FirstOrDefaultAsync();

            if (
                lower.Contains("en fazla") ||
                lower.Contains("en çok")
            )
            {
                if (enDevamsiz == null)
                    return "Puantaj kaydı bulunamadı.";

                return $"En fazla devamsızlık yapan çalışan: {enDevamsiz.Ad} ({enDevamsiz.Sayi} gün)";
            }

            return "Puantaj analizi tamamlandı.";
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
                return
                    $"Kasa analiziniz:\n\n" +
                    $"- Toplam giriş: {giris:N2} TL\n" +
                    $"- Toplam çıkış: {cikis:N2} TL\n" +
                    $"- Güncel bakiye: {bakiye:N2} TL";
            }

            return $"Güncel kasa bakiyesi: {bakiye:N2} TL";
        }

        // STOK ANALİZİ

        if (lower.Contains("stok"))
        {
            if (lower.Contains("hareket"))
            {
                var sonHareketler = await _db.StokHareketleri
                    .Where(x => x.FirmaId == firmaId)
                    .OrderByDescending(x => x.Tarih)
                    .Take(5)
                    .ToListAsync();

                if (!sonHareketler.Any())
                    return "Stok hareketi bulunamadı.";

                var text = "Son stok hareketleri:\n\n";

                foreach (var item in sonHareketler)
                {
                    text +=
                        $"- {item.Ad} | {item.Miktar} | {item.Tarih:dd.MM.yyyy}\n";
                }

                return text;
            }

            var stokSayisi = await _db.StokUrunler
                .CountAsync(x => x.FirmaId == firmaId);

            return $"Toplam stok ürün sayınız: {stokSayisi}";
        }

        // MÜŞTERİ PERFORMANS

        if (
            lower.Contains("en çok kazandıran") ||
            lower.Contains("en aktif müşteri") ||
            lower.Contains("müşteri performans")
        )
        {
            var musteri = await _db.MusteriIsler
                .Include(x => x.Musteri)
                .Where(x => x.FirmaId == firmaId)
                .GroupBy(x => x.Musteri!.AdSoyad)
                .Select(x => new
                {
                    Musteri = x.Key,
                    Toplam = x.Sum(y => y.Gelir)
                })
                .OrderByDescending(x => x.Toplam)
                .FirstOrDefaultAsync();

            if (musteri == null)
                return "Müşteri performans verisi bulunamadı.";

            return
                $"En çok kazandıran müşteri: {musteri.Musteri}\n" +
                $"Toplam gelir: {musteri.Toplam:N2} TL";
        }

        // MÜŞTERİ ANALİZİ

        if (
            lower.Contains("müşteri") ||
            lower.Contains("musteri")
        )
        {
            var sayi = await _db.Musteriler
                .CountAsync(x => x.FirmaId == firmaId);

            if (
                lower.Contains("durum") ||
                lower.Contains("analiz")
            )
            {
                return
                    $"Müşteri analiziniz:\n\n" +
                    $"- Toplam müşteri sayısı: {sayi}\n" +
                    "- Müşteri ilişkileri normal görünüyor.";
            }

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

            decimal oran = 0;

            if (gelir > 0)
                oran = (gider / gelir) * 100;

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

        // KAR ZARAR ANALİZİ

        if (
            lower.Contains("kar") ||
            lower.Contains("zarar") ||
            lower.Contains("gelir gider")
        )
        {
            var toplamGelir = await _db.MusteriIsler
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.Tarih >= ayBaslangic &&
                    x.Tarih < ayBitis)
                .SumAsync(x => (decimal?)x.Gelir) ?? 0;

            var toplamMasraf = await _db.MusteriMasraflar
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.Tarih >= ayBaslangic &&
                    x.Tarih < ayBitis)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            var personel = await _db.CalisanAvanslari
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.Tarih >= ayBaslangic &&
                    x.Tarih < ayBitis)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            var net = toplamGelir - toplamMasraf - personel;

            string durum;

            if (net > 0)
                durum = "Şirket bu ay kâr etmiş görünüyor.";
            else
                durum = "Şirket bu ay zarar etmiş görünüyor.";

            return
                $"{turkceAy} ayı finansal özeti:\n\n" +
                $"- Toplam gelir: {toplamGelir:N2} TL\n" +
                $"- Toplam masraf: {toplamMasraf:N2} TL\n" +
                $"- Personel gideri: {personel:N2} TL\n" +
                $"- Net sonuç: {net:N2} TL\n\n" +
                durum;
        }

        if (
            lower.Contains("şirket") ||
            lower.Contains("işletme") ||
            lower.Contains("genel durum") ||
            lower.Contains("performans")
        )
        {
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

            var fark = giris - cikis;

            string yorum;

            if (fark > 0)
                yorum = "Şirket finansal olarak pozitif görünüyor.";
            else
                yorum = "Giderler yüksek görünüyor.";

            return
                $"İşletme analizi:\n\n" +
                $"- Toplam gelir: {giris:N2} TL\n" +
                $"- Toplam gider: {cikis:N2} TL\n" +
                $"- Net durum: {fark:N2} TL\n\n" +
                yorum;
        }

        // ÇALIŞAN SAYISI

        if (
            lower.Contains("çalışan") ||
            lower.Contains("personel")
        )
        {
            var sayi = await _db.Calisanlar
                .CountAsync(x => x.FirmaId == firmaId);

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
}

public class ChatMesaj
{
    public string Gonderen { get; set; } = "";
    public string Metin { get; set; } = "";
}
