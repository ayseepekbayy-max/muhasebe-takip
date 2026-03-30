using System.ComponentModel.DataAnnotations;

namespace MuhasebeTakip2.App.Models;

public class Musteri
{
    public int Id { get; set; }

    public int? FirmaId { get; set; }
    
    public Firma? Firma { get; set; }

    public string Ad { get; set; } = "";

    [Required, MaxLength(120)]
    public string AdSoyad { get; set; } = "";

    [MaxLength(30)]
    public string Telefon { get; set; } = "";

    [MaxLength(250)]
    public string Adres { get; set; } = "";
}