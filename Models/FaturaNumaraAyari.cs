namespace MuhasebeTakip2.App.Models;

public class FaturaNumaraAyari
{
    public int Id { get; set; }

    public int FirmaId { get; set; }

    public Firma? Firma { get; set; }

    public string Prefix { get; set; } = "FTR";

    public int SonNumara { get; set; }

    public int SiraUzunlugu { get; set; } = 4;

    public bool YilEkle { get; set; } = true;
}