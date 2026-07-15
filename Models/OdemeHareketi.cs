using System.ComponentModel.DataAnnotations;

namespace MuhasebeTakip2.App.Models;

public class OdemeHareketi
{
    public int Id { get; set; }
    public int FirmaId { get; set; }
    public Firma? Firma { get; set; }
    public int OdemePlaniId { get; set; }
    public OdemePlani? OdemePlani { get; set; }
    public DateTime OdemeTarihi { get; set; } = DateTime.UtcNow.Date;
    public decimal Tutar { get; set; }

    [MaxLength(500)]
    public string? Aciklama { get; set; }

    public int KalanTaksitSayisi { get; set; }
    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
    public int? OlusturanKullaniciId { get; set; }

    [MaxLength(100)]
    public string? OlusturanKullaniciAdi { get; set; }
}