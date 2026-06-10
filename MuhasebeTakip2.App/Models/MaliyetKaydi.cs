namespace MuhasebeTakip2.App.Models;

public class MaliyetKaydi
{
    public int Id { get; set; }

    public int FirmaId { get; set; }
    public Firma? Firma { get; set; }

    public string UretimAdi { get; set; } = "";

    public decimal UretimAdedi { get; set; }

    public decimal PlakaMaliyeti { get; set; }

    public decimal BantlamaMaliyeti { get; set; }

    public decimal ArkalikMaliyeti { get; set; }

    public decimal MalzemeMaliyeti { get; set; }

    public decimal ToplamMaliyet { get; set; }

    public decimal BirimMaliyet { get; set; }

    public string Kaynak { get; set; } = "Üretim";

    public string DetayJson { get; set; } = "";

    public string OkunanMetin { get; set; } = "";

    public DateTime HesapTarihi { get; set; } = DateTime.UtcNow;
}
