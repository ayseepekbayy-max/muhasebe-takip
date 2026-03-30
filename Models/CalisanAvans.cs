using System.ComponentModel.DataAnnotations;

namespace MuhasebeTakip2.App.Models;

public enum CalisanHareketTipi
{
    Avans = 1,
    MaasOdeme = 2
}

public class CalisanAvans
{
    public int Id { get; set; }

    public string Ad { get; set; } = "";

    public int? FirmaId { get; set; }

    public Firma? Firma { get; set; }

    public int CalisanId { get; set; }
   
    public Calisan? Calisan { get; set; }

    public DateTime Tarih { get; set; } = DateTime.Today;

    [Range(0.01, 999999999)]
    public decimal Tutar { get; set; }

    [MaxLength(250)]
    public string Aciklama { get; set; } = "";

    public CalisanHareketTipi Tip { get; set; } = CalisanHareketTipi.Avans;
}