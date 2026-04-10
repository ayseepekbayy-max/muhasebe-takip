using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Globalization;

namespace MuhasebeTakip2.App.Pages.Maliyet;

public class IndexModel : PageModel
{
    public int Adet { get; set; }

    public decimal PlakaBirParcaMaliyeti { get; set; }
    public decimal BantBirParcaMaliyeti { get; set; }
    public decimal MalzemeBirParcaMaliyeti { get; set; }

    public decimal ToplamBirParcaMaliyet { get; set; }
    public decimal ToplamGenelMaliyet { get; set; }

    public bool HesapVar { get; set; }

    public IActionResult OnGet()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        VerileriYukle(firmaId.Value);
        return Page();
    }

    public IActionResult OnPostTemizle()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        string[] keys =
        {
            $"Maliyet_{firmaId}_Adet",
            $"Maliyet_{firmaId}_BirPlakadanCikan",
            $"Maliyet_{firmaId}_GerekliPlaka",
            $"Maliyet_{firmaId}_PlakaBirParcaMaliyeti",
            $"Maliyet_{firmaId}_PlakaToplamMaliyeti",
            $"Maliyet_{firmaId}_BantBirParcaMaliyeti",
            $"Maliyet_{firmaId}_BantToplamMaliyeti",
            $"Maliyet_{firmaId}_MalzemeBirParcaMaliyeti",
            $"Maliyet_{firmaId}_MalzemeToplamMaliyeti",
            $"Maliyet_{firmaId}_ToplamBirParcaMaliyet_Tum",
            $"Maliyet_{firmaId}_ToplamGenelMaliyet_Tum"
        };

        foreach (var key in keys)
            HttpContext.Session.Remove(key);

        return RedirectToPage();
    }

    private void VerileriYukle(int firmaId)
    {
        Adet = HttpContext.Session.GetInt32($"Maliyet_{firmaId}_Adet") ?? 0;

        PlakaBirParcaMaliyeti = SessionDecimalOku($"Maliyet_{firmaId}_PlakaBirParcaMaliyeti");
        BantBirParcaMaliyeti = SessionDecimalOku($"Maliyet_{firmaId}_BantBirParcaMaliyeti");
        MalzemeBirParcaMaliyeti = SessionDecimalOku($"Maliyet_{firmaId}_MalzemeBirParcaMaliyeti");

        ToplamBirParcaMaliyet = SessionDecimalOku($"Maliyet_{firmaId}_ToplamBirParcaMaliyet_Tum");
        ToplamGenelMaliyet = SessionDecimalOku($"Maliyet_{firmaId}_ToplamGenelMaliyet_Tum");

        if (ToplamBirParcaMaliyet <= 0)
            ToplamBirParcaMaliyet = PlakaBirParcaMaliyeti + BantBirParcaMaliyeti + MalzemeBirParcaMaliyeti;

        if (ToplamGenelMaliyet <= 0 && Adet > 0)
            ToplamGenelMaliyet = ToplamBirParcaMaliyet * Adet;

        HesapVar =
            Adet > 0 ||
            PlakaBirParcaMaliyeti > 0 ||
            BantBirParcaMaliyeti > 0 ||
            MalzemeBirParcaMaliyeti > 0;
    }

    private decimal SessionDecimalOku(string key)
    {
        var value = HttpContext.Session.GetString(key);
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed);
        return parsed;
    }
}