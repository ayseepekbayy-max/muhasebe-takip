using System.ComponentModel.DataAnnotations;
namespace MuhasebeTakip2.App.Models;

// One transfer row, not income/expense rows: total money and operating reports remain unchanged.
public class KasaTransfer
{
    public int Id { get; set; }
    public int FirmaId { get; set; }
    public Firma? Firma { get; set; }
    public int KaynakHesapId { get; set; }
    public KasaHesap? KaynakHesap { get; set; }
    public int HedefHesapId { get; set; }
    public KasaHesap? HedefHesap { get; set; }
    public decimal Tutar { get; set; }
    public DateTime Tarih { get; set; } = DateTime.UtcNow.Date;
    [MaxLength(500)] public string Aciklama { get; set; } = "";
}
