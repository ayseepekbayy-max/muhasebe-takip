using System.ComponentModel.DataAnnotations;

namespace MuhasebeTakip2.App.Models;

public class StokUrun
{
    public int Id { get; set; }

    public int? FirmaId { get; set; }
    
    public Firma? Firma { get; set; }

    [Required, MaxLength(120)]
    public string Ad { get; set; } = "";

    [MaxLength(50)]
    public string Kod { get; set; } = "";

    [MaxLength(30)]
    public string Birim { get; set; } = "Adet"; // Adet, m2, mt vb.
}