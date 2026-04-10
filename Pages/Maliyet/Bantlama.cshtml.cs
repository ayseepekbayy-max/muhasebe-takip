using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Globalization;

namespace MuhasebeTakip2.App.Pages.Maliyet;

public class BantlamaModel : PageModel
{
    [BindProperty]
    public decimal ParcaEn { get; set; }

    [BindProperty]
    public decimal ParcaBoy { get; set; }

    [BindProperty]
    public int Adet { get; set; }

    [BindProperty]
    public decimal BantMetrekareFiyati { get; set; }

    public bool Hesaplandi { get; set; }

    public decimal PlakaBirParcaMaliyeti { get; set; }

    public decimal BirParcaBantAlani { get; set; }
    public decimal ToplamBantAlani { get; set; }
    public decimal ToplamBantMaliyeti { get; set; }
    public decimal BirParcaBantMaliyeti { get; set; }

    public decimal ToplamBirParcaMaliyet { get; set; }
    public decimal ToplamGenelMaliyet { get; set; }

    public string Hata { get; set; } = "";
    public string Mesaj { get; set; } = "";

    public IActionResult OnGet()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        Adet = HttpContext.Session.GetInt32($"Maliyet_{firmaId.Value}_Adet") ?? 0;

        var plakaBirim = HttpContext.Session.GetString($"Maliyet_{firmaId.Value}_PlakaBirParcaMaliyeti");
        if (!string.IsNullOrWhiteSpace(plakaBirim))
        {
            decimal.TryParse(plakaBirim, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed);
            PlakaBirParcaMaliyeti = parsed;
        }

        return Page();
    }

    public IActionResult OnPost()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var sessionAdet = HttpContext.Session.GetInt32($"Maliyet_{firmaId.Value}_Adet") ?? 0;
        if (Adet <= 0 && sessionAdet > 0)
            Adet = sessionAdet;

        var plakaBirim = HttpContext.Session.GetString($"Maliyet_{firmaId.Value}_PlakaBirParcaMaliyeti");
        if (!string.IsNullOrWhiteSpace(plakaBirim))
        {
            decimal.TryParse(plakaBirim, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed);
            PlakaBirParcaMaliyeti = parsed;
        }

        if (ParcaEn <= 0 || ParcaBoy <= 0 || Adet <= 0 || BantMetrekareFiyati <= 0)
        {
            Hata = "Tüm alanlara 0'dan büyük değer girin.";
            return Page();
        }

        decimal bantGenisligiCm = 2.2m;
        decimal cevreCm = 2 * (ParcaEn + ParcaBoy);

        BirParcaBantAlani = Math.Round((cevreCm * bantGenisligiCm) / 10000m, 4);
        ToplamBantAlani = Math.Round(BirParcaBantAlani * Adet, 4);
        ToplamBantMaliyeti = Math.Round(ToplamBantAlani * BantMetrekareFiyati, 2);
        BirParcaBantMaliyeti = Math.Round(ToplamBantMaliyeti / Adet, 2);

        ToplamBirParcaMaliyet = Math.Round(PlakaBirParcaMaliyeti + BirParcaBantMaliyeti, 2);
        ToplamGenelMaliyet = Math.Round(ToplamBirParcaMaliyet * Adet, 2);

        HttpContext.Session.SetInt32($"Maliyet_{firmaId.Value}_Adet", Adet);
        HttpContext.Session.SetString($"Maliyet_{firmaId.Value}_BantBirParcaMaliyeti", BirParcaBantMaliyeti.ToString(CultureInfo.InvariantCulture));
        HttpContext.Session.SetString($"Maliyet_{firmaId.Value}_BantToplamMaliyeti", ToplamBantMaliyeti.ToString(CultureInfo.InvariantCulture));
        HttpContext.Session.SetString($"Maliyet_{firmaId.Value}_ToplamBirParcaMaliyet_Tum", ToplamBirParcaMaliyet.ToString(CultureInfo.InvariantCulture));
        HttpContext.Session.SetString($"Maliyet_{firmaId.Value}_ToplamGenelMaliyet_Tum", ToplamGenelMaliyet.ToString(CultureInfo.InvariantCulture));

        Hesaplandi = true;
        Mesaj = "Bantlama hesabı kaydedildi.";
        return Page();
    }
}