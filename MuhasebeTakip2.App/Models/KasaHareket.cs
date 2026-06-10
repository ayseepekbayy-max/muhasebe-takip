namespace MuhasebeTakip2.App.Models;

public enum HareketTipi
{
    Giris = 1,
    Cikis = 2
}

public class KasaHareket
{
    public int Id { get; set; }

    public int? FirmaId { get; set; }
    
    public Firma? Firma { get; set; }

    public DateTime Tarih { get; set; } = DateTime.Now;

    public HareketTipi Tip { get; set; }

    public decimal Tutar { get; set; }

    public string Aciklama { get; set; } = "";

    public int? CariKartId { get; set; }

    public CariKart? CariKart { get; set; }
}