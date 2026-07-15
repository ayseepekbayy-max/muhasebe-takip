using System.ComponentModel.DataAnnotations;

namespace MuhasebeTakip2.App.Models;

public class OdemeBildirimGizleme
{
    public int Id { get; set; }
    public int FirmaId { get; set; }
    public Firma? Firma { get; set; }
    public int KullaniciId { get; set; }
    public Kullanici? Kullanici { get; set; }
    public int OdemePlaniId { get; set; }
    public OdemePlani? OdemePlani { get; set; }
    public DateTime GizlemeTarihi { get; set; }
    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string? OlusturanKullaniciAdi { get; set; }
}
