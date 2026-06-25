using System.ComponentModel.DataAnnotations;

namespace MuhasebeTakip2.App.Models;

public class IslemGecmisi
{
    public int Id { get; set; }

    public int FirmaId { get; set; }
    public Firma? Firma { get; set; }

    public int? KullaniciId { get; set; }

    [MaxLength(100)]
    public string KullaniciAdi { get; set; } = "";

    [MaxLength(80)]
    public string Modul { get; set; } = "";

    [MaxLength(30)]
    public string IslemTuru { get; set; } = "";

    [MaxLength(500)]
    public string Aciklama { get; set; } = "";

    public string? EskiDeger { get; set; }
    public string? YeniDeger { get; set; }

    [MaxLength(80)]
    public string? IpAdresi { get; set; }

    [MaxLength(300)]
    public string? TarayiciBilgisi { get; set; }

    public DateTime Tarih { get; set; } = DateTime.UtcNow;
}
