namespace MuhasebeTakip2.App.Models;

public enum CariTip
{
    Alici = 1,
    Satici = 2,
    HerIkisi = 3
}

public static class CariTipExtensions
{
    public static bool AliciMi(this CariTip tip) =>
        tip is CariTip.Alici or CariTip.HerIkisi;

    public static bool SaticiMi(this CariTip tip) =>
        tip is CariTip.Satici or CariTip.HerIkisi;

    public static string Metin(this CariTip tip) => tip switch
    {
        CariTip.Alici => "Alıcı",
        CariTip.Satici => "Satıcı",
        CariTip.HerIkisi => "Alıcı ve Satıcı",
        _ => "Cari"
    };
}

public class CariKart
{
    public int Id { get; set; }

    public int? FirmaId { get; set; }

    public Firma? Firma { get; set; }

    public string Ad { get; set; } = "";

    public string Unvan { get; set; } = "";

    public string? Telefon { get; set; }

    public string? VergiNo { get; set; }

    public CariTip Tip { get; set; }

    public DateTime OlusturmaTarihi { get; set; } = DateTime.Now;

    public bool AktifMi { get; set; } = true;

    public DateTime? ArsivTarihi { get; set; }

    public string? ArsivNotu { get; set; }
}
