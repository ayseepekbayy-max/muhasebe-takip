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

    public int PlakaAdet { get; set; }
    public decimal PlakaBirParcaMaliyeti { get; set; }

    public decimal BirParcaBantAlani { get; set; }
    public decimal ToplamBantAlani { get; set; }
    public decimal ToplamBantMaliyeti { get; set; }
    public decimal BirParcaBantMaliyeti { get; set; }

    public decimal ToplamBirParcaMaliyet { get; set; }
    public decimal ToplamGenelMaliyet { get; set; }

    public string Hata { get; set; } = "";

    public IActionResult OnGet()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        // Plaka'dan gelen adet varsa göster
        PlakaAdet = HttpContext.Session.GetInt32("Maliyet_Adet") ?? 0;

        var plakaBirim = HttpContext.Session.GetString("Maliyet_PlakaBirParcaMaliyeti");
        if (!string.IsNullOrWhiteSpace(plakaBirim))
        {
            decimal.TryParse(plakaBirim, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed);
            PlakaBirParcaMaliyeti = parsed;
        }

        if (PlakaAdet > 0 && Adet == 0)
            Adet = PlakaAdet;

        return Page();
    }

    public IActionResult OnPost()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        PlakaAdet = HttpContext.Session.GetInt32("Maliyet_Adet") ?? 0;

        var plakaBirim = HttpContext.Session.GetString("Maliyet_PlakaBirParcaMaliyeti");
        if (!string.IsNullOrWhiteSpace(plakaBirim))
        {
            decimal.TryParse(plakaBirim, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed);
            PlakaBirParcaMaliyeti = parsed;
        }

        if (PlakaAdet > 0)
            Adet = PlakaAdet;

        if (ParcaEn <= 0 || ParcaBoy <= 0 || Adet <= 0 || BantMetrekareFiyati <= 0)
        {
            Hata = "Tüm alanlara 0'dan büyük değer girin.";
            return Page();
        }

        // Bant genişliği sabit: 2.2 cm
        decimal bantGenisligiCm = 2.2m;

        decimal cevreCm = 2 * (ParcaEn + ParcaBoy);

        // cm² -> m²
        BirParcaBantAlani = Math.Round((cevreCm * bantGenisligiCm) / 10000m, 4);

        ToplamBantAlani = Math.Round(BirParcaBantAlani * Adet, 4);
        ToplamBantMaliyeti = Math.Round(ToplamBantAlani * BantMetrekareFiyati, 2);
        BirParcaBantMaliyeti = Math.Round(ToplamBantMaliyeti / Adet, 2);

        ToplamBirParcaMaliyet = Math.Round(PlakaBirParcaMaliyeti + BirParcaBantMaliyeti, 2);
        ToplamGenelMaliyet = Math.Round(ToplamBirParcaMaliyet * Adet, 2);

        // Session'a kaydet
        HttpContext.Session.SetString("Maliyet_BantBirParcaMaliyeti", BirParcaBantMaliyeti.ToString(CultureInfo.InvariantCulture));
        HttpContext.Session.SetString("Maliyet_BantToplamMaliyeti", ToplamBantMaliyeti.ToString(CultureInfo.InvariantCulture));
        HttpContext.Session.SetString("Maliyet_ToplamBirParcaMaliyet", ToplamBirParcaMaliyet.ToString(CultureInfo.InvariantCulture));
        HttpContext.Session.SetString("Maliyet_ToplamGenelMaliyet", ToplamGenelMaliyet.ToString(CultureInfo.InvariantCulture));

        Hesaplandi = true;
        return Page();
    }
}