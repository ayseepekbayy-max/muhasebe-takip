using System.ComponentModel.DataAnnotations;

namespace MuhasebeTakip2.App.Models;

public enum OdemeTuru
{
    Kredi = 1,
    KrediKarti = 2,
    Kira = 3,
    Vergi = 4,
    Fatura = 5,
    Personel = 6,
    Abonelik = 7,
    Diger = 8
}

public enum OdemeDurumu
{
    Odendi = 1,
    Yaklasiyor = 2,
    Bugun = 3,
    Gecikti = 4,
    Tamamlandi = 5,
    Pasif = 6
}

public class OdemePlani
{
    public int Id { get; set; }

    public int FirmaId { get; set; }
    public Firma? Firma { get; set; }

    [Required(ErrorMessage = "Ã–deme adÄ± zorunludur.")]
    [MaxLength(150)]
    public string OdemeAdi { get; set; } = "";

    public OdemeTuru OdemeTuru { get; set; } = OdemeTuru.Diger;

    [MaxLength(500)]
    public string? Aciklama { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "AylÄ±k Ã¶deme tutarÄ± sÄ±fÄ±rdan bÃ¼yÃ¼k olmalÄ±dÄ±r.")]
    public decimal AylikOdemeTutari { get; set; }

    [Range(1, 600, ErrorMessage = "Toplam taksit sayÄ±sÄ± 1 ile 600 arasÄ±nda olmalÄ±dÄ±r.")]
    public int ToplamTaksitSayisi { get; set; } = 1;

    [Range(0, 600, ErrorMessage = "Kalan taksit sayÄ±sÄ± 0 ile 600 arasÄ±nda olmalÄ±dÄ±r.")]
    public int KalanTaksitSayisi { get; set; } = 1;

    [Range(1, 31, ErrorMessage = "Ã–deme gÃ¼nÃ¼ 1 ile 31 arasÄ±nda olmalÄ±dÄ±r.")]
    public int OdemeGunu { get; set; } = 1;

    public DateTime IlkOdemeTarihi { get; set; } = DateTime.UtcNow.Date;
    public DateTime SonrakiOdemeTarihi { get; set; } = DateTime.UtcNow.Date;
    public DateTime? SonOdemeTarihi { get; set; }
    public bool SonOdemeYapildiMi { get; set; }

    [Range(0, 60, ErrorMessage = "Bildirim gÃ¼nÃ¼ 0 ile 60 arasÄ±nda olmalÄ±dÄ±r.")]
    public int BildirimGunu { get; set; } = 3;

    public bool BildirimAktifMi { get; set; } = true;
    public bool OtomatikTaksitDusur { get; set; } = true;
    public bool AktifMi { get; set; } = true;
    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
    public DateTime? GuncellemeTarihi { get; set; }

    public List<OdemeHareketi> Hareketler { get; set; } = new();

    public decimal KalanToplamTutar => AylikOdemeTutari * Math.Max(0, KalanTaksitSayisi);
}

public static class OdemeTuruExtensions
{
    public static string Metin(this OdemeTuru tur) => tur switch
    {
        OdemeTuru.Kredi => "Kredi",
        OdemeTuru.KrediKarti => "Kredi KartÄ±",
        OdemeTuru.Kira => "Kira",
        OdemeTuru.Vergi => "Vergi",
        OdemeTuru.Fatura => "Fatura",
        OdemeTuru.Personel => "Personel",
        OdemeTuru.Abonelik => "Abonelik",
        _ => "DiÄŸer"
    };

    public static string Metin(this OdemeDurumu durum) => durum switch
    {
        OdemeDurumu.Odendi => "Ã–dendi",
        OdemeDurumu.Yaklasiyor => "YaklaÅŸÄ±yor",
        OdemeDurumu.Bugun => "BugÃ¼n",
        OdemeDurumu.Gecikti => "Gecikti",
        OdemeDurumu.Tamamlandi => "TamamlandÄ±",
        OdemeDurumu.Pasif => "Pasif",
        _ => "Bilinmiyor"
    };

    public static string CssSinifi(this OdemeDurumu durum) => durum switch
    {
        OdemeDurumu.Odendi => "status-pill-income",
        OdemeDurumu.Yaklasiyor => "status-pill-warning",
        OdemeDurumu.Bugun => "status-pill-primary",
        OdemeDurumu.Gecikti => "status-pill-expense",
        OdemeDurumu.Tamamlandi => "status-pill-neutral",
        OdemeDurumu.Pasif => "status-pill-neutral",
        _ => "status-pill-neutral"
    };
}