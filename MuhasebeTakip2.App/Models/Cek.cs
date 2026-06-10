using System.ComponentModel.DataAnnotations;

namespace MuhasebeTakip2.App.Models;

public enum CekTipi
{
    Alinacak = 1,
    Odenecek = 2
}

public class Cek
{
    public int Id { get; set; }

    public int FirmaId { get; set; }
    public Firma? Firma { get; set; }

    [MaxLength(100)]
    public string No { get; set; } = "";

    public DateTime Tarih { get; set; } = DateTime.Today;

    public decimal Tutar { get; set; }

    [MaxLength(300)]
    public string Aciklama { get; set; } = "";

    public CekTipi Tip { get; set; }

    [MaxLength(300)]
    public string? ResimYolu { get; set; }

    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
}