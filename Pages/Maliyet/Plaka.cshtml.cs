using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MuhasebeTakip2.App.Pages.Maliyet;

public class PlakaModel : PageModel
{
    [BindProperty]
    public decimal PlakaEn { get; set; }

    [BindProperty]
    public decimal PlakaBoy { get; set; }

    [BindProperty]
    public decimal PlakaMetrekareFiyati { get; set; }

    [BindProperty]
    public decimal ParcaEn { get; set; }

    [BindProperty]
    public decimal ParcaBoy { get; set; }

    [BindProperty]
    public int Adet { get; set; }

    public bool Hesaplandi { get; set; }

    public int EnYonundeSigan { get; set; }
    public int BoyYonundeSigan { get; set; }
    public int BirPlakadanCikanAdet { get; set; }
    public int GerekliPlakaSayisi { get; set; }

    public int NormalEnYonundeSigan { get; set; }
    public int NormalBoyYonundeSigan { get; set; }
    public int NormalToplam { get; set; }

    public int DonukEnYonundeSigan { get; set; }
    public int DonukBoyYonundeSigan { get; set; }
    public int DonukToplam { get; set; }

    public string KullanilanYerlesim { get; set; } = "";

    public decimal BirPlakaMetrekare { get; set; }
    public decimal BirParcaMetrekare { get; set; }

    public decimal ToplamMaliyet { get; set; }
    public decimal BirParcaMaliyeti { get; set; }

    public string Hata { get; set; } = "";

    public IActionResult OnGet()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        return Page();
    }

    public IActionResult OnPost()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        if (PlakaEn <= 0 || PlakaBoy <= 0 || PlakaMetrekareFiyati <= 0 || ParcaEn <= 0 || ParcaBoy <= 0 || Adet <= 0)
        {
            Hata = "Tüm alanlara 0'dan büyük değer girin.";
            return Page();
        }

        NormalEnYonundeSigan = (int)(PlakaEn / ParcaEn);
        NormalBoyYonundeSigan = (int)(PlakaBoy / ParcaBoy);
        NormalToplam = NormalEnYonundeSigan * NormalBoyYonundeSigan;

        DonukEnYonundeSigan = (int)(PlakaEn / ParcaBoy);
        DonukBoyYonundeSigan = (int)(PlakaBoy / ParcaEn);
        DonukToplam = DonukEnYonundeSigan * DonukBoyYonundeSigan;

        if (NormalToplam <= 0 && DonukToplam <= 0)
        {
            Hata = "Bu parça ölçüsü plakaya sığmıyor.";
            return Page();
        }

        if (DonukToplam > NormalToplam)
        {
            EnYonundeSigan = DonukEnYonundeSigan;
            BoyYonundeSigan = DonukBoyYonundeSigan;
            BirPlakadanCikanAdet = DonukToplam;
            KullanilanYerlesim = "Döndürülmüş yerleşim kullanıldı";
        }
        else
        {
            EnYonundeSigan = NormalEnYonundeSigan;
            BoyYonundeSigan = NormalBoyYonundeSigan;
            BirPlakadanCikanAdet = NormalToplam;
            KullanilanYerlesim = "Normal yerleşim kullanıldı";
        }

        GerekliPlakaSayisi = (int)Math.Ceiling((decimal)Adet / BirPlakadanCikanAdet);

        BirPlakaMetrekare = Math.Round((PlakaEn * PlakaBoy) / 10000m, 4);
        BirParcaMetrekare = Math.Round((ParcaEn * ParcaBoy) / 10000m, 4);

        var birPlakaMaliyeti = BirPlakaMetrekare * PlakaMetrekareFiyati;
        ToplamMaliyet = Math.Round(birPlakaMaliyeti * GerekliPlakaSayisi, 2);
        BirParcaMaliyeti = Math.Round(birPlakaMaliyeti / BirPlakadanCikanAdet, 2);

        // Session'a kaydet
        HttpContext.Session.SetInt32("Maliyet_Adet", Adet);
        HttpContext.Session.SetInt32("Maliyet_BirPlakadanCikan", BirPlakadanCikanAdet);
        HttpContext.Session.SetInt32("Maliyet_GerekliPlaka", GerekliPlakaSayisi);
        HttpContext.Session.SetString("Maliyet_PlakaBirParcaMaliyeti", BirParcaMaliyeti.ToString(System.Globalization.CultureInfo.InvariantCulture));
        HttpContext.Session.SetString("Maliyet_PlakaToplamMaliyeti", ToplamMaliyet.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Hesaplandi = true;
        return Page();
    }
}