using System.ComponentModel.DataAnnotations;

namespace MuhasebeTakip2.App.Models;

public class YoneticiNotu
{
    public int Id { get; set; }

    public int FirmaId { get; set; }
    public Firma? Firma { get; set; }

    public int KullaniciId { get; set; }
    public Kullanici? Kullanici { get; set; }

    [Required, StringLength(500)]
    public string NotMetni { get; set; } = "";

    public DateTime OlusturmaTarihi { get; set; }
    public DateTime SonKullanmaTarihi { get; set; }
}
