using System.ComponentModel.DataAnnotations;
namespace MuhasebeTakip2.App.Models;

public enum KasaHesapTuru { Nakit = 1, Banka = 2 }
public class KasaHesap
{
    public int Id { get; set; }
    public int FirmaId { get; set; }
    public Firma? Firma { get; set; }
    [Required, MaxLength(100)] public string Ad { get; set; } = "";
    public KasaHesapTuru Tur { get; set; } = KasaHesapTuru.Nakit;
    public decimal AcilisBakiyesi { get; set; }
    public bool AktifMi { get; set; } = true;
}
