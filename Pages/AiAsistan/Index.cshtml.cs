using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Helpers;
using MuhasebeTakip2.App.Services.Ai;

namespace MuhasebeTakip2.App.Pages.AiAsistan;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly NovaReplyService _novaReplyService;
    private readonly ConversationMemoryService _memory;
    
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

        var ayBaslangic = new DateTime(
            ay.Year,
            ay.Month,
            1);

        var ayBitis = ayBaslangic.AddMonths(1);

        var calisanlar = await _db.Calisanlar
            .Where(x => x.FirmaId == firmaId)
            .ToListAsync();

        var bulunanCalisan = calisanlar
    .FirstOrDefault(x =>
        lower.Contains(x.AdSoyad.ToLower()) ||
        lower.Contains(x.Ad.ToLower()));

if (bulunanCalisan != null)
{
    _memory.SonCalisaniKaydet(bulunanCalisan.AdSoyad);
}
else
{
    var sonCalisan = _memory.SonCalisaniGetir();

    if (!string.IsNullOrWhiteSpace(sonCalisan))
    {
        bulunanCalisan = calisanlar
            .FirstOrDefault(x =>
                x.AdSoyad.ToLower() == sonCalisan.ToLower());
    }
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
                    x.Tarih >= ayBaslangic &&
                    x.Tarih < ayBitis)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            return $"{ayBaslangic:MMMM} ayı personel gideriniz: {toplam:N2} TL";
        }

        // ÇALIŞAN AVANS

        if (
            bulunanCalisan != null &&
            lower.Contains("avans")
        )
        {
            var toplam = await _db.CalisanAvanslari
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.CalisanId == bulunanCalisan.Id &&
                    x.Tip == CalisanHareketTipi.Avans &&
                    x.Tarih >= ayBaslangic &&
                    x.Tarih < ayBitis)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            return
                $"{bulunanCalisan.AdSoyad} isimli çalışan " +
                $"{ayBaslangic:MMMM} ayında toplam " +
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
                var veri = await _db.CalisanAvanslari
                    .Where(x =>
                        x.FirmaId == firmaId &&
                        x.Tip == CalisanHareketTipi.Avans)
                    .GroupBy(x => x.Calisan!.AdSoyad)
                    .Select(x => new
                    {
                        Ad = x.Key,
                        Toplam = x.Sum(y => y.Tutar)
                    })
                    .OrderByDescending(x => x.Toplam)
                    .FirstOrDefaultAsync();

                if (veri == null)
                    return "Avans kaydı bulunamadı.";

                return $"En fazla avans alan kişi: {veri.Ad} ({veri.Toplam:N2} TL)";
            }

            var toplamAvans = await _db.CalisanAvanslari
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.Tarih >= ayBaslangic &&
                    x.Tarih < ayBitis &&
                    x.Tip == CalisanHareketTipi.Avans)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            return $"{ayBaslangic:MMMM} ayında toplam {toplamAvans:N2} TL avans verilmiş.";
        }

        // ÇALIŞAN MAAŞ

        if (
            bulunanCalisan != null &&
            lower.Contains("maaş")
        )
        {
            return
                $"{bulunanCalisan.AdSoyad} isimli çalışanın maaşı: " +
                $"{bulunanCalisan.Maas:N2} TL";
        }

        // MAAŞ ANALİZİ

        if (lower.Contains("maaş"))
        {
            if (lower.Contains("ortalama"))
            {
                var ortalama = await _db.Calisanlar
                    .Where(x => x.FirmaId == firmaId)
                    .AverageAsync(x => (decimal?)x.Maas) ?? 0;

                return $"Ortalama maaş: {ortalama:N2} TL";
            }

            if (lower.Contains("en yüksek"))
            {
                var calisan = await _db.Calisanlar
                    .Where(x => x.FirmaId == firmaId)
                    .OrderByDescending(x => x.Maas)
                    .FirstOrDefaultAsync();

                if (calisan == null)
                    return "Çalışan bulunamadı.";

                return $"En yüksek maaşı alan çalışan: {calisan.AdSoyad} ({calisan.Maas:N2} TL)";
            }

            var toplam = await _db.CalisanAvanslari
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.Tarih >= ayBaslangic &&
                    x.Tarih < ayBitis &&
                    x.Tip == CalisanHareketTipi.MaasOdeme)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            return $"{ayBaslangic:MMMM} ayında toplam maaş ödemesi: {toplam:N2} TL";
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

            var gelmedi = puantajlar.Count(x =>
                x.Durum == PuantajDurum.Gelmedi);

            var izinli = puantajlar.Count(x =>
                x.Durum == PuantajDurum.Izinli);

            var yarim = puantajlar.Count(x =>
                x.Durum == PuantajDurum.YarimGun);

            var geldi = puantajlar.Count(x =>
                x.Durum == PuantajDurum.Geldi);

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

    if (
        lower.Contains("giriş")
    )
    {
        return $"Bugünkü kasa girişi: {bugunGiris:N2} TL";
    }

    if (
        lower.Contains("çıkış")
    )
    {
        return $"Bugünkü kasa çıkışı: {bugunCikis:N2} TL";
    }

    return
        $"Bugünkü kasa özeti:\n\n" +
        $"- Giriş: {bugunGiris:N2} TL\n" +
        $"- Çıkış: {bugunCikis:N2} TL\n" +
        $"- Net durum: {bugunNet:N2} TL";
}
        if (
            lower.Contains("son kasa hareket")
        )
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

        // GENEL DURUM

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
                $"{ayBaslangic:MMMM} ayı finansal özeti:\n\n" +
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

    private DateTime AyBul(string text)
    {
        var now = DateTime.UtcNow;

        if (text.Contains("ocak"))
            return new DateTime(now.Year, 1, 1);

        if (text.Contains("şubat"))
            return new DateTime(now.Year, 2, 1);

        if (text.Contains("mart"))
            return new DateTime(now.Year, 3, 1);

        if (text.Contains("nisan"))
            return new DateTime(now.Year, 4, 1);

        if (text.Contains("mayıs"))
            return new DateTime(now.Year, 5, 1);

        if (text.Contains("haziran"))
            return new DateTime(now.Year, 6, 1);

        if (text.Contains("temmuz"))
            return new DateTime(now.Year, 7, 1);

        if (text.Contains("ağustos"))
            return new DateTime(now.Year, 8, 1);

        if (text.Contains("eylül"))
            return new DateTime(now.Year, 9, 1);

        if (text.Contains("ekim"))
            return new DateTime(now.Year, 10, 1);

        if (text.Contains("kasım"))
            return new DateTime(now.Year, 11, 1);

        if (text.Contains("aralık"))
            return new DateTime(now.Year, 12, 1);

        return new DateTime(now.Year, now.Month, 1);
    }
}

public class ChatMesaj
{
    public string Gonderen { get; set; } = "";
    public string Metin { get; set; } = "";
}