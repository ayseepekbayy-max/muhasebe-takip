namespace MuhasebeTakip2.App.Models;

public class Calisan
{
    public decimal Maas { get; set; }      // Aylık maaş
    
    public List<CalisanAvans> Avanslar { get; set; } = new();
    
    public decimal Avans { get; set; }     // Toplam avans
    
    public int Id { get; set; }

    public int? FirmaId { get; set; }
    
    public Firma? Firma { get; set; }

    public string Ad { get; set; } = "";

    public string AdSoyad { get; set; } = "";

    public string? Telefon { get; set; }

    public DateTime IseGirisTarihi { get; set; } = DateTime.Now;
}