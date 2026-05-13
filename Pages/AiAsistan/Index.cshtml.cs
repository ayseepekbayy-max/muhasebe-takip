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
    private readonly QueryInterpreter _interpreter;
    private readonly QueryExecutor _executor;
    private readonly NovaReplyService _novaReplyService;

    public IndexModel(
        AppDbContext db,
        QueryInterpreter interpreter,
        QueryExecutor executor,
        NovaReplyService novaReplyService)
    {
        _db = db;
        _interpreter = interpreter;
        _executor = executor;
        _novaReplyService = novaReplyService;
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
        var firmaId = HttpContext.Session.GetInt32("FirmaId");

        if (firmaId == null)
            return RedirectToPage("/Login");

        Mesajlar = HttpContext.Session.GetObject<List<ChatMesaj>>("AiMesajlar")
                    ?? new List<ChatMesaj>();

        if (string.IsNullOrWhiteSpace(Soru))
            return Page();

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
                "HATA DETAYI:\n\n" +
                ex.Message + "\n\n" +
                ex.StackTrace + "\n\n" +
                ex.InnerException?.Message;
        }

        Mesajlar.Add(new ChatMesaj
        {
            Gonderen = "Ai",
            Metin = cevap
        });

        HttpContext.Session.SetObject("AiMesajlar", Mesajlar);

        Soru = "";

        return Page();
    }

    public async Task<IActionResult> OnPostAjaxAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");

        if (firmaId == null)
        {
            return new JsonResult(new
            {
                success = false,
                cevap = "Oturum süresi dolmuş. Lütfen tekrar giriş yapın."
            });
        }

        if (string.IsNullOrWhiteSpace(Soru))
        {
            return new JsonResult(new
            {
                success = false,
                cevap = "Lütfen bir soru yazın."
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
            cevap = $"İşlem sırasında hata oluştu.\n\nHata: {ex.Message}";
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
            soru = Soru,
            cevap = cevap
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

        var sonuc = _interpreter.Interpret(soru);
        sonuc.FirmaId = firmaId;

        var cevap = await _executor.ExecuteAsync(sonuc);

        if (!string.IsNullOrWhiteSpace(cevap) &&
            cevap != "Bu sorgu tipi henüz desteklenmiyor." &&
            cevap != "Sorunuzu anlayamadım.")
        {
            return cevap;
        }

        return await CevapUret(soru, firmaId);
    }

    private async Task<string> CevapUret(string soru, int firmaId)
    {
        var lower = soru.ToLowerInvariant();

        var bugun = DateTime.UtcNow.Date;
        var yarin = bugun.AddDays(1);
        var ayBaslangic = new DateTime(bugun.Year, bugun.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var gelecekAy = ayBaslangic.AddMonths(1);
        var gecenAyBaslangic = ayBaslangic.AddMonths(-1);
        var gecenAyBitis = ayBaslangic;

        if (lower.Contains("çalışan") || lower.Contains("personel"))
        {
            if (lower.Contains("liste") || lower.Contains("göster"))
            {
                var liste = await _db.Calisanlar
                    .Where(x => x.FirmaId == firmaId)
                    .OrderBy(x => x.AdSoyad)
                    .Select(x => x.AdSoyad)
                    .ToListAsync();

                if (!liste.Any())
                    return "Bu firmaya ait çalışan bulunamadı.";

                return "Çalışan listesi:\n\n" + string.Join("\n", liste.Select(x => "- " + x));
            }

            var sayi = await _db.Calisanlar.CountAsync(x => x.FirmaId == firmaId);
            return $"Toplam çalışan sayınız: {sayi}";
        }

        if (lower.Contains("müşteri") || lower.Contains("musteri"))
        {
            var sayi = await _db.Musteriler.CountAsync(x => x.FirmaId == firmaId);
            return $"Toplam müşteri sayınız: {sayi}";
        }

        if (lower.Contains("cari"))
        {
            var sayi = await _db.CariKartlar.CountAsync(x => x.FirmaId == firmaId);
            return $"Toplam cari kart sayınız: {sayi}";
        }

        if (lower.Contains("alıcı") || lower.Contains("alici"))
        {
            var sayi = await _db.CariKartlar
                .CountAsync(x => x.FirmaId == firmaId && x.Tip == CariTip.Alici);

            return $"Toplam alıcı sayınız: {sayi}";
        }

        if (lower.Contains("satıcı") || lower.Contains("satici"))
        {
            var sayi = await _db.CariKartlar
                .CountAsync(x => x.FirmaId == firmaId && x.Tip == CariTip.Satici);

            return $"Toplam satıcı sayınız: {sayi}";
        }

        if (lower.Contains("stok"))
        {
            var sayi = await _db.StokUrunler.CountAsync(x => x.FirmaId == firmaId);
            return $"Toplam stok ürün sayınız: {sayi}";
        }

        if (lower.Contains("son") && lower.Contains("kasa"))
        {
            var liste = await _db.KasaHareketleri
                .Where(x => x.FirmaId == firmaId)
                .OrderByDescending(x => x.Tarih)
                .ThenByDescending(x => x.Id)
                .Take(10)
                .ToListAsync();

            if (!liste.Any())
                return "Kasa hareketi bulunamadı.";

            var cevap = "Son 10 kasa hareketi:\n\n";

            foreach (var item in liste)
            {
                var tip = item.Tip == HareketTipi.Giris ? "Giriş" : "Çıkış";
                cevap += $"- {item.Tarih:dd.MM.yyyy} | {tip} | {item.Tutar:N2} TL | {item.Aciklama}\n";
            }

            return cevap;
        }

        if (lower.Contains("bugün") && (lower.Contains("giriş") || lower.Contains("gelir") || lower.Contains("girdi")))
        {
            var toplam = await _db.KasaHareketleri
                .Where(x => x.FirmaId == firmaId &&
                            x.Tarih >= bugun &&
                            x.Tarih < yarin &&
                            x.Tip == HareketTipi.Giris)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            return $"Bugünkü toplam kasa girişiniz: {toplam:N2} TL";
        }

        if (lower.Contains("bugün") && (lower.Contains("çıkış") || lower.Contains("gider") || lower.Contains("çıktı")))
        {
            var toplam = await _db.KasaHareketleri
                .Where(x => x.FirmaId == firmaId &&
                            x.Tarih >= bugun &&
                            x.Tarih < yarin &&
                            x.Tip == HareketTipi.Cikis)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            return $"Bugünkü toplam kasa çıkışınız: {toplam:N2} TL";
        }

        if (lower.Contains("kasa") && (lower.Contains("bakiye") || lower.Contains("durum") || lower.Contains("ne kadar")))
        {
            var giris = await _db.KasaHareketleri
                .Where(x => x.FirmaId == firmaId && x.Tip == HareketTipi.Giris)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            var cikis = await _db.KasaHareketleri
                .Where(x => x.FirmaId == firmaId && x.Tip == HareketTipi.Cikis)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            var bakiye = giris - cikis;

            return $"Güncel kasa bakiyeniz: {bakiye:N2} TL";
        }

        if (lower.Contains("bu ay") && (lower.Contains("gelir") || lower.Contains("giriş")))
        {
            var toplam = await _db.KasaHareketleri
                .Where(x => x.FirmaId == firmaId &&
                            x.Tarih >= ayBaslangic &&
                            x.Tarih < gelecekAy &&
                            x.Tip == HareketTipi.Giris)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            return $"Bu ay toplam geliriniz: {toplam:N2} TL";
        }

        if (lower.Contains("bu ay") && (lower.Contains("gider") || lower.Contains("çıkış") || lower.Contains("masraf")))
        {
            var toplam = await _db.KasaHareketleri
                .Where(x => x.FirmaId == firmaId &&
                            x.Tarih >= ayBaslangic &&
                            x.Tarih < gelecekAy &&
                            x.Tip == HareketTipi.Cikis)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            return $"Bu ay toplam gideriniz: {toplam:N2} TL";
        }

        if (lower.Contains("kâr") || lower.Contains("kar"))
        {
            var gelir = await _db.KasaHareketleri
                .Where(x => x.FirmaId == firmaId &&
                            x.Tarih >= ayBaslangic &&
                            x.Tarih < gelecekAy &&
                            x.Tip == HareketTipi.Giris)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            var gider = await _db.KasaHareketleri
                .Where(x => x.FirmaId == firmaId &&
                            x.Tarih >= ayBaslangic &&
                            x.Tarih < gelecekAy &&
                            x.Tip == HareketTipi.Cikis)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            var kar = gelir - gider;

            if (kar > 0)
                return $"Bu ay kâr etmiş görünüyorsunuz.\nGelir: {gelir:N2} TL\nGider: {gider:N2} TL\nKâr: {kar:N2} TL";

            if (kar < 0)
                return $"Bu ay zarar etmiş görünüyorsunuz.\nGelir: {gelir:N2} TL\nGider: {gider:N2} TL\nZarar: {Math.Abs(kar):N2} TL";

            return $"Bu ay gelir ve gider eşit görünüyor.\nGelir: {gelir:N2} TL\nGider: {gider:N2} TL";
        }

        if (lower.Contains("geçen aya göre") || lower.Contains("önceki aya göre"))
        {
            var buAyGelir = await _db.KasaHareketleri
                .Where(x => x.FirmaId == firmaId &&
                            x.Tarih >= ayBaslangic &&
                            x.Tarih < gelecekAy &&
                            x.Tip == HareketTipi.Giris)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            var buAyGider = await _db.KasaHareketleri
                .Where(x => x.FirmaId == firmaId &&
                            x.Tarih >= ayBaslangic &&
                            x.Tarih < gelecekAy &&
                            x.Tip == HareketTipi.Cikis)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            var gecenAyGelir = await _db.KasaHareketleri
                .Where(x => x.FirmaId == firmaId &&
                            x.Tarih >= gecenAyBaslangic &&
                            x.Tarih < gecenAyBitis &&
                            x.Tip == HareketTipi.Giris)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            var gecenAyGider = await _db.KasaHareketleri
                .Where(x => x.FirmaId == firmaId &&
                            x.Tarih >= gecenAyBaslangic &&
                            x.Tarih < gecenAyBitis &&
                            x.Tip == HareketTipi.Cikis)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            return
                $"Geçen aya göre durum:\n\n" +
                $"Bu ay gelir: {buAyGelir:N2} TL\n" +
                $"Bu ay gider: {buAyGider:N2} TL\n" +
                $"Bu ay sonuç: {(buAyGelir - buAyGider):N2} TL\n\n" +
                $"Geçen ay gelir: {gecenAyGelir:N2} TL\n" +
                $"Geçen ay gider: {gecenAyGider:N2} TL\n" +
                $"Geçen ay sonuç: {(gecenAyGelir - gecenAyGider):N2} TL";
        }

        if (lower.Contains("en çok gider") || lower.Contains("en fazla gider"))
        {
            var gider = await _db.KasaHareketleri
                .Where(x => x.FirmaId == firmaId &&
                            x.Tip == HareketTipi.Cikis &&
                            x.Tarih >= ayBaslangic &&
                            x.Tarih < gelecekAy)
                .GroupBy(x => string.IsNullOrWhiteSpace(x.Aciklama) ? "Açıklamasız gider" : x.Aciklama)
                .Select(g => new
                {
                    Aciklama = g.Key,
                    Toplam = g.Sum(x => x.Tutar)
                })
                .OrderByDescending(x => x.Toplam)
                .FirstOrDefaultAsync();

            if (gider == null)
                return "Bu ay gider kaydı bulunamadı.";

            return $"Bu ay en çok gider: {gider.Aciklama} - {gider.Toplam:N2} TL";
        }

        if (lower.Contains("avans"))
        {
            var toplam = await _db.CalisanAvanslari
                .Where(x => x.FirmaId == firmaId &&
                            x.Tip == CalisanHareketTipi.Avans &&
                            x.Tarih >= ayBaslangic &&
                            x.Tarih < gelecekAy)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            return $"Bu ay toplam avans: {toplam:N2} TL";
        }

        if (lower.Contains("maaş") || lower.Contains("maas"))
        {
            var toplam = await _db.CalisanAvanslari
                .Where(x => x.FirmaId == firmaId &&
                            x.Tip == CalisanHareketTipi.MaasOdeme &&
                            x.Tarih >= ayBaslangic &&
                            x.Tarih < gelecekAy)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            return toplam > 0
                ? $"Bu ay toplam maaş ödemesi: {toplam:N2} TL"
                : "Bu ay maaş ödemesi kaydı bulunamadı.";
        }

        if (lower.Contains("genel durum") || lower.Contains("özet") || lower.Contains("işler nasıl"))
        {
            var calisanSayisi = await _db.Calisanlar.CountAsync(x => x.FirmaId == firmaId);
            var cariSayisi = await _db.CariKartlar.CountAsync(x => x.FirmaId == firmaId);
            var musteriSayisi = await _db.Musteriler.CountAsync(x => x.FirmaId == firmaId);
            var stokSayisi = await _db.StokUrunler.CountAsync(x => x.FirmaId == firmaId);

            var giris = await _db.KasaHareketleri
                .Where(x => x.FirmaId == firmaId && x.Tip == HareketTipi.Giris)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            var cikis = await _db.KasaHareketleri
                .Where(x => x.FirmaId == firmaId && x.Tip == HareketTipi.Cikis)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            return
                $"Genel durum:\n\n" +
                $"- Kasa bakiyesi: {(giris - cikis):N2} TL\n" +
                $"- Çalışan sayısı: {calisanSayisi}\n" +
                $"- Cari sayısı: {cariSayisi}\n" +
                $"- Müşteri sayısı: {musteriSayisi}\n" +
                $"- Stok ürün sayısı: {stokSayisi}";
        }

        return "Soruyu anladım ancak henüz buna cevap verecek sistem eklenmedi.";
    }
}

public class ChatMesaj
{
    public string Gonderen { get; set; } = "";
    public string Metin { get; set; } = "";
}