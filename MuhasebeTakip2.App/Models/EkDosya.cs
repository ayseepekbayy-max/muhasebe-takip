namespace MuhasebeTakip2.App.Models;

public class EkDosya
{
    public int Id { get; set; }
    public int FirmaId { get; set; }
    public Firma? Firma { get; set; }

    public int? FaturaId { get; set; }
    public Fatura? Fatura { get; set; }

    public int? CariKartId { get; set; }
    public CariKart? CariKart { get; set; }

    public string DosyaAdi { get; set; } = "";
    public string DosyaYolu { get; set; } = "";
    public string IcerikTipi { get; set; } = "";
    public long Boyut { get; set; }
    public DateTime YuklemeTarihi { get; set; } = DateTime.UtcNow;
}