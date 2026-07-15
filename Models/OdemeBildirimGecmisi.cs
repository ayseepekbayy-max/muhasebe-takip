using System.ComponentModel.DataAnnotations;

namespace MuhasebeTakip2.App.Models;

public class OdemeBildirimGecmisi
{
    public int Id { get; set; }
    public int FirmaId { get; set; }
    public Firma? Firma { get; set; }
    public int KullaniciId { get; set; }
    public Kullanici? Kullanici { get; set; }
    public int OdemePlaniId { get; set; }
    public OdemePlani? OdemePlani { get; set; }

    [MaxLength(30)]
    public string BildirimTuru { get; set; } = "Email";

    [MaxLength(7)]
    public string OdemeDonemi { get; set; } = "";

    [MaxLength(254)]
    public string HedefEmail { get; set; } = "";

    public bool BasariliMi { get; set; }

    [MaxLength(500)]
    public string? HataMesaji { get; set; }

    public DateTime BildirimTarihi { get; set; } = DateTime.UtcNow;
    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
}
