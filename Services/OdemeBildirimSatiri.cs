namespace MuhasebeTakip2.App.Services;

public class OdemeBildirimSatiri
{
    public int OdemePlaniId { get; set; }
    public string OdemeAdi { get; set; } = "";
    public decimal Tutar { get; set; }
    public DateTime SonOdemeTarihi { get; set; }
    public int KalanGun { get; set; }
    public int Oncelik { get; set; }
    public string Durum { get; set; } = "";
    public string RenkSinifi { get; set; } = "";
    public string GunBilgisi { get; set; } = "";
}
