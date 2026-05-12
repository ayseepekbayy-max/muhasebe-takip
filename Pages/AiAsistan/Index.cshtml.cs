using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Helpers;

namespace MuhasebeTakip2.App.Pages.AiAsistan;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public string Soru { get; set; } = "";

    public List<ChatMesaj> Mesajlar { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
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

        var cevap = await CevapUret(Soru, firmaId.Value);

        Mesajlar.Add(new ChatMesaj
        {
            Gonderen = "Ai",
            Metin = cevap
        });

        HttpContext.Session.SetObject("AiMesajlar", Mesajlar);

        Soru = "";

        return Page();
    }

    public IActionResult OnPostTemizle()
    {
        HttpContext.Session.Remove("AiMesajlar");

        return RedirectToPage();
    }

    private async Task<string> CevapUret(string soru, int firmaId)
    {
        var lower = soru.ToLower();

        if (lower.Contains("çalışan") || lower.Contains("personel"))
        {
            var sayi = await _db.Calisanlar
                .CountAsync(x => x.FirmaId == firmaId);

            return $"Toplam çalışan sayınız: {sayi}";
        }

        if (lower.Contains("cari"))
        {
            var sayi = await _db.CariKartlar
                .CountAsync(x => x.FirmaId == firmaId);

            return $"Toplam cari kart sayınız: {sayi}";
        }

        if (lower.Contains("bugün") && lower.Contains("giriş"))
        {
            var bugun = DateTime.UtcNow.Date;
            var yarin = bugun.AddDays(1);

            var toplam = await _db.KasaHareketleri
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.Tarih >= bugun &&
                    x.Tarih < yarin &&
                    x.Tip == HareketTipi.Giris)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            return $"Bugünkü toplam kasa girişiniz: {toplam}";
        }

        if (lower.Contains("bugün") && lower.Contains("çıkış"))
        {
            var bugun = DateTime.UtcNow.Date;
            var yarin = bugun.AddDays(1);

            var toplam = await _db.KasaHareketleri
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.Tarih >= bugun &&
                    x.Tarih < yarin &&
                    x.Tip == HareketTipi.Cikis)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            return $"Bugünkü toplam kasa çıkışınız: {toplam}";
        }

        return "Soruyu anladım ancak henüz buna cevap verecek sistem eklenmedi.";
    }
}

public class ChatMesaj
{
    public string Gonderen { get; set; } = "";
    public string Metin { get; set; } = "";
}