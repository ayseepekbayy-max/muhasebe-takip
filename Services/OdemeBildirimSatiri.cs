namespace MuhasebeTakip2.App.Services;

public class OdemeBildirimSatiri
{
    public int OdemePlaniId { get; set; }
    public int? CekId { get; set; }
    public string KaynakTuru { get; set; } = "Ödeme";
    public string OdemeAdi { get; set; } = "";
    public string Detay { get; set; } = "";
    public string Url { get; set; } = "";
    public decimal Tutar { get; set; }
    public DateTime SonOdemeTarihi { get; set; }
    public int KalanGun { get; set; }
    public int Oncelik { get; set; }
    public string Durum { get; set; } = "";
    public string RenkSinifi { get; set; } = "";
    public string GunBilgisi { get; set; } = "";
}
