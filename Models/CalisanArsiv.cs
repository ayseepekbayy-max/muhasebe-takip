using System.ComponentModel.DataAnnotations;

namespace MuhasebeTakip2.App.Models;

public class CalisanArsiv
{
    public int Id { get; set; }

    public int FirmaId { get; set; }
    public Firma? Firma { get; set; }

    public int EskiCalisanId { get; set; }

    [MaxLength(150)]
    public string AdSoyad { get; set; } = "";

    [MaxLength(50)]
    public string Telefon { get; set; } = "";

    public decimal Maas { get; set; }

    public DateTime IseGirisTarihi { get; set; }

    public DateTime AyrilisTarihi { get; set; } = DateTime.UtcNow;

    [MaxLength(300)]
    public string AyrilisNotu { get; set; } = "";
}