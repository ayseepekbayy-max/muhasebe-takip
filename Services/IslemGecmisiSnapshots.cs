using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Services;

public static class IslemGecmisiSnapshots
{
    public static object Cari(CariKart x) => new
    {
        x.Id,
        x.FirmaId,
        x.Ad,
        x.Unvan,
        x.Telefon,
        x.VergiNo,
        Tip = x.Tip.ToString(),
        x.OlusturmaTarihi
    };

    public static object StokUrun(StokUrun x) => new
    {
        x.Id,
        x.FirmaId,
        x.Ad,
        x.Kod,
        x.Birim,
        x.MinStokSeviyesi
    };

    public static object StokHareket(StokHareket x) => new
    {
        x.Id,
        x.FirmaId,
        x.StokUrunId,
        x.Tarih,
        Tip = x.Tip.ToString(),
        x.Miktar,
        x.BirimFiyat,
        x.KdvOrani,
        x.KoliAdedi,
        x.KoliFiyat,
        x.Aciklama
    };

    public static object KasaHareket(KasaHareket x) => new
    {
        x.Id,
        x.FirmaId,
        x.Tarih,
        Tip = x.Tip.ToString(),
        x.Tutar,
        x.Aciklama,
        x.CariKartId,
        x.FaturaId
    };

    public static object Musteri(Musteri x) => new
    {
        x.Id,
        x.FirmaId,
        x.Ad,
        x.AdSoyad,
        x.Telefon,
        x.Adres
    };

    public static object Calisan(Calisan x) => new
    {
        x.Id,
        x.FirmaId,
        x.Ad,
        x.AdSoyad,
        x.Telefon,
        x.Maas,
        x.Avans,
        x.IseGirisTarihi,
        x.AktifMi,
        x.AyrilisTarihi,
        x.AyrilisNotu
    };

    public static object CalisanHareket(CalisanAvans x) => new
    {
        x.Id,
        x.FirmaId,
        x.CalisanId,
        x.Tarih,
        x.Tutar,
        x.Aciklama,
        Tip = x.Tip.ToString(),
        x.ArsivlendiMi
    };

    public static object MaasArsiv(CalisanMaasArsiv x) => new
    {
        x.Id,
        x.FirmaId,
        x.CalisanId,
        x.DonemBaslangic,
        x.DonemBitis,
        x.ToplamMaas,
        x.ToplamAvans,
        x.KalanMaas,
        x.OdemeTarihi,
        x.Aciklama
    };

    public static object Puantaj(CalisanPuantaj x) => new
    {
        x.Id,
        x.FirmaId,
        x.CalisanId,
        x.Tarih,
        Durum = x.Durum.ToString(),
        x.Not
    };

    public static object Maliyet(MaliyetKaydi x) => new
    {
        x.Id,
        x.FirmaId,
        x.UretimAdi,
        x.UretimAdedi,
        x.PlakaMaliyeti,
        x.BantlamaMaliyeti,
        x.ArkalikMaliyeti,
        x.MalzemeMaliyeti,
        x.ToplamMaliyet,
        x.BirimMaliyet,
        x.Kaynak,
        x.HesapTarihi
    };

    public static object Fatura(Fatura x) => new
    {
        x.Id,
        x.FirmaId,
        x.CariKartId,
        x.FaturaNo,
        Tip = x.Tip.ToString(),
        x.Tarih,
        x.VadeTarihi,
        x.AraToplam,
        x.KdvToplam,
        x.GenelToplam,
        x.OdenenToplam,
        Durum = x.Durum.Metin(),
        x.Aciklama,
        x.OlusturmaTarihi,
        Kalemler = x.Kalemler.Select(kalem => new
        {
            kalem.Id,
            kalem.Aciklama,
            kalem.Miktar,
            kalem.BirimFiyat,
            kalem.KdvOrani,
            kalem.AraToplam,
            kalem.KdvTutar,
            kalem.GenelToplam
        }).ToList()
    };
}
