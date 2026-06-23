namespace MuhasebeTakip2.App.Models;

public class FaturaKalem
{
    public int Id { get; set; }

    public int FaturaId { get; set; }
    public Fatura? Fatura { get; set; }

    public string Aciklama { get; set; } = "";

    public decimal Miktar { get; set; } = 1;

    public decimal BirimFiyat { get; set; }

    public decimal KdvOrani { get; set; } = 20;

    public decimal AraToplam { get; set; }

    public decimal KdvTutar { get; set; }

    public decimal GenelToplam { get; set; }
}