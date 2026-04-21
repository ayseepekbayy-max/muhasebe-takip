using System.ComponentModel.DataAnnotations.Schema;

namespace FirmovaAI.Models.Muhasebe;

public class CalisanAvans
{
    public int Id { get; set; }

    public string Ad { get; set; } = "";

    public int? FirmaId { get; set; }

    public int CalisanId { get; set; }

    public Calisan? Calisan { get; set; }

    public DateTime Tarih { get; set; } = DateTime.Today;

    public decimal Tutar { get; set; }

    public string Aciklama { get; set; } = "";

    public CalisanHareketTipi Tip { get; set; } = CalisanHareketTipi.Avans;

    public bool ArsivlendiMi { get; set; } = false;
}