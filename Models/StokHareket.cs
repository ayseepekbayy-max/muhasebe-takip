using System.ComponentModel.DataAnnotations;

namespace MuhasebeTakip2.App.Models;

public enum StokHareketTipi
{
    Giris = 1,
    Cikis = 2
}

public class StokHareket
{
    public int Id { get; set; }

    public int? FirmaId { get; set; }
    
    public Firma? Firma { get; set; }

    public string Ad { get; set; } = "";

    public int StokUrunId { get; set; }
    public StokUrun? StokUrun { get; set; }

    public DateTime Tarih { get; set; } = DateTime.Today;

    public StokHareketTipi Tip { get; set; } = StokHareketTipi.Giris;

    [Range(0.01, 999999999)]
    public decimal Miktar { get; set; }

    [MaxLength(250)]
    public string Aciklama { get; set; } = "";
}