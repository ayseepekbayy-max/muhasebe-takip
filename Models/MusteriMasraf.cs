using System.ComponentModel.DataAnnotations;

namespace MuhasebeTakip2.App.Models;

public class MusteriMasraf
{
    public int Id { get; set; }

    public int? FirmaId { get; set; }
    
    public Firma? Firma { get; set; }

    public string Ad { get; set; } = "";
    
    public int MusteriIsId { get; set; }
    public MusteriIs? MusteriIs { get; set; }

    public DateTime Tarih { get; set; } = DateTime.Today;

    [MaxLength(200)]
    public string Aciklama { get; set; } = "";

    [Range(0.01, 999999999)]
    public decimal Tutar { get; set; }
}