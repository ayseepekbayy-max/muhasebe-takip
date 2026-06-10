using System.ComponentModel.DataAnnotations;

namespace MuhasebeTakip2.App.Models;

public enum PuantajDurum
{
    Geldi = 1,
    Gelmedi = 2,
    Izinli = 3,
    YarimGun = 4
}

public class CalisanPuantaj
{
    public int Id { get; set; }

    public int FirmaId { get; set; }

    public Firma? Firma { get; set; }

    public int CalisanId { get; set; }

    public Calisan? Calisan { get; set; }

    public DateTime Tarih { get; set; } = DateTime.Today;

    public PuantajDurum Durum { get; set; } = PuantajDurum.Geldi;

    public string? Not { get; set; }
}