namespace MuhasebeTakip2.App.Models;

public enum CariTip
{
    Alici = 1,
    Satici = 2
}

public class CariKart
{
    public int Id { get; set; }

    public int? FirmaId { get; set; }

    public Firma? Firma { get; set; }
    
    public string Ad { get; set; } = "";

    public string Unvan { get; set; } = "";

    public string? Telefon { get; set; }

    public string? VergiNo { get; set; }

    public CariTip Tip { get; set; }

    public DateTime OlusturmaTarihi { get; set; } = DateTime.Now;
}