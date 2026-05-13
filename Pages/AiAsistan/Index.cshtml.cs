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

    public IndexModel(
        AppDbContext db,
        NovaReplyService novaReplyService)
    {
        _db = db;
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
                "İşlem sırasında hata oluştu.\n\n" +
                $"Hata: {ex.Message}\n\n" +
                $"Detay: {ex.InnerException?.Message}";
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
            cevap =
                "İşlem sırasında hata oluştu.\n\n" +
                $"Hata: {ex.Message}\n\n" +
                $"Detay: {ex.InnerException?.Message}";
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

        return await CevapUret(soru, firmaId);
    }

    private async Task<string> CevapUret(string soru, int firmaId)
    {
        var lower = soru.ToLowerInvariant();

        var bugun = DateTime.UtcNow.Date;
        var yarin = bugun.AddDays(1);
        var ayBaslangic = new DateTime(
            bugun.Year,
            bugun.Month,
            1,
            0,
            0,
            0,
            DateTimeKind.Utc);

        var gelecekAy = ayBaslangic.AddMonths(1);

        if (lower.Contains("çalışan") || lower.Contains("personel"))
        {
            var sayi = await _db.Calisanlar
                .CountAsync(x => x.FirmaId == firmaId);

            return $"Toplam çalışan sayınız: {sayi}";
        }

        if (lower.Contains("müşteri") || lower.Contains("musteri"))
        {
            var sayi = await _db.Musteriler
                .CountAsync(x => x.FirmaId == firmaId);

            return $"Toplam müşteri sayınız: {sayi}";
        }

        if (lower.Contains("cari"))
        {
            var sayi = await _db.CariKartlar
                .CountAsync(x => x.FirmaId == firmaId);

            return $"Toplam cari kart sayınız: {sayi}";
        }

        if (lower.Contains("stok"))
        {
            var sayi = await _db.StokUrunler
                .CountAsync(x => x.FirmaId == firmaId);

            return $"Toplam stok ürün sayınız: {sayi}";
        }

        if (lower.Contains("bugün") &&
            (lower.Contains("giriş") || lower.Contains("gelir")))
        {
            var toplam = await _db.KasaHareketleri
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.Tarih >= bugun &&
                    x.Tarih < yarin &&
                    x.Tip == HareketTipi.Giris)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            return $"Bugünkü toplam kasa girişiniz: {toplam:N2} TL";
        }

        if (lower.Contains("bugün") &&
            (lower.Contains("çıkış") || lower.Contains("gider")))
        {
            var toplam = await _db.KasaHareketleri
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.Tarih >= bugun &&
                    x.Tarih < yarin &&
                    x.Tip == HareketTipi.Cikis)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            return $"Bugünkü toplam kasa çıkışınız: {toplam:N2} TL";
        }

        if (lower.Contains("kasa") &&
            (lower.Contains("bakiye") || lower.Contains("durum")))
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

            return $"Güncel kasa bakiyeniz: {(giris - cikis):N2} TL";
        }

        if (lower.Contains("bu ay") &&
            (lower.Contains("gelir") || lower.Contains("giriş")))
        {
            var toplam = await _db.KasaHareketleri
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.Tarih >= ayBaslangic &&
                    x.Tarih < gelecekAy &&
                    x.Tip == HareketTipi.Giris)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            return $"Bu ay toplam geliriniz: {toplam:N2} TL";
        }

        if (lower.Contains("bu ay") &&
            (lower.Contains("gider") ||
             lower.Contains("çıkış") ||
             lower.Contains("masraf")))
        {
            var toplam = await _db.KasaHareketleri
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.Tarih >= ayBaslangic &&
                    x.Tarih < gelecekAy &&
                    x.Tip == HareketTipi.Cikis)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            return $"Bu ay toplam gideriniz: {toplam:N2} TL";
        }

        if (lower.Contains("genel durum") ||
            lower.Contains("özet") ||
            lower.Contains("işler nasıl"))
        {
            var calisanSayisi = await _db.Calisanlar
                .CountAsync(x => x.FirmaId == firmaId);

            var cariSayisi = await _db.CariKartlar
                .CountAsync(x => x.FirmaId == firmaId);

            var musteriSayisi = await _db.Musteriler
                .CountAsync(x => x.FirmaId == firmaId);

            var stokSayisi = await _db.StokUrunler
                .CountAsync(x => x.FirmaId == firmaId);

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