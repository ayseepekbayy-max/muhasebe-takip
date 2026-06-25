using System.ComponentModel.DataAnnotations.Schema;

namespace MuhasebeTakip2.App.Models;

public enum FaturaTipi
{
    Satis = 1,
    Alis = 2
}

public enum FaturaDurumu
{
    Bekliyor = 1,
    Odendi = 2,
    KismenOdendi = 3,
    Iptal = 4
}

public class Fatura
{
    public int Id { get; set; }

    public int? FirmaId { get; set; }
    public Firma? Firma { get; set; }

    public int? CariKartId { get; set; }
    public CariKart? CariKart { get; set; }

    public string FaturaNo { get; set; } = "";

    public FaturaTipi Tip { get; set; } = FaturaTipi.Satis;

    public DateTime Tarih { get; set; } = DateTime.UtcNow;

    public DateTime? VadeTarihi { get; set; }

    public decimal AraToplam { get; set; }

    public decimal KdvToplam { get; set; }

    public decimal GenelToplam { get; set; }

    public decimal OdenenToplam { get; set; }

    public FaturaDurumu Durum { get; set; } = FaturaDurumu.Bekliyor;

    public string Aciklama { get; set; } = "";

    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;

    public List<FaturaKalem> Kalemler { get; set; } = new();

    [NotMapped]
    public decimal KalanTutar => GenelToplam - OdenenToplam;
}

public static class FaturaDurumuExtensions
{
    public static string Metin(this FaturaDurumu durum) => durum switch
    {
        FaturaDurumu.Bekliyor => "Bekliyor",
        FaturaDurumu.Odendi => "Ödendi",
        FaturaDurumu.KismenOdendi => "Kısmen Ödendi",
        FaturaDurumu.Iptal => "İptal",
        _ => "Bekliyor"
    };

    public static FaturaDurumu OdemeDurumu(decimal genelToplam, decimal odenenToplam)
    {
        if (odenenToplam <= 0)
            return FaturaDurumu.Bekliyor;

        return odenenToplam >= genelToplam
            ? FaturaDurumu.Odendi
            : FaturaDurumu.KismenOdendi;
    }
}
