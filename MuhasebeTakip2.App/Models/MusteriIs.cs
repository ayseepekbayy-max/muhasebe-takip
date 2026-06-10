using System.ComponentModel.DataAnnotations;

namespace MuhasebeTakip2.App.Models;

public class MusteriIs
{
    public int Id { get; set; }

    public int? FirmaId { get; set; }
    
    public Firma? Firma { get; set; }

    public string Ad { get; set; } = "";

    public int MusteriId { get; set; }
    
    public Musteri? Musteri { get; set; }

    public DateTime Tarih { get; set; } = DateTime.Today;

    [Required, MaxLength(160)]
    public string IsAdi { get; set; } = "";

    [Range(0, 999999999)]
    public decimal Gelir { get; set; }

    public List<MusteriMasraf> Masraflar { get; set; } = new();
}